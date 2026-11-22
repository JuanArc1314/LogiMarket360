using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Consultas;

// Lado de CONSULTA (CQRS básico): DTO de solo lectura, sin entidades de dominio expuestas a la UI.

public sealed record ProductoDto(int Id, string Sku, string Nombre, string Categoria, string Descripcion,
    decimal Precio, decimal PesoKg, int Stock, bool Activo);

public sealed record KitDto(int Id, string Sku, string Nombre, string Descripcion, decimal Precio,
    decimal PesoKg, string Composicion, bool Activo);

public sealed record ElementoVendibleDto(string Sku, string Nombre, decimal Precio, bool EsKit, int? Stock);

public sealed record ClienteDto(int Id, string Identificacion, string Nombre, string Email, string Telefono,
    CategoriaCliente Categoria, bool Activo);

public sealed record PedidoResumenDto(int Id, string Codigo, int ClienteId, string Cliente, EstadoPedido Estado,
    decimal Total, DateTime Fecha, TipoEntrega Entrega, int Unidades, bool Devuelto);

public sealed record LineaPedidoDto(string Sku, string Descripcion, bool EsKit, int Cantidad, decimal PrecioUnitario,
    decimal Subtotal, IReadOnlyList<ComponenteLineaDto> Componentes);

public sealed record ComponenteLineaDto(string Sku, string Nombre, int CantidadPorUnidad, decimal PrecioUnitario);

public sealed record HistorialEstadoDto(DateTime Fecha, EstadoPedido? Anterior, EstadoPedido Nuevo, string Usuario, string Nota);

public sealed record PagoDto(string Referencia, string ClaveIdempotencia, decimal Monto, string Proveedor, DateTime Fecha);

public sealed record DespachoDto(string NumeroGuia, TipoEntrega Familia, string Etiqueta, DateTime FechaDespacho,
    DateTime FechaEstimada, DateTime? FechaEntrega);

public sealed record DevolucionDto(string Motivo, int DiasDesdeEntrega, decimal MontoReembolso, string Referencia,
    bool StockReintegrado, DateTime Fecha);

public sealed record PedidoDetalleDto(
    int Id, string Codigo, int ClienteId, string Cliente, CategoriaCliente CategoriaCliente, EstadoPedido Estado,
    TipoEntrega Entrega, DateTime Fecha, decimal Subtotal, string Politica, decimal Descuento, decimal Tarifa,
    int PlazoDias, decimal Total, bool Devuelto, DateTime? FechaEntrega,
    IReadOnlyList<LineaPedidoDto> Lineas, IReadOnlyList<HistorialEstadoDto> Historial,
    PagoDto? Pago, DespachoDto? Despacho, DevolucionDto? Devolucion)
{
    public decimal ImporteProductosNeto => Subtotal - Descuento;
}

public sealed record MovimientoDto(DateTime Fecha, string Sku, int Cantidad, string Tipo, string Referencia,
    int StockResultante, string Usuario);

public sealed record AuditoriaDto(DateTime Fecha, string Usuario, string Categoria, string Accion, string Detalle, string? Referencia);

public sealed record OutboxDto(Guid Id, string Tipo, string Referencia, DateTime Creado, DateTime? Procesado,
    int Intentos, string? UltimoError);

public sealed record NotificacionDto(DateTime Fecha, string Canal, string Destino, string Asunto, string Mensaje, Guid EventoId);

public sealed record IndicadoresDto(
    IReadOnlyDictionary<EstadoPedido, int> PedidosPorEstado,
    decimal TotalCobrado, int ProductosConStockBajo, int EventosPendientes, int Clientes,
    IReadOnlyList<string> Alertas);

public sealed record SeguimientoDto(string Codigo, string Cliente, EstadoPedido Estado, string? NumeroGuia,
    string? Etiqueta, DateTime? FechaEstimada, DateTime? FechaEntrega, IReadOnlyList<HistorialEstadoDto> Historial);

/// Puerto de consultas de solo lectura (implementado con AsNoTracking en Infrastructure).
public interface IConsultasOperativas
{
    Task<IReadOnlyList<ProductoDto>> ListarProductosAsync(string? filtro = null, CancellationToken ct = default);
    Task<IReadOnlyList<KitDto>> ListarKitsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ClienteDto>> ListarClientesAsync(string? filtro = null, CancellationToken ct = default);
    Task<IReadOnlyList<PedidoResumenDto>> ListarPedidosAsync(CancellationToken ct = default);
    Task<PedidoDetalleDto?> ObtenerPedidoAsync(int id, CancellationToken ct = default);
    Task<PedidoDetalleDto?> ObtenerPedidoPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<IReadOnlyList<MovimientoDto>> ListarMovimientosAsync(string? sku = null, CancellationToken ct = default);
    Task<IReadOnlyList<AuditoriaDto>> ListarAuditoriaAsync(string? categoria = null, int maximo = 500, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxDto>> ListarOutboxAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NotificacionDto>> ListarNotificacionesAsync(CancellationToken ct = default);
    Task<IndicadoresDto> ObtenerIndicadoresAsync(CancellationToken ct = default);
    Task<SeguimientoDto?> ObtenerSeguimientoAsync(string codigoOGuia, CancellationToken ct = default);
}
