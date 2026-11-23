using LogiMarket.Application.Consultas;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;
using Microsoft.EntityFrameworkCore;

namespace LogiMarket.Infrastructure.Persistencia;

/// Lado de CONSULTA (CQRS básico) con AsNoTracking. Los ordenamientos/sumas sobre decimal se hacen
/// en memoria porque SQLite almacena decimal como TEXT (sin aritmética exacta en el motor).
internal sealed class ConsultasOperativasEf(LogiMarketDbContext db) : IConsultasOperativas
{
    public const int UmbralStockBajo = 3;

    public async Task<IReadOnlyList<ProductoDto>> ListarProductosAsync(string? filtro = null, CancellationToken ct = default)
    {
        var consulta = db.Productos.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = $"%{filtro.Trim()}%";
            consulta = consulta.Where(p => EF.Functions.Like(p.Sku, f) || EF.Functions.Like(p.Nombre, f) || EF.Functions.Like(p.Categoria, f));
        }
        return (await consulta.ToListAsync(ct))
            .OrderBy(p => p.Sku)
            .Select(p => new ProductoDto(p.Id, p.Sku, p.Nombre, p.Categoria, p.Descripcion, p.Precio, p.PesoKg, p.Stock, p.Activo))
            .ToList();
    }

    public async Task<IReadOnlyList<KitDto>> ListarKitsAsync(CancellationToken ct = default)
    {
        // Grafo con seguimiento para que EF enlace sub-kits; no se guardan cambios desde aquí.
        var kits = await db.Kits.Include(k => k.Componentes).ThenInclude(c => c.Producto).ToListAsync(ct);
        return kits.OrderBy(k => k.Sku).Select(k => new KitDto(k.Id, k.Sku, k.Nombre, k.Descripcion, k.PrecioVenta, k.PesoKg,
            string.Join(" + ", k.Componentes.Select(c => $"{c.Elemento.Sku}×{c.Cantidad}")), k.Activo)).ToList();
    }

    public async Task<IReadOnlyList<ClienteDto>> ListarClientesAsync(string? filtro = null, CancellationToken ct = default)
    {
        var consulta = db.Clientes.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var f = $"%{filtro.Trim()}%";
            consulta = consulta.Where(c => EF.Functions.Like(c.Nombre, f) || EF.Functions.Like(c.Identificacion, f) || EF.Functions.Like(c.Email, f));
        }
        return await consulta.OrderBy(c => c.Nombre)
            .Select(c => new ClienteDto(c.Id, c.Identificacion, c.Nombre, c.Email, c.Telefono, c.Categoria, c.Activo))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PedidoResumenDto>> ListarPedidosAsync(CancellationToken ct = default)
    {
        var pedidos = await db.Pedidos.AsNoTracking().Include(p => p.Lineas).ThenInclude(l => l.Componentes)
            .AsSplitQuery().ToListAsync(ct);
        return pedidos.OrderByDescending(p => p.Id)
            .Select(p => new PedidoResumenDto(p.Id, p.Codigo, p.ClienteId, p.ClienteNombre, p.Estado, p.Total,
                p.FechaCreacion, p.Entrega, p.UnidadesFisicas, p.Devuelto))
            .ToList();
    }

    public async Task<PedidoDetalleDto?> ObtenerPedidoAsync(int id, CancellationToken ct = default)
    {
        var pedido = await PedidosCompletos().FirstOrDefaultAsync(p => p.Id == id, ct);
        return pedido is null ? null : await MapearAsync(pedido, ct);
    }

    public async Task<PedidoDetalleDto?> ObtenerPedidoPorCodigoAsync(string codigo, CancellationToken ct = default)
    {
        var clave = codigo.Trim().ToUpperInvariant();
        var pedido = await PedidosCompletos().FirstOrDefaultAsync(p => p.Codigo == clave, ct);
        return pedido is null ? null : await MapearAsync(pedido, ct);
    }

    public async Task<IReadOnlyList<MovimientoDto>> ListarMovimientosAsync(string? sku = null, CancellationToken ct = default)
    {
        var consulta = db.MovimientosInventario.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(sku))
        {
            var clave = sku.Trim().ToUpperInvariant();
            consulta = consulta.Where(m => m.Sku == clave);
        }
        return await consulta.OrderByDescending(m => m.Id)
            .Select(m => new MovimientoDto(m.Fecha, m.Sku, m.Cantidad, m.Tipo.ToString(), m.Referencia, m.StockResultante, m.Usuario))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AuditoriaDto>> ListarAuditoriaAsync(string? categoria = null, int maximo = 500, CancellationToken ct = default)
    {
        var consulta = db.Auditorias.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(categoria) && Enum.TryParse<CategoriaAuditoria>(categoria, true, out var cat))
            consulta = consulta.Where(a => a.Categoria == cat);
        return await consulta.OrderByDescending(a => a.Id).Take(maximo)
            .Select(a => new AuditoriaDto(a.Fecha, a.Usuario, a.Categoria.ToString(), a.Accion, a.Detalle, a.Referencia))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxDto>> ListarOutboxAsync(CancellationToken ct = default) =>
        await db.EventosOutbox.AsNoTracking().OrderByDescending(e => e.FechaCreacion)
            .Select(e => new OutboxDto(e.Id, e.Tipo, e.Referencia, e.FechaCreacion, e.FechaProcesado, e.Intentos, e.UltimoError))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<NotificacionDto>> ListarNotificacionesAsync(CancellationToken ct = default) =>
        await db.Notificaciones.AsNoTracking().OrderByDescending(n => n.Id)
            .Select(n => new NotificacionDto(n.Fecha, n.Canal, n.Destino, n.Asunto, n.Mensaje, n.EventoId))
            .ToListAsync(ct);

    public async Task<IndicadoresDto> ObtenerIndicadoresAsync(CancellationToken ct = default)
    {
        var estados = await db.Pedidos.AsNoTracking().GroupBy(p => p.Estado)
            .Select(g => new { g.Key, Total = g.Count() }).ToListAsync(ct);
        var porEstado = Enum.GetValues<EstadoPedido>().ToDictionary(e => e, e => estados.FirstOrDefault(x => x.Key == e)?.Total ?? 0);
        var montos = await db.Pagos.AsNoTracking().Select(p => p.Monto).ToListAsync(ct);
        var bajos = await db.Productos.AsNoTracking().Where(p => p.Activo && p.Stock <= UmbralStockBajo)
            .Select(p => new { p.Sku, p.Stock }).ToListAsync(ct);
        var pendientes = await db.EventosOutbox.CountAsync(e => e.FechaProcesado == null, ct);
        var clientes = await db.Clientes.CountAsync(ct);

        var alertas = bajos.Select(b => $"Stock bajo: {b.Sku} = {b.Stock}").ToList();
        if (pendientes > 0) alertas.Add($"{pendientes} evento(s) Outbox pendiente(s)");
        return new IndicadoresDto(porEstado, montos.Sum(), bajos.Count, pendientes, clientes, alertas);
    }

    public async Task<SeguimientoDto?> ObtenerSeguimientoAsync(string codigoOGuia, CancellationToken ct = default)
    {
        var clave = codigoOGuia.Trim().ToUpperInvariant();
        var despacho = await db.Despachos.AsNoTracking().FirstOrDefaultAsync(d => d.NumeroGuia == clave, ct);
        var pedido = despacho is not null
            ? await PedidosCompletos().FirstOrDefaultAsync(p => p.Id == despacho.PedidoId, ct)
            : await PedidosCompletos().FirstOrDefaultAsync(p => p.Codigo == clave, ct);
        if (pedido is null) return null;
        despacho ??= await db.Despachos.AsNoTracking().FirstOrDefaultAsync(d => d.PedidoId == pedido.Id, ct);
        return new SeguimientoDto(pedido.Codigo, pedido.ClienteNombre, pedido.Estado, despacho?.NumeroGuia, despacho?.Etiqueta,
            despacho?.FechaEstimada, despacho?.FechaEntrega, Historial(pedido));
    }

    private IQueryable<Pedido> PedidosCompletos() => db.Pedidos.AsNoTracking()
        .Include(p => p.Lineas).ThenInclude(l => l.Componentes)
        .Include(p => p.Historial)
        .AsSplitQuery();

    private static List<HistorialEstadoDto> Historial(Pedido p) =>
        p.Historial.OrderBy(h => h.Id).Select(h => new HistorialEstadoDto(h.Fecha, h.EstadoAnterior, h.EstadoNuevo, h.Usuario, h.Nota)).ToList();

    private async Task<PedidoDetalleDto> MapearAsync(Pedido p, CancellationToken ct)
    {
        var pago = await db.Pagos.AsNoTracking().FirstOrDefaultAsync(x => x.PedidoId == p.Id, ct);
        var despacho = await db.Despachos.AsNoTracking().FirstOrDefaultAsync(x => x.PedidoId == p.Id, ct);
        var devolucion = await db.Devoluciones.AsNoTracking().FirstOrDefaultAsync(x => x.PedidoId == p.Id, ct);

        return new PedidoDetalleDto(p.Id, p.Codigo, p.ClienteId, p.ClienteNombre, p.CategoriaCliente, p.Estado, p.Entrega,
            p.FechaCreacion, p.Subtotal, p.PoliticaDescuento, p.Descuento, p.TarifaEnvio, p.PlazoEntregaDias, p.Total,
            p.Devuelto, p.FechaEntrega,
            p.Lineas.OrderBy(l => l.Id).Select(l => new LineaPedidoDto(l.Sku, l.Descripcion, l.EsKit, l.Cantidad, l.PrecioUnitario,
                l.Subtotal, l.Componentes.Select(c => new ComponenteLineaDto(c.Sku, c.Nombre, c.CantidadPorUnidad, c.PrecioUnitario)).ToList())).ToList(),
            Historial(p),
            pago is null ? null : new PagoDto(pago.Referencia, pago.ClaveIdempotencia, pago.Monto, pago.Proveedor, pago.Fecha),
            despacho is null ? null : new DespachoDto(despacho.NumeroGuia, despacho.Familia, despacho.Etiqueta, despacho.FechaDespacho,
                despacho.FechaEstimada, despacho.FechaEntrega),
            devolucion is null ? null : new DevolucionDto(devolucion.Motivo.ToString(), devolucion.DiasDesdeEntrega,
                devolucion.MontoReembolso, devolucion.ReferenciaReembolso, devolucion.StockReintegrado, devolucion.Fecha));
    }
}
