using LogiMarket.Application.Comun;
using LogiMarket.Application.Confirmacion;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using LogiMarket.Infrastructure;
using LogiMarket.Infrastructure.Persistencia;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.Tests.Soporte;

//reloj controlable para reproducir plazos (dia 20, dia 10...)
public sealed class RelojControlado : IReloj
{
    public DateTime Ahora { get; set; } = new(2026, 11, 18, 10, 0, 0);
    public void AvanzarDias(int dias) => Ahora = Ahora.AddDays(dias);
}

//base SQLite aislada por prueba (archivo temporal), creada con migraciones + seed
//cada operacion usa un ambito DI nuevo, igual que la aplicacion de escritorio
public sealed class EntornoPrueba : IAsyncDisposable
{
    private EntornoPrueba(string directorio, ServiceProvider proveedor, RelojControlado reloj)
    {
        Directorio = directorio;
        Proveedor = proveedor;
        Reloj = reloj;
    }

    public string Directorio { get; }
    public ServiceProvider Proveedor { get; }
    public RelojControlado Reloj { get; }
    public OpcionesInfraestructura Opciones => Proveedor.GetRequiredService<OpcionesInfraestructura>();
    public ISesionUsuario Sesion => Proveedor.GetRequiredService<ISesionUsuario>();

    public static async Task<EntornoPrueba> CrearAsync(RolUsuario? rol = RolUsuario.ADMIN, string? directorio = null,
        Action<IServiceCollection>? configurar = null)
    {
        directorio ??= Path.Combine(Path.GetTempPath(), "logimarket-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directorio);
        var reloj = new RelojControlado();
        var servicios = new ServiceCollection();
        servicios.AgregarLogiMarket(new OpcionesInfraestructura(directorio) { IteracionesHash = 1_000 });
        servicios.AddSingleton<IReloj>(reloj); //reemplaza el reloj del sistema
        configurar?.Invoke(servicios);
        var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        var entorno = new EntornoPrueba(directorio, proveedor, reloj);
        await entorno.EnAmbitoAsync(sp => sp.GetRequiredService<InicializadorBaseDatos>().InicializarAsync());
        if (rol is { } r) entorno.IniciarSesion(r);
        return entorno;
    }

    public void IniciarSesion(RolUsuario rol) =>
        Sesion.Iniciar(new UsuarioSesion((int)rol + 1, rol.ToString().ToLowerInvariant(), $"Prueba {rol}", rol));

    public T Servicio<T>() where T : notnull => Proveedor.GetRequiredService<T>();

    public async Task EnAmbitoAsync(Func<IServiceProvider, Task> accion)
    {
        await using var ambito = Proveedor.CreateAsyncScope();
        await accion(ambito.ServiceProvider);
    }

    public async Task<T> EnAmbitoAsync<T>(Func<IServiceProvider, Task<T>> accion)
    {
        await using var ambito = Proveedor.CreateAsyncScope();
        return await accion(ambito.ServiceProvider);
    }

    //ayudantes de escenario

    public Task<int> ClienteIdAsync(string nombre) => EnAmbitoAsync(async sp =>
        (await sp.GetRequiredService<IConsultasOperativas>().ListarClientesAsync(nombre)).Single().Id);

    public async Task<PedidoGuardadoDto> CrearPedidoAsync(string cliente, TipoEntrega entrega, params (string Sku, int Cantidad)[] lineas)
    {
        var clienteId = await ClienteIdAsync(cliente);
        return await EnAmbitoAsync(sp => sp.GetRequiredService<ServicioPedidos>().GuardarBorradorAsync(
            new BorradorDto(null, clienteId, entrega, lineas.Select(l => new LineaBorradorDto(l.Sku, l.Cantidad)).ToList())));
    }

    public Task<ResultadoConfirmacion> ConfirmarAsync(int pedidoId, string? clave = null) =>
        EnAmbitoAsync(sp => sp.GetRequiredService<ConfirmacionPedidoFacade>().ConfirmarAsync(pedidoId, clave ?? $"CLAVE-{Guid.NewGuid():N}"));

    public Task<int> StockAsync(string sku) => EnAmbitoAsync(async sp =>
        (await sp.GetRequiredService<IConsultasOperativas>().ListarProductosAsync()).Single(p => p.Sku == sku).Stock);

    public async Task<Dictionary<string, int>> StocksAsync() => await EnAmbitoAsync(async sp =>
        (await sp.GetRequiredService<IConsultasOperativas>().ListarProductosAsync()).ToDictionary(p => p.Sku, p => p.Stock));

    public Task<PedidoDetalleDto> PedidoAsync(int id) => EnAmbitoAsync(async sp =>
        (await sp.GetRequiredService<IConsultasOperativas>().ObtenerPedidoAsync(id))!);

    public Task<T> ContarAsync<T>(Func<LogiMarketDbContext, T> consulta) => EnAmbitoAsync(sp =>
        Task.FromResult(consulta(sp.GetRequiredService<LogiMarketDbContext>())));

    public async ValueTask DisposeAsync()
    {
        await Proveedor.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Directorio, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
