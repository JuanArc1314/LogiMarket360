using LogiMarket.Application.Comun;
using LogiMarket.Application.Puertos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LogiMarket.Infrastructure.Persistencia;

//unit of work real: DbContext + transaccion SQLite (begin immediate, serializa escrituras)
//traduce excepciones de EF/SQLite a excepciones de la aplicacion (la capa interna no conoce EF)
internal sealed class UnidadDeTrabajoEf : IUnidadDeTrabajo
{
    private readonly LogiMarketDbContext _contexto;

    public UnidadDeTrabajoEf(LogiMarketDbContext contexto) => _contexto = contexto;

    public async Task<T> EjecutarEnTransaccionAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default)
    {
        if (_contexto.Database.CurrentTransaction is not null)
            return await operacion(ct); //se une a la transaccion en curso

        await using var transaccion = await _contexto.Database.BeginTransactionAsync(ct);
        try
        {
            var resultado = await operacion(ct);
            await _contexto.SaveChangesAsync(ct);
            await transaccion.CommitAsync(ct);
            return resultado;
        }
        catch (Exception ex)
        {
            try { await transaccion.RollbackAsync(CancellationToken.None); } catch { /* conexion ya revertida */ }
            _contexto.ChangeTracker.Clear();
            var traducida = Traducir(ex);
            if (traducida is null) throw;
            throw traducida;
        }
    }

    public async Task GuardarCambiosAsync(CancellationToken ct = default)
    {
        try
        {
            await _contexto.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (Traducir(ex) is { } traducida)
        {
            if (_contexto.Database.CurrentTransaction is null) _contexto.ChangeTracker.Clear();
            throw traducida;
        }
    }

    public void DescartarCambios() => _contexto.ChangeTracker.Clear();

    internal static Exception? Traducir(Exception ex) => ex switch
    {
        DbUpdateConcurrencyException c =>
            new ConflictoConcurrenciaException("Otro proceso modificó los mismos datos; la operación se revirtió.", c),
        DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } s } u
            when s.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) =>
            new ViolacionUnicidadException($"Registro duplicado: {s.Message}", u),
        DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 19 } s } u =>
            new ExcepcionAplicacion("RESTRICCION", $"Se violó una restricción de integridad: {s.Message}", u),
        _ => null
    };
}
