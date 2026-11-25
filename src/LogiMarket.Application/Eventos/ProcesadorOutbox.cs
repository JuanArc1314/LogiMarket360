using LogiMarket.Application.Comun;
using LogiMarket.Application.Puertos;

namespace LogiMarket.Application.Eventos;

public sealed record ResultadoProcesamientoOutbox(int Procesados, int Fallidos, IReadOnlyList<string> Detalle);

//procesa eventos pendientes del outbox despues de que su transaccion de negocio se confirmo
//cada evento se publica a los observadores
//si alguno falla, el evento queda pendiente con su error y se reintenta en la siguiente
//ejecucion (recuperacion)
//un evento se marca procesado una sola vez
public sealed class ProcesadorOutbox
{
    //evita que dos procesamientos simultaneos (temporizador y boton) tomen el mismo evento
    private static readonly SemaphoreSlim Candado = new(1, 1);

    private readonly IRepositorioOutbox _outbox;
    private readonly IPublicadorEventos _publicador;
    private readonly SerializadorEventos _serializador;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IReloj _reloj;
    private readonly IBitacoraOperativa _bitacora;

    public ProcesadorOutbox(IRepositorioOutbox outbox, IPublicadorEventos publicador, SerializadorEventos serializador,
        IUnidadDeTrabajo unidad, IReloj reloj, IBitacoraOperativa bitacora)
    {
        _outbox = outbox;
        _publicador = publicador;
        _serializador = serializador;
        _unidad = unidad;
        _reloj = reloj;
        _bitacora = bitacora;
    }

    public async Task<ResultadoProcesamientoOutbox> ProcesarPendientesAsync(int maximo = 50, CancellationToken ct = default)
    {
        await Candado.WaitAsync(ct);
        try
        {
            var detalle = new List<string>();
            int procesados = 0, fallidos = 0;
            foreach (var id in await _outbox.ObtenerIdsPendientesAsync(maximo, ct))
            {
                var registro = await _outbox.ObtenerAsync(id, ct);
                if (registro is null || registro.Procesado) continue;
                try
                {
                    var evento = _serializador.Leer(registro);
                    var publicacion = await _publicador.PublicarAsync(evento, ct);
                    if (!publicacion.Completo)
                        throw new InvalidOperationException(string.Join(" | ", publicacion.Fallos));

                    registro.MarcarProcesado(_reloj.Ahora);
                    await _unidad.GuardarCambiosAsync(ct); //efectos de observadores + marca, juntos
                    procesados++;
                    detalle.Add($"{registro.Tipo} {registro.Referencia}: procesado ({string.Join(", ", publicacion.Notificados)})");
                    _bitacora.Registrar(CategoriasBitacora.Outbox, "PROCESADO", $"{registro.Tipo} {registro.Referencia} [{id}]");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _unidad.DescartarCambios();
                    var recargado = await _outbox.ObtenerAsync(id, ct);
                    recargado?.RegistrarFallo(ex.Message);
                    await _unidad.GuardarCambiosAsync(ct);
                    fallidos++;
                    detalle.Add($"{registro.Tipo} {registro.Referencia}: PENDIENTE ({ex.Message})");
                    _bitacora.Registrar(CategoriasBitacora.Outbox, "FALLO", $"{registro.Tipo} {registro.Referencia}: {ex.Message}");
                }
            }
            return new ResultadoProcesamientoOutbox(procesados, fallidos, detalle);
        }
        finally
        {
            Candado.Release();
        }
    }
}
