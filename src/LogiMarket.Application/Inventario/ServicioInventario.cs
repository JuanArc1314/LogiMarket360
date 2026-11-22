using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Inventario;

/// Única regla de movimiento de existencias por venta/devolución (DRY entre confirmación y devolución).
/// Cada cambio de stock deja un MovimientoInventario con signo, motivo y referencia.
public sealed class ServicioInventario
{
    private readonly IRepositorioMovimientos _movimientos;

    public ServicioInventario(IRepositorioMovimientos movimientos) => _movimientos = movimientos;

    public void DescontarPorVenta(Pedido pedido, IReadOnlyDictionary<string, ProductoSimple> productos,
        DateTime fecha, string usuario)
    {
        foreach (var (sku, cantidad) in pedido.RequerimientosStock())
        {
            var producto = Buscar(productos, sku);
            producto.DescontarStock(cantidad);
            _movimientos.Agregar(new MovimientoInventario(producto.Id, sku, -cantidad, TipoMovimiento.VENTA,
                pedido.Codigo, producto.Stock, fecha, usuario));
        }
    }

    public void ReintegrarPorDevolucion(Pedido pedido, IReadOnlyDictionary<string, ProductoSimple> productos,
        DateTime fecha, string usuario)
    {
        foreach (var (sku, cantidad) in pedido.RequerimientosStock())
        {
            var producto = Buscar(productos, sku);
            producto.ReintegrarStock(cantidad);
            _movimientos.Agregar(new MovimientoInventario(producto.Id, sku, cantidad, TipoMovimiento.DEVOLUCION,
                pedido.Codigo, producto.Stock, fecha, usuario));
        }
    }

    private static ProductoSimple Buscar(IReadOnlyDictionary<string, ProductoSimple> productos, string sku) =>
        productos.TryGetValue(sku, out var producto)
            ? producto
            : throw new ExcepcionDominio("PRODUCTO_INEXISTENTE", $"El producto {sku} ya no existe en el catálogo.");
}
