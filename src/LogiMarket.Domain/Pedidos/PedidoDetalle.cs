using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Pedidos;

/// Línea de venta con INSTANTÁNEA de precio y composición (los cambios posteriores del catálogo
/// no alteran el pedido). Un producto simple se guarda como una línea con un único componente.
public sealed class PedidoDetalle
{
    private readonly List<PedidoDetalleComponente> _componentes = new();

    private PedidoDetalle() { } // EF Core

    internal static PedidoDetalle DesdeCatalogo(IElementoCatalogo elemento, int cantidad)
    {
        var detalle = new PedidoDetalle
        {
            Sku = elemento.Sku,
            Descripcion = elemento.Nombre,
            EsKit = elemento.EsCompuesto,
            PrecioUnitario = Dinero.Redondear(elemento.PrecioVenta),
            PesoUnitarioKg = elemento.PesoKg,
            Cantidad = Guardia.CantidadPositiva(cantidad)
        };
        // Iterator: se expande hasta SKU finales y se congela la composición por unidad de la línea.
        foreach (var fisico in ExpansionCatalogo.ExpandirAgregado(elemento))
            detalle._componentes.Add(new PedidoDetalleComponente(
                fisico.Producto.Sku, fisico.Producto.Nombre, fisico.Cantidad,
                fisico.Producto.Precio, fisico.Producto.PesoKg));
        detalle.ActualizarSubtotal();
        return detalle;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public bool EsKit { get; private set; }
    public decimal PrecioUnitario { get; private set; }
    public decimal PesoUnitarioKg { get; private set; }
    public int Cantidad { get; private set; }
    public decimal Subtotal { get; private set; }

    /// Composición física por UNA unidad de la línea.
    public IReadOnlyList<PedidoDetalleComponente> Componentes => _componentes.AsReadOnly();

    public int UnidadesFisicas => _componentes.Sum(c => c.CantidadPorUnidad) * Cantidad;

    internal void CambiarCantidad(int cantidad)
    {
        Cantidad = Guardia.CantidadPositiva(cantidad);
        ActualizarSubtotal();
    }

    internal void Incrementar(int cantidad) => CambiarCantidad(Cantidad + Guardia.CantidadPositiva(cantidad));

    /// Copia profunda (Prototype / Memento): colecciones nuevas, sin identificadores persistidos.
    internal PedidoDetalle ClonarProfundo()
    {
        var copia = new PedidoDetalle
        {
            Sku = Sku,
            Descripcion = Descripcion,
            EsKit = EsKit,
            PrecioUnitario = PrecioUnitario,
            PesoUnitarioKg = PesoUnitarioKg,
            Cantidad = Cantidad,
            Subtotal = Subtotal
        };
        foreach (var c in _componentes)
            copia._componentes.Add(c.Clonar());
        return copia;
    }

    private void ActualizarSubtotal() => Subtotal = Dinero.Redondear(PrecioUnitario * Cantidad);
}

/// Instantánea de un SKU final dentro de una línea (precio y peso al momento del registro).
public sealed class PedidoDetalleComponente
{
    private PedidoDetalleComponente() { } // EF Core

    internal PedidoDetalleComponente(string sku, string nombre, int cantidadPorUnidad, decimal precioUnitario, decimal pesoKg)
    {
        Sku = sku;
        Nombre = nombre;
        CantidadPorUnidad = Guardia.CantidadPositiva(cantidadPorUnidad);
        PrecioUnitario = precioUnitario;
        PesoKg = pesoKg;
    }

    public int Id { get; private set; }
    public int PedidoDetalleId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public int CantidadPorUnidad { get; private set; }
    public decimal PrecioUnitario { get; private set; }
    public decimal PesoKg { get; private set; }

    internal PedidoDetalleComponente Clonar() =>
        new(Sku, Nombre, CantidadPorUnidad, PrecioUnitario, PesoKg);
}

/// Registro inmutable de cada transición de estado (trazabilidad).
public sealed class PedidoHistorialEstado
{
    private PedidoHistorialEstado() { } // EF Core

    internal PedidoHistorialEstado(EstadoPedido? anterior, EstadoPedido nuevo, DateTime fecha, string usuario, string nota)
    {
        EstadoAnterior = anterior;
        EstadoNuevo = nuevo;
        Fecha = fecha;
        Usuario = usuario;
        Nota = nota;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public EstadoPedido? EstadoAnterior { get; private set; }
    public EstadoPedido EstadoNuevo { get; private set; }
    public DateTime Fecha { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
    public string Nota { get; private set; } = string.Empty;
}
