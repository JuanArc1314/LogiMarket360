using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Pedidos;

//patron builder
//ensambla cliente, lineas simples y kits, forma de entrega e instantaneas
//el agregado resultante nace valido: sin cliente o sin lineas no se construye
public sealed class PedidoBuilder
{
    private readonly List<(IElementoCatalogo Elemento, int Cantidad)> _lineas = new();
    private Cliente? _cliente;
    private TipoEntrega _entrega = TipoEntrega.ESTANDAR;
    private DateTime _fecha = DateTime.Now;
    private string _usuario = "sistema";

    public PedidoBuilder ParaCliente(Cliente cliente)
    {
        _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
        return this;
    }

    public PedidoBuilder ConEntrega(TipoEntrega entrega)
    {
        _entrega = entrega;
        return this;
    }

    public PedidoBuilder AgregarProducto(ProductoSimple producto, int cantidad) => AgregarElemento(producto, cantidad);

    public PedidoBuilder AgregarKit(KitCompuesto kit, int cantidad) => AgregarElemento(kit, cantidad);

    public PedidoBuilder AgregarElemento(IElementoCatalogo elemento, int cantidad)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        _lineas.Add((elemento, Guardia.CantidadPositiva(cantidad)));
        return this;
    }

    public PedidoBuilder RegistradoPor(string usuario, DateTime fecha)
    {
        _usuario = usuario;
        _fecha = fecha;
        return this;
    }

    public Pedido Construir()
    {
        if (_cliente is null)
            throw new ExcepcionDominio("PEDIDO_SIN_CLIENTE", "El pedido requiere un cliente.");
        if (_lineas.Count == 0)
            throw new ExcepcionDominio("PEDIDO_SIN_LINEAS", "El pedido requiere al menos una línea.");

        var pedido = new Pedido(_cliente, _entrega, _fecha, _usuario);
        foreach (var (elemento, cantidad) in _lineas)
            pedido.AgregarLinea(elemento, cantidad);
        return pedido;
    }
}
