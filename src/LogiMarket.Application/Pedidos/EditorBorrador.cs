using LogiMarket.Application.Comun;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Pedidos;

/// Edición en memoria de un borrador con DESHACER/REHACER (PATRÓN MEMENTO: el editor actúa de cliente,
/// HistorialEdicioneses el cuidador y "Pedido el originador).
/// Cada modificación captura el estado completo ANTES de aplicarse.
public sealed class EditorBorrador
{
    private readonly ValoradorPedidos _valorador;
    private readonly IReloj _reloj;
    private Cliente _cliente;

    internal EditorBorrador(Cliente cliente, Pedido? existente, ValoradorPedidos valorador, IReloj reloj)
    {
        _cliente = cliente;
        _valorador = valorador;
        _reloj = reloj;
        Pedido = existente;
        PedidoId = existente?.Id;
        Entrega = existente?.Entrega ?? TipoEntrega.ESTANDAR;
    }

    public Pedido? Pedido { get; private set; }
    public int? PedidoId { get; private set; }
    public TipoEntrega Entrega { get; private set; }
    public HistorialEdiciones Historial { get; } = new();
    public Cliente Cliente => _cliente;

    public IReadOnlyList<PedidoDetalle> Lineas => Pedido?.Lineas ?? [];
    public bool PuedeDeshacer => Historial.PuedeDeshacer;
    public bool PuedeRehacer => Historial.PuedeRehacer;

    public Valoracion? Valoracion => Pedido is { Lineas.Count: > 0 } p ? _valorador.Valorar(p) : null;

    public void Agregar(IElementoCatalogo elemento, int cantidad)
    {
        if (Pedido is null)
        {
            Pedido = new PedidoBuilder().ParaCliente(_cliente).ConEntrega(Entrega)
                .RegistradoPor("editor", _reloj.Ahora).AgregarElemento(elemento, cantidad).Construir();
            return;
        }
        Modificar(p => p.AgregarLinea(elemento, cantidad));
    }

    public void CambiarCantidad(string sku, int cantidad) => Modificar(p => p.CambiarCantidad(sku, cantidad));

    public void Quitar(string sku) => Modificar(p => p.QuitarLinea(sku));

    public void CambiarEntrega(TipoEntrega entrega)
    {
        Entrega = entrega;
        if (Pedido is not null) Modificar(p => p.CambiarEntrega(entrega));
    }

    public void CambiarCliente(Cliente cliente)
    {
        _cliente = cliente;
        if (Pedido is not null) Modificar(p => p.CambiarCliente(cliente));
    }

    public void Deshacer()
    {
        Historial.Deshacer(ExigirPedido());
        Entrega = Pedido!.Entrega;
    }

    public void Rehacer()
    {
        Historial.Rehacer(ExigirPedido());
        Entrega = Pedido!.Entrega;
    }

    /// Tras confirmar el pedido, el historial se bloquea: no se deshace un cobro.
    public void BloquearHistorial() => Historial.BloquearYLimpiar();

    public void MarcarGuardado(int pedidoId) => PedidoId = pedidoId;

    public BorradorDto ObtenerContenido() =>
        new(PedidoId, _cliente.Id, Entrega, Lineas.Select(l => new LineaBorradorDto(l.Sku, l.Cantidad)).ToList());

    private void Modificar(Action<Pedido> cambio)
    {
        var pedido = ExigirPedido();
        Historial.Capturar(pedido);
        try
        {
            cambio(pedido);
        }
        catch
        {
            Historial.Deshacer(pedido); // el cambio inválido no deja rastro en el borrador
            throw;
        }
    }

    private Pedido ExigirPedido() =>
        Pedido ?? throw new ExcepcionDominio("BORRADOR_VACIO", "Agregue al menos un producto al borrador.");
}
