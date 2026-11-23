using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiMarket.Infrastructure.Persistencia;

// Mapeo relacional: PK/FK reales, índices de búsqueda, unicidad y restricciones CHECK.

internal sealed class UsuarioConfig : IEntityTypeConfiguration<Usuario>
{
    public void Configure(EntityTypeBuilder<Usuario> b)
    {
        b.ToTable("Usuarios");
        b.HasKey(x => x.Id);
        b.Property(x => x.NombreUsuario).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.NombreUsuario).IsUnique();
        b.Property(x => x.NombreCompleto).HasMaxLength(120).IsRequired();
        b.Property(x => x.Rol).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.HashContrasena).HasMaxLength(200).IsRequired();
    }
}

internal sealed class ClienteConfig : IEntityTypeConfiguration<Cliente>
{
    public void Configure(EntityTypeBuilder<Cliente> b)
    {
        b.ToTable("Clientes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Identificacion).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Identificacion).IsUnique();
        b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Nombre);
        b.Property(x => x.Email).HasMaxLength(120);
        b.Property(x => x.Telefono).HasMaxLength(20);
        b.Property(x => x.Categoria).HasConversion<string>().HasMaxLength(10);
    }
}

internal sealed class ProductoConfig : IEntityTypeConfiguration<ProductoSimple>
{
    public void Configure(EntityTypeBuilder<ProductoSimple> b)
    {
        b.ToTable("Productos", t =>
        {
            t.HasCheckConstraint("CK_Producto_Precio", "CAST(\"Precio\" AS REAL) > 0");
            t.HasCheckConstraint("CK_Producto_Peso", "CAST(\"PesoKg\" AS REAL) >= 0");
            t.HasCheckConstraint("CK_Producto_Stock", "\"Stock\" >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        b.HasIndex(x => x.Nombre);
        b.Property(x => x.Categoria).HasMaxLength(60).IsRequired();
        b.Property(x => x.Descripcion).HasMaxLength(250);
        b.Property(x => x.Precio).HasPrecision(12, 2);
        b.Property(x => x.PesoKg).HasPrecision(10, 3);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Ignore(x => x.PrecioVenta);
        b.Ignore(x => x.EsCompuesto);
    }
}

internal sealed class KitConfig : IEntityTypeConfiguration<KitCompuesto>
{
    public void Configure(EntityTypeBuilder<KitCompuesto> b)
    {
        b.ToTable("Kits");
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(30).IsRequired();
        b.HasIndex(x => x.Sku).IsUnique();
        b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        b.Property(x => x.Descripcion).HasMaxLength(250);
        b.Ignore(x => x.PrecioVenta);
        b.Ignore(x => x.PesoKg);
        b.Ignore(x => x.EsCompuesto);
        b.HasMany(x => x.Componentes).WithOne(c => c.Kit).HasForeignKey(c => c.KitId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Componentes).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class KitComponenteConfig : IEntityTypeConfiguration<KitComponente>
{
    public void Configure(EntityTypeBuilder<KitComponente> b)
    {
        b.ToTable("KitComponentes", t =>
        {
            t.HasCheckConstraint("CK_KitComponente_Elemento", "(\"ProductoId\" IS NULL) <> (\"SubKitId\" IS NULL)");
            t.HasCheckConstraint("CK_KitComponente_Cantidad", "\"Cantidad\" > 0");
            t.HasCheckConstraint("CK_KitComponente_NoAutoReferencia", "\"SubKitId\" IS NULL OR \"SubKitId\" <> \"KitId\"");
        });
        b.HasKey(x => x.Id);
        b.HasOne(x => x.Producto).WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SubKit).WithMany().HasForeignKey(x => x.SubKitId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.KitId, x.ProductoId }).IsUnique();
        b.HasIndex(x => new { x.KitId, x.SubKitId }).IsUnique();
        b.Ignore(x => x.Elemento);
    }
}

internal sealed class PedidoConfig : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> b)
    {
        b.ToTable("Pedidos", t => t.HasCheckConstraint("CK_Pedido_Total", "CAST(\"Total\" AS REAL) >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Codigo).HasMaxLength(20).IsRequired();
        b.HasIndex(x => x.Codigo).IsUnique();
        b.HasOne<Cliente>().WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ClienteNombre).HasMaxLength(120).IsRequired();
        b.Property(x => x.CategoriaCliente).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Entrega).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Estado).HasConversion<string>().HasMaxLength(12);
        b.HasIndex(x => x.Estado);
        b.HasIndex(x => x.FechaCreacion);
        b.Property(x => x.PoliticaDescuento).HasMaxLength(20);
        b.Property(x => x.Subtotal).HasPrecision(12, 2);
        b.Property(x => x.Descuento).HasPrecision(12, 2);
        b.Property(x => x.TarifaEnvio).HasPrecision(12, 2);
        b.Property(x => x.Total).HasPrecision(12, 2);
        b.Property(x => x.ClaveIdempotencia).HasMaxLength(80);
        b.HasIndex(x => x.ClaveIdempotencia).IsUnique();
        b.Property(x => x.Version).IsConcurrencyToken();

        b.HasMany(x => x.Lineas).WithOne().HasForeignKey(l => l.PedidoId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lineas).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Historial).WithOne().HasForeignKey(h => h.PedidoId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Historial).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.Ignore(x => x.UnidadesFisicas);
        b.Ignore(x => x.PesoTotalKg);
        b.Ignore(x => x.ImporteProductosNeto);
        b.Ignore(x => x.EstadoActual);
    }
}

internal sealed class PedidoDetalleConfig : IEntityTypeConfiguration<PedidoDetalle>
{
    public void Configure(EntityTypeBuilder<PedidoDetalle> b)
    {
        b.ToTable("PedidoDetalles", t => t.HasCheckConstraint("CK_PedidoDetalle_Cantidad", "\"Cantidad\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(30).IsRequired();
        b.Property(x => x.Descripcion).HasMaxLength(120).IsRequired();
        b.Property(x => x.PrecioUnitario).HasPrecision(12, 2);
        b.Property(x => x.Subtotal).HasPrecision(12, 2);
        b.Property(x => x.PesoUnitarioKg).HasPrecision(10, 3);
        b.HasIndex(x => new { x.PedidoId, x.Sku }).IsUnique();
        b.HasMany(x => x.Componentes).WithOne().HasForeignKey(c => c.PedidoDetalleId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Componentes).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Ignore(x => x.UnidadesFisicas);
    }
}

internal sealed class PedidoDetalleComponenteConfig : IEntityTypeConfiguration<PedidoDetalleComponente>
{
    public void Configure(EntityTypeBuilder<PedidoDetalleComponente> b)
    {
        b.ToTable("PedidoDetalleComponentes", t => t.HasCheckConstraint("CK_PDC_Cantidad", "\"CantidadPorUnidad\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Sku).HasMaxLength(30).IsRequired();
        b.Property(x => x.Nombre).HasMaxLength(120).IsRequired();
        b.Property(x => x.PrecioUnitario).HasPrecision(12, 2);
        b.Property(x => x.PesoKg).HasPrecision(10, 3);
    }
}

internal sealed class PedidoHistorialConfig : IEntityTypeConfiguration<PedidoHistorialEstado>
{
    public void Configure(EntityTypeBuilder<PedidoHistorialEstado> b)
    {
        b.ToTable("PedidoHistorialEstados");
        b.HasKey(x => x.Id);
        b.Property(x => x.EstadoAnterior).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.EstadoNuevo).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.Usuario).HasMaxLength(40);
        b.Property(x => x.Nota).HasMaxLength(200);
    }
}

internal sealed class PagoConfig : IEntityTypeConfiguration<Pago>
{
    public void Configure(EntityTypeBuilder<Pago> b)
    {
        b.ToTable("Pagos", t => t.HasCheckConstraint("CK_Pago_Monto", "CAST(\"Monto\" AS REAL) > 0"));
        b.HasKey(x => x.Id);
        b.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PedidoId).IsUnique();               // un cobro aprobado por pedido
        b.Property(x => x.Referencia).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.Referencia).IsUnique();             // referencia de cobro única
        b.Property(x => x.ClaveIdempotencia).HasMaxLength(80).IsRequired();
        b.HasIndex(x => x.ClaveIdempotencia).IsUnique();      // clave de idempotencia única
        b.Property(x => x.Monto).HasPrecision(12, 2);
        b.Property(x => x.Proveedor).HasMaxLength(40);
        b.Property(x => x.Usuario).HasMaxLength(40);
    }
}

internal sealed class MovimientoConfig : IEntityTypeConfiguration<MovimientoInventario>
{
    public void Configure(EntityTypeBuilder<MovimientoInventario> b)
    {
        b.ToTable("MovimientosInventario", t => t.HasCheckConstraint("CK_Movimiento_NoCero", "\"Cantidad\" <> 0"));
        b.HasKey(x => x.Id);
        b.HasOne<ProductoSimple>().WithMany().HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Sku).HasMaxLength(30).IsRequired();
        b.Property(x => x.Tipo).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Referencia).HasMaxLength(60);
        b.Property(x => x.Usuario).HasMaxLength(40);
        b.HasIndex(x => new { x.Sku, x.Fecha });
        b.HasIndex(x => x.Referencia);
    }
}

internal sealed class DespachoConfig : IEntityTypeConfiguration<Despacho>
{
    public void Configure(EntityTypeBuilder<Despacho> b)
    {
        b.ToTable("Despachos");
        b.HasKey(x => x.Id);
        b.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PedidoId).IsUnique();
        b.Property(x => x.NumeroGuia).HasMaxLength(40).IsRequired();
        b.HasIndex(x => x.NumeroGuia).IsUnique();
        b.Property(x => x.Familia).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.Etiqueta).HasMaxLength(300);
        b.Property(x => x.Usuario).HasMaxLength(40);
    }
}

internal sealed class DevolucionConfig : IEntityTypeConfiguration<Devolucion>
{
    public void Configure(EntityTypeBuilder<Devolucion> b)
    {
        b.ToTable("Devoluciones", t => t.HasCheckConstraint("CK_Devolucion_Monto", "CAST(\"MontoReembolso\" AS REAL) > 0"));
        b.HasKey(x => x.Id);
        b.HasOne<Pedido>().WithMany().HasForeignKey(x => x.PedidoId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.PedidoId).IsUnique();               // devolución única por pedido
        b.Property(x => x.Motivo).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.MontoReembolso).HasPrecision(12, 2);
        b.Property(x => x.ReferenciaReembolso).HasMaxLength(60).IsRequired();
        b.HasIndex(x => x.ReferenciaReembolso).IsUnique();
        b.Property(x => x.Usuario).HasMaxLength(40);
    }
}

internal sealed class EventoOutboxConfig : IEntityTypeConfiguration<EventoOutbox>
{
    public void Configure(EntityTypeBuilder<EventoOutbox> b)
    {
        b.ToTable("EventosOutbox");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Tipo).HasMaxLength(60).IsRequired();
        b.Property(x => x.Referencia).HasMaxLength(40);
        b.Property(x => x.Contenido).IsRequired();
        b.Property(x => x.UltimoError).HasMaxLength(400);
        b.HasIndex(x => new { x.FechaProcesado, x.FechaCreacion });
        b.Ignore(x => x.Procesado);
    }
}

internal sealed class AuditoriaConfig : IEntityTypeConfiguration<Auditoria>
{
    public void Configure(EntityTypeBuilder<Auditoria> b)
    {
        b.ToTable("Auditorias");
        b.HasKey(x => x.Id);
        b.Property(x => x.Usuario).HasMaxLength(40);
        b.Property(x => x.Categoria).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.Accion).HasMaxLength(60).IsRequired();
        b.Property(x => x.Detalle).HasMaxLength(500);
        b.Property(x => x.Referencia).HasMaxLength(80);
        b.HasIndex(x => x.Fecha);
        b.HasIndex(x => new { x.Referencia, x.Accion });
    }
}

internal sealed class NotificacionConfig : IEntityTypeConfiguration<Notificacion>
{
    public void Configure(EntityTypeBuilder<Notificacion> b)
    {
        b.ToTable("Notificaciones");
        b.HasKey(x => x.Id);
        b.Property(x => x.Canal).HasMaxLength(10).IsRequired();
        b.Property(x => x.Destino).HasMaxLength(120);
        b.Property(x => x.Asunto).HasMaxLength(200);
        b.Property(x => x.Mensaje).HasMaxLength(1000);
        b.HasIndex(x => new { x.EventoId, x.Canal }).IsUnique();   // sin avisos duplicados por evento
    }
}
