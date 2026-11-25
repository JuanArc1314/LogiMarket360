using System.Collections.Concurrent;

namespace LogiMarket.Infrastructure.Pagos;

//respuesta del sistema legado (codigos ISO-8583 simplificados: "00" = aprobado)
public sealed record RespuestaLegada(string CodigoRespuesta, string? NumeroAutorizacion, string Mensaje);

//codigo legado (simulado) - no se modifica ===== api incompatible con IPasarelaPago: trabaja en
//centavos (long), con "referencia de comercio" y codigos de respuesta en texto
//la aplicacion nunca la invoca directamente: usa el adapter
public sealed class PasarelaLegadaSimulada
{
    private readonly Func<string?> _terminalPruebas;
    private readonly ConcurrentDictionary<string, RespuestaLegada> _procesadas = new();
    private int _secuencia;

    //terminalPruebas: terminal de pruebas del legado: si devuelve un codigo
    //la transaccion se declina con el
    public PasarelaLegadaSimulada(Func<string?>? terminalPruebas = null) =>
        _terminalPruebas = terminalPruebas ?? (() => null);

    public int TransaccionesProcesadas => _procesadas.Count;

    public RespuestaLegada CobrarCentavos(long centavos, string referenciaComercio)
    {
        if (centavos <= 0) return new RespuestaLegada("13", null, "MONTO INVALIDO");
        if (string.IsNullOrWhiteSpace(referenciaComercio)) return new RespuestaLegada("12", null, "TRANSACCION INVALIDA");
        if (_procesadas.TryGetValue(referenciaComercio, out var previa)) return previa; //el legado deduplica por referencia

        var forzado = _terminalPruebas();
        RespuestaLegada respuesta;
        if (forzado is not null)
            respuesta = new RespuestaLegada(forzado, null, "TRANSACCION DECLINADA POR EMISOR (TERMINAL DE PRUEBAS)");
        else if (centavos > 5_000_000)
            respuesta = new RespuestaLegada("61", null, "EXCEDE LIMITE DE MONTO");
        else
            respuesta = new RespuestaLegada("00",
                $"LEG{DateTime.Now:yyMMddHHmmss}{Interlocked.Increment(ref _secuencia):0000}", "APROBADA");

        if (respuesta.CodigoRespuesta == "00") _procesadas[referenciaComercio] = respuesta;
        return respuesta;
    }
}
