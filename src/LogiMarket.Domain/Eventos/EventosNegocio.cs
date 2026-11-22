using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Eventos;

/// Evento de negocio publicado después de confirmar la transacción (vía Outbox).
public interface IEventoNegocio
{
    Guid Id { get; }
    DateTime Fecha { get; }
    string Tipo { get; }
    string CodigoPedido { get; }
    string ClienteNombre { get; }
    string ClienteEmail { get; }
    string ClienteTelefono { get; }
    string Describir();
}

public sealed record PedidoPagado(
    Guid Id, DateTime Fecha, int PedidoId, string CodigoPedido,
    string ClienteNombre, string ClienteEmail, string ClienteTelefono,
    decimal Total, string ReferenciaPago) : IEventoNegocio
{
    public string Tipo => nameof(PedidoPagado);
    public string Describir() => $"Pedido {CodigoPedido} pagado por {Dinero.Formatear(Total)} (ref. {ReferenciaPago}).";
}

public sealed record DespachoRegistrado(
    Guid Id, DateTime Fecha, int PedidoId, string CodigoPedido,
    string ClienteNombre, string ClienteEmail, string ClienteTelefono,
    string NumeroGuia, DateTime FechaEstimada) : IEventoNegocio
{
    public string Tipo => nameof(DespachoRegistrado);
    public string Describir() => $"Pedido {CodigoPedido} despachado con guía {NumeroGuia}; llega {FechaEstimada:dd/MM/yyyy}.";
}

public sealed record DevolucionAceptada(
    Guid Id, DateTime Fecha, int PedidoId, string CodigoPedido,
    string ClienteNombre, string ClienteEmail, string ClienteTelefono,
    string Motivo, decimal MontoReembolso, string ReferenciaReembolso) : IEventoNegocio
{
    public string Tipo => nameof(DevolucionAceptada);
    public string Describir() =>
        $"Devolución de {CodigoPedido} ({Motivo}) aceptada; reembolso {Dinero.Formatear(MontoReembolso)} (ref. {ReferenciaReembolso}).";
}

/// Evento pendiente (patrón Outbox). Se inserta en la MISMA transacción que la operación de negocio
/// y se procesa después; registra intentos y errores para su recuperación.
public sealed class EventoOutbox
{
    private EventoOutbox() { }

    public EventoOutbox(Guid id, string tipo, string contenido, DateTime fecha, string referencia)
    {
        Id = id;
        Tipo = Guardia.Texto(tipo, "El tipo de evento", 60);
        Contenido = contenido;
        FechaCreacion = fecha;
        Referencia = referencia;
    }

    public Guid Id { get; private set; }
    public string Tipo { get; private set; } = string.Empty;
    public string Contenido { get; private set; } = string.Empty;
    public string Referencia { get; private set; } = string.Empty;
    public DateTime FechaCreacion { get; private set; }
    public DateTime? FechaProcesado { get; private set; }
    public int Intentos { get; private set; }
    public string? UltimoError { get; private set; }

    public bool Procesado => FechaProcesado is not null;

    public void MarcarProcesado(DateTime fecha)
    {
        if (Procesado)
            throw new ExcepcionDominio("EVENTO_YA_PROCESADO", "El evento ya fue procesado.");
        Intentos++;
        FechaProcesado = fecha;
        UltimoError = null;
    }

    public void RegistrarFallo(string error)
    {
        Intentos++;
        UltimoError = error.Length > 400 ? error[..400] : error;
    }
}
