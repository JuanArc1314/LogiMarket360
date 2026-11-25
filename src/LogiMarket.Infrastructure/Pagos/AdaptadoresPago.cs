using LogiMarket.Application.Comun;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Pagos;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Infrastructure.Pagos;

//patron adapter (de objeto)
//convierte CobrarAsync(decimal, clave) en PasarelaLegadaSimulada.CobrarCentavos y traduce sus
//codigos al resultado contractual, sin modificar el codigo legado
public sealed class PasarelaLegadaAdapter : IPasarelaPago
{
    private readonly PasarelaLegadaSimulada _legado;
    private readonly IBitacoraOperativa _bitacora;

    public PasarelaLegadaAdapter(PasarelaLegadaSimulada legado, IBitacoraOperativa bitacora)
    {
        _legado = legado;
        _bitacora = bitacora;
    }

    public string Nombre => ProveedoresPago.Legado;

    public Task<ResultadoCobro> CobrarAsync(decimal monto, string claveIdempotencia, CancellationToken ct = default)
    {
        ContratoPasarela.ValidarEntrada(monto, claveIdempotencia);
        var centavos = (long)decimal.Round(monto * 100m, 0, MidpointRounding.AwayFromZero);
        var respuesta = _legado.CobrarCentavos(centavos, claveIdempotencia);
        _bitacora.Registrar(CategoriasBitacora.Pago, "PASARELA",
            $"LEGADO.CobrarCentavos({centavos}, {claveIdempotencia}) → {respuesta.CodigoRespuesta} {respuesta.Mensaje}",
            ContextoCorrelacion.Actual);

        return Task.FromResult(respuesta.CodigoRespuesta == "00"
            ? ResultadoCobro.Aprobar(respuesta.NumeroAutorizacion!, Nombre)
            : ResultadoCobro.Rechazar($"{respuesta.CodigoRespuesta} - {respuesta.Mensaje}", Nombre));
    }
}

//segundo proveedor simulado con api moderna, sustituible por DI (mismo contrato)
public sealed class PasarelaAlternaSimulada : IPasarelaPago
{
    private readonly ConfiguracionDemostracion _configuracion;
    private readonly IBitacoraOperativa _bitacora;
    private int _secuencia;

    public PasarelaAlternaSimulada(ConfiguracionDemostracion configuracion, IBitacoraOperativa bitacora)
    {
        _configuracion = configuracion;
        _bitacora = bitacora;
    }

    public string Nombre => ProveedoresPago.Alterno;

    public Task<ResultadoCobro> CobrarAsync(decimal monto, string claveIdempotencia, CancellationToken ct = default)
    {
        ContratoPasarela.ValidarEntrada(monto, claveIdempotencia);
        var resultado = _configuracion.SimularRechazoCobro
            ? ResultadoCobro.Rechazar("Tarjeta rechazada por el emisor (simulado)", Nombre)
            : ResultadoCobro.Aprobar($"ALT-{DateTime.Now:yyyyMMdd}-{Interlocked.Increment(ref _secuencia):00000}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}", Nombre);
        _bitacora.Registrar(CategoriasBitacora.Pago, "PASARELA",
            $"ALTERNO.charge({Dinero.Formatear(monto)}) → {(resultado.Aprobado ? "approved " + resultado.Referencia : "declined")}",
            ContextoCorrelacion.Actual);
        return Task.FromResult(resultado);
    }
}

//selecciona el proveedor simulado activo (configurable desde la pantalla de demostracion)
public sealed class PasarelaConmutable : IPasarelaPago
{
    private readonly PasarelaLegadaAdapter _legado;
    private readonly PasarelaAlternaSimulada _alterno;
    private readonly ConfiguracionDemostracion _configuracion;

    public PasarelaConmutable(PasarelaLegadaAdapter legado, PasarelaAlternaSimulada alterno, ConfiguracionDemostracion configuracion)
    {
        _legado = legado;
        _alterno = alterno;
        _configuracion = configuracion;
    }

    private IPasarelaPago Actual => _configuracion.ProveedorPago == ProveedoresPago.Alterno ? _alterno : _legado;

    public string Nombre => Actual.Nombre;

    public Task<ResultadoCobro> CobrarAsync(decimal monto, string claveIdempotencia, CancellationToken ct = default) =>
        Actual.CobrarAsync(monto, claveIdempotencia, ct);
}

//reembolso/anulacion simulada (interfaz segregada
//el legado no la implementa)
public sealed class ServicioReembolsoSimulado : IPasarelaReembolso
{
    private readonly IBitacoraOperativa _bitacora;
    private int _secuencia;

    public ServicioReembolsoSimulado(IBitacoraOperativa bitacora) => _bitacora = bitacora;

    public Task<ResultadoReembolso> ReembolsarAsync(string referenciaCobro, decimal monto, string claveIdempotencia,
        CancellationToken ct = default)
    {
        ContratoPasarela.ValidarEntrada(monto, claveIdempotencia);
        var referencia = $"REM-{DateTime.Now:yyyyMMddHHmmss}-{Interlocked.Increment(ref _secuencia):0000}";
        _bitacora.Registrar(CategoriasBitacora.Pago, "REEMBOLSO",
            $"Reembolso {Dinero.Formatear(monto)} sobre {referenciaCobro} → {referencia}");
        return Task.FromResult(new ResultadoReembolso(true, referencia, null));
    }
}
