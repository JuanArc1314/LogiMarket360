using System.Text.Json;
using LogiMarket.Domain.Eventos;

namespace LogiMarket.Application.Eventos;

//convierte eventos de negocio a registros outbox y viceversa (JSON
//sin tipos dinamicos inseguros)
public sealed class SerializadorEventos
{
    private static readonly IReadOnlyDictionary<string, Type> Tipos = new Dictionary<string, Type>
    {
        [nameof(PedidoPagado)] = typeof(PedidoPagado),
        [nameof(DespachoRegistrado)] = typeof(DespachoRegistrado),
        [nameof(DevolucionAceptada)] = typeof(DevolucionAceptada)
    };

    public EventoOutbox CrearRegistro(IEventoNegocio evento) =>
        new(evento.Id, evento.Tipo, JsonSerializer.Serialize(evento, evento.GetType()), evento.Fecha, evento.CodigoPedido);

    public IEventoNegocio Leer(EventoOutbox registro)
    {
        if (!Tipos.TryGetValue(registro.Tipo, out var tipo))
            throw new InvalidOperationException($"Tipo de evento desconocido: {registro.Tipo}");
        return (IEventoNegocio)(JsonSerializer.Deserialize(registro.Contenido, tipo)
            ?? throw new InvalidOperationException($"Contenido de evento vacío: {registro.Id}"));
    }
}
