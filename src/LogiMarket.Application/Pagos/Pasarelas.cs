using LogiMarket.Application.Comun;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Application.Pagos;

//resultado contractual de un cobro: si es aprobado, siempre trae referencia
public sealed record ResultadoCobro(bool Aprobado, string? Referencia, string Proveedor, string? Motivo)
{
    public static ResultadoCobro Aprobar(string referencia, string proveedor) => new(true, referencia, proveedor, null);
    public static ResultadoCobro Rechazar(string motivo, string proveedor) => new(false, null, proveedor, motivo);
}

public sealed record ResultadoReembolso(bool Aprobado, string? Referencia, string? Motivo);

//puerto de cobro (target del adapter)
//contrato (LSP) para toda implementacion: pre: monto > 0 con max 2 decimales y clave no vacia
//(si no, ArgumentException)
//post: aprobado -> referencia no vacia
//un rechazo de negocio se devuelve (no se lanza)
public interface IPasarelaPago
{
    string Nombre { get; }
    Task<ResultadoCobro> CobrarAsync(decimal monto, string claveIdempotencia, CancellationToken ct = default);
}

//ISP: el reembolso es otra interfaz pequeña
//la pasarela legada no sabe reembolsar y no esta obligada a implementar una operacion que no aplica
public interface IPasarelaReembolso
{
    Task<ResultadoReembolso> ReembolsarAsync(string referenciaCobro, decimal monto, string claveIdempotencia,
        CancellationToken ct = default);
}

//validacion unica de precondiciones del contrato (DRY entre proveedores)
public static class ContratoPasarela
{
    public static void ValidarEntrada(decimal monto, string claveIdempotencia)
    {
        if (monto <= 0m || Dinero.Redondear(monto) != monto)
            throw new ArgumentOutOfRangeException(nameof(monto), "El monto debe ser positivo y tener máximo 2 decimales.");
        if (string.IsNullOrWhiteSpace(claveIdempotencia))
            throw new ArgumentException("La clave de idempotencia es obligatoria.", nameof(claveIdempotencia));
    }
}

//patron proxy (de proteccion)
//verifica rol VENTAS/ADMIN antes de delegar en el decorator y el adapter
//un rol CONSULTA/BODEGA nunca alcanza la pasarela ni produce LOG de inicio/fin de cobro
//el intento se registra en la auditoria de seguridad separada
public sealed class PagoAutorizadoProxy : IPasarelaPago
{
    private readonly IPasarelaPago _real;
    private readonly ISesionUsuario _sesion;
    private readonly IBitacoraOperativa _bitacora;

    public PagoAutorizadoProxy(IPasarelaPago real, ISesionUsuario sesion, IBitacoraOperativa bitacora)
    {
        _real = real;
        _sesion = sesion;
        _bitacora = bitacora;
    }

    public string Nombre => _real.Nombre;

    public Task<ResultadoCobro> CobrarAsync(decimal monto, string claveIdempotencia, CancellationToken ct = default)
    {
        var usuario = _sesion.Actual;
        if (usuario is null || !MatrizPermisos.Permite(usuario.Rol, Permiso.CobrarPedidos))
        {
            _bitacora.Registrar(CategoriasBitacora.Seguridad, "COBRO_DENEGADO",
                $"Proxy bloqueó cobro de {Dinero.Formatear(monto)} para {usuario?.NombreUsuario ?? "anonimo"} " +
                $"(rol {usuario?.Rol.ToString() ?? "ninguno"}).");
            throw new AccesoDenegadoException(
                $"El rol {usuario?.Rol.ToString() ?? "anónimo"} no está autorizado para cobrar.");
        }
        return _real.CobrarAsync(monto, claveIdempotencia, ct);
    }
}

//patron decorator
//envuelve cualquier IPasarelaPago y registra inicio -> (pasarela) -> fin/error con un
//identificador de correlacion, sin alterar el resultado del cobro concreto
public sealed class AuditoriaPagoDecorator : IPasarelaPago
{
    private readonly IPasarelaPago _interna;
    private readonly IBitacoraOperativa _bitacora;

    public AuditoriaPagoDecorator(IPasarelaPago interna, IBitacoraOperativa bitacora)
    {
        _interna = interna;
        _bitacora = bitacora;
    }

    public string Nombre => _interna.Nombre;

    public async Task<ResultadoCobro> CobrarAsync(decimal monto, string claveIdempotencia, CancellationToken ct = default)
    {
        var correlacion = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        using var _ = ContextoCorrelacion.Establecer(correlacion);
        _bitacora.Registrar(CategoriasBitacora.Pago, "INICIO",
            $"Cobro {Dinero.Formatear(monto)} clave={claveIdempotencia} vía {_interna.Nombre}", correlacion);
        try
        {
            var resultado = await _interna.CobrarAsync(monto, claveIdempotencia, ct);
            _bitacora.Registrar(CategoriasBitacora.Pago, "FIN",
                resultado.Aprobado
                    ? $"APROBADO ref={resultado.Referencia} ({resultado.Proveedor})"
                    : $"RECHAZADO: {resultado.Motivo} ({resultado.Proveedor})", correlacion);
            return resultado;
        }
        catch (Exception ex)
        {
            _bitacora.Registrar(CategoriasBitacora.Pago, "ERROR", $"{ex.GetType().Name}: {ex.Message}", correlacion);
            throw;
        }
    }
}
