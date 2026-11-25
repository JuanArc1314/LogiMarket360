using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace LogiMarket.Infrastructure.Persistencia;

//patron repository - adaptadores EF Core de las interfaces del nucleo

internal sealed class RepositorioCatalogoEf(LogiMarketDbContext db) : IRepositorioCatalogo
{
    public Task<ProductoSimple?> ObtenerProductoAsync(string sku, CancellationToken ct = default)
    {
        var clave = sku.Trim().ToUpperInvariant();
        return db.Productos.FirstOrDefaultAsync(p => p.Sku == clave, ct);
    }

    public async Task<IReadOnlyDictionary<string, ProductoSimple>> ObtenerProductosAsync(IEnumerable<string> skus, CancellationToken ct = default)
    {
        var claves = skus.Select(s => s.ToUpperInvariant()).Distinct().ToList();
        var productos = await db.Productos.Where(p => claves.Contains(p.Sku)).ToListAsync(ct);
        return productos.ToDictionary(p => p.Sku, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ProductoSimple>> ListarProductosAsync(CancellationToken ct = default) =>
        (await db.Productos.ToListAsync(ct)).OrderBy(p => p.Sku).ToList();

    public async Task<KitCompuesto?> ObtenerKitAsync(string sku, CancellationToken ct = default)
    {
        var clave = sku.Trim().ToUpperInvariant();
        await CargarGrafoKitsAsync(ct);
        return db.Kits.Local.FirstOrDefault(k => k.Sku == clave);
    }

    public async Task<IReadOnlyList<KitCompuesto>> ListarKitsAsync(CancellationToken ct = default)
    {
        await CargarGrafoKitsAsync(ct);
        return db.Kits.Local.OrderBy(k => k.Sku).ToList();
    }

    public async Task<IElementoCatalogo?> ObtenerElementoAsync(string sku, CancellationToken ct = default) =>
        (IElementoCatalogo?)await ObtenerProductoAsync(sku, ct) ?? await ObtenerKitAsync(sku, ct);

    public async Task<bool> ExisteSkuAsync(string sku, CancellationToken ct = default)
    {
        var clave = sku.Trim().ToUpperInvariant();
        return await db.Productos.AnyAsync(p => p.Sku == clave, ct) || await db.Kits.AnyAsync(k => k.Sku == clave, ct);
    }

    public void AgregarProducto(ProductoSimple producto) => db.Productos.Add(producto);
    public void AgregarKit(KitCompuesto kit) => db.Kits.Add(kit);

    //carga todos los kits con sus componentes y productos: EF enlaza los sub-kits por identidad
    //de modo que el composite queda completo a cualquier profundidad
    private Task CargarGrafoKitsAsync(CancellationToken ct) =>
        db.Kits.Include(k => k.Componentes).ThenInclude(c => c.Producto).LoadAsync(ct);
}

internal sealed class RepositorioClientesEf(LogiMarketDbContext db) : IRepositorioClientes
{
    public Task<Cliente?> ObtenerAsync(int id, CancellationToken ct = default) =>
        db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct);

    public Task<bool> ExisteIdentificacionAsync(string identificacion, int? excluirId = null, CancellationToken ct = default) =>
        db.Clientes.AnyAsync(c => c.Identificacion == identificacion && (excluirId == null || c.Id != excluirId), ct);

    public void Agregar(Cliente cliente) => db.Clientes.Add(cliente);
}

internal sealed class RepositorioPedidosEf(LogiMarketDbContext db) : IRepositorioPedidos
{
    private IQueryable<Pedido> Completo => db.Pedidos
        .Include(p => p.Lineas).ThenInclude(l => l.Componentes)
        .Include(p => p.Historial)
        .AsSplitQuery();

    public Task<Pedido?> ObtenerAsync(int id, CancellationToken ct = default) =>
        Completo.FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<Pedido?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        Completo.FirstOrDefaultAsync(p => p.Codigo == codigo, ct);

    public void Agregar(Pedido pedido) => db.Pedidos.Add(pedido);
}

internal sealed class RepositorioPagosEf(LogiMarketDbContext db) : IRepositorioPagos
{
    public Task<Pago?> ObtenerPorClaveAsync(string claveIdempotencia, CancellationToken ct = default) =>
        db.Pagos.FirstOrDefaultAsync(p => p.ClaveIdempotencia == claveIdempotencia, ct);

    public Task<Pago?> ObtenerPorPedidoAsync(int pedidoId, CancellationToken ct = default) =>
        db.Pagos.FirstOrDefaultAsync(p => p.PedidoId == pedidoId, ct);

    public void Agregar(Pago pago) => db.Pagos.Add(pago);
}

internal sealed class RepositorioDespachosEf(LogiMarketDbContext db) : IRepositorioDespachos
{
    public Task<Despacho?> ObtenerPorPedidoAsync(int pedidoId, CancellationToken ct = default) =>
        db.Despachos.FirstOrDefaultAsync(d => d.PedidoId == pedidoId, ct);

    public void Agregar(Despacho despacho) => db.Despachos.Add(despacho);
}

internal sealed class RepositorioDevolucionesEf(LogiMarketDbContext db) : IRepositorioDevoluciones
{
    public Task<bool> ExisteParaPedidoAsync(int pedidoId, CancellationToken ct = default) =>
        db.Devoluciones.AnyAsync(d => d.PedidoId == pedidoId, ct);

    public void Agregar(Devolucion devolucion) => db.Devoluciones.Add(devolucion);
}

internal sealed class RepositorioMovimientosEf(LogiMarketDbContext db) : IRepositorioMovimientos
{
    public void Agregar(MovimientoInventario movimiento) => db.MovimientosInventario.Add(movimiento);
}

internal sealed class RepositorioAuditoriaEf(LogiMarketDbContext db) : IRepositorioAuditoria
{
    public async Task<bool> ExisteAsync(string referencia, string accion, CancellationToken ct = default) =>
        db.Auditorias.Local.Any(a => a.Referencia == referencia && a.Accion == accion) ||
        await db.Auditorias.AnyAsync(a => a.Referencia == referencia && a.Accion == accion, ct);

    public void Agregar(Auditoria registro) => db.Auditorias.Add(registro);
}

internal sealed class RepositorioOutboxEf(LogiMarketDbContext db) : IRepositorioOutbox
{
    public async Task<IReadOnlyList<Guid>> ObtenerIdsPendientesAsync(int maximo, CancellationToken ct = default) =>
        await db.EventosOutbox.AsNoTracking()
            .Where(e => e.FechaProcesado == null)
            .OrderBy(e => e.FechaCreacion)
            .Select(e => e.Id)
            .Take(maximo)
            .ToListAsync(ct);

    public Task<EventoOutbox?> ObtenerAsync(Guid id, CancellationToken ct = default) =>
        db.EventosOutbox.FirstOrDefaultAsync(e => e.Id == id, ct);

    public void Agregar(EventoOutbox evento) => db.EventosOutbox.Add(evento);
}

internal sealed class RepositorioUsuariosEf(LogiMarketDbContext db) : IRepositorioUsuarios
{
    public Task<Usuario?> ObtenerPorNombreAsync(string nombreUsuario, CancellationToken ct = default)
    {
        var clave = nombreUsuario.Trim().ToLowerInvariant();
        return db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.NombreUsuario == clave, ct);
    }
}

internal sealed class RepositorioNotificacionesEf(LogiMarketDbContext db) : IRepositorioNotificaciones
{
    public async Task<bool> ExisteAsync(Guid eventoId, string canal, CancellationToken ct = default) =>
        db.Notificaciones.Local.Any(n => n.EventoId == eventoId && n.Canal == canal) ||
        await db.Notificaciones.AnyAsync(n => n.EventoId == eventoId && n.Canal == canal, ct);

    public void Agregar(Notificacion notificacion) => db.Notificaciones.Add(notificacion);
}
