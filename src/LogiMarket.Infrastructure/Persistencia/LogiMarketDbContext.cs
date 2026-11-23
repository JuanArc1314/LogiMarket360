using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace LogiMarket.Infrastructure.Persistencia;

/// DbContext de EF Core (SQLite). Junto con la transacción explícita materializa la UNIDAD DE TRABAJO.
/// Las entidades del dominio se mapean sin setters públicos (acceso por campos/propiedades privadas).
public sealed class LogiMarketDbContext : DbContext
{
    public LogiMarketDbContext(DbContextOptions<LogiMarketDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<ProductoSimple> Productos => Set<ProductoSimple>();
    public DbSet<KitCompuesto> Kits => Set<KitCompuesto>();
    public DbSet<KitComponente> KitComponentes => Set<KitComponente>();
    public DbSet<Pedido> Pedidos => Set<Pedido>();
    public DbSet<PedidoDetalle> PedidoDetalles => Set<PedidoDetalle>();
    public DbSet<PedidoDetalleComponente> PedidoDetalleComponentes => Set<PedidoDetalleComponente>();
    public DbSet<PedidoHistorialEstado> PedidoHistorial => Set<PedidoHistorialEstado>();
    public DbSet<Pago> Pagos => Set<Pago>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<Despacho> Despachos => Set<Despacho>();
    public DbSet<Devolucion> Devoluciones => Set<Devolucion>();
    public DbSet<EventoOutbox> EventosOutbox => Set<EventoOutbox>();
    public DbSet<Auditoria> Auditorias => Set<Auditoria>();
    public DbSet<Notificacion> Notificaciones => Set<Notificacion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LogiMarketDbContext).Assembly);
}
