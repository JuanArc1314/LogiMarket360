using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Pedidos;

/// PATRÓN PROTOTYPE — contrato de clonación.
public interface IPrototipo<out T>
{
    T Clonar();
}

/// PATRÓN MEMENTO — Memento. Estado completo e inmutable de un borrador (cliente, entrega y líneas
/// anidadas). Su contenido solo lo lee el originador (<see cref="Pedido"/>); el exterior ve un resumen.
public sealed class PedidoMemento
{
    internal PedidoMemento(int clienteId, string clienteNombre, CategoriaCliente categoria,
        TipoEntrega entrega, IReadOnlyList<PedidoDetalle> lineas)
    {
        ClienteId = clienteId;
        ClienteNombre = clienteNombre;
        CategoriaCliente = categoria;
        Entrega = entrega;
        Lineas = lineas;
        Resumen = $"{clienteNombre} · {entrega} · " +
                  (lineas.Count == 0 ? "sin líneas" : string.Join(", ", lineas.Select(l => $"{l.Sku}×{l.Cantidad}")));
    }

    internal int ClienteId { get; }
    internal string ClienteNombre { get; }
    internal CategoriaCliente CategoriaCliente { get; }
    internal TipoEntrega Entrega { get; }
    internal IReadOnlyList<PedidoDetalle> Lineas { get; }

    public string Resumen { get; }
}

/// PATRÓN MEMENTO — Cuidador (caretaker). Deshacer/rehacer hasta <see cref="Capacidad"/> estados.
/// Al confirmar el pedido se bloquea y se limpia: no permite revertir un cobro ni stock.
public sealed class HistorialEdiciones
{
    public const int Capacidad = 5;

    private readonly LinkedList<PedidoMemento> _deshacer = new();
    private readonly Stack<PedidoMemento> _rehacer = new();

    public bool Bloqueado { get; private set; }
    public bool PuedeDeshacer => !Bloqueado && _deshacer.Count > 0;
    public bool PuedeRehacer => !Bloqueado && _rehacer.Count > 0;
    public int EstadosGuardados => _deshacer.Count;

    /// Capturar ANTES de modificar el borrador.
    public void Capturar(Pedido pedido) => Registrar(pedido.CrearMemento());

    /// Guarda un memento ya creado (estado previo a un cambio que se aplicó con éxito).
    public void Registrar(PedidoMemento memento)
    {
        ArgumentNullException.ThrowIfNull(memento);
        ExigirDesbloqueado();
        _deshacer.AddLast(memento);
        if (_deshacer.Count > Capacidad) _deshacer.RemoveFirst();
        _rehacer.Clear();
    }

    public void Deshacer(Pedido pedido)
    {
        ExigirDesbloqueado();
        ExigirBorrador(pedido);
        if (_deshacer.Last is null)
            throw new ExcepcionDominio("SIN_HISTORIAL", "No hay cambios para deshacer.");
        var anterior = _deshacer.Last.Value;
        _deshacer.RemoveLast();
        _rehacer.Push(pedido.CrearMemento());
        pedido.RestaurarMemento(anterior);
    }

    public void Rehacer(Pedido pedido)
    {
        ExigirDesbloqueado();
        ExigirBorrador(pedido);
        if (_rehacer.Count == 0)
            throw new ExcepcionDominio("SIN_HISTORIAL", "No hay cambios para rehacer.");
        _deshacer.AddLast(pedido.CrearMemento());
        if (_deshacer.Count > Capacidad) _deshacer.RemoveFirst();
        pedido.RestaurarMemento(_rehacer.Pop());
    }

    public void BloquearYLimpiar()
    {
        _deshacer.Clear();
        _rehacer.Clear();
        Bloqueado = true;
    }

    private static void ExigirBorrador(Pedido pedido)
    {
        if (pedido.Estado != EstadoPedido.BORRADOR)
            throw new ExcepcionDominio("MEMENTO_BLOQUEADO",
                "Solo se puede deshacer/rehacer un borrador; un pedido confirmado no se revierte.");
    }

    private void ExigirDesbloqueado()
    {
        if (Bloqueado)
            throw new ExcepcionDominio("MEMENTO_BLOQUEADO", "El historial está bloqueado: el pedido ya fue confirmado.");
    }
}

/// Resultado de valorar un pedido (subtotal, descuento por estrategia y tarifa por familia).
public sealed record Valoracion(
    decimal Subtotal,
    string Politica,
    decimal TasaDescuento,
    decimal Descuento,
    TipoEntrega Familia,
    decimal Tarifa,
    int PlazoDias)
{
    public decimal Total => Dinero.Redondear(Subtotal - Descuento + Tarifa);
    public decimal ImporteProductosNeto => Dinero.Redondear(Subtotal - Descuento);
}
