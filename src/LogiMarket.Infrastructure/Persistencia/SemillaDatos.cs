using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Seguridad;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LogiMarket.Infrastructure.Persistencia;

/// Semilla IDEMPOTENTE: inserta por clave natural (SKU, usuario, identificación) solo lo que falta.
/// Relanzarla no duplica registros. Todos los datos son ficticios.
public sealed class SemillaDatos
{
    public static readonly (string Sku, string Nombre, string Categoria, decimal Precio, int Stock, decimal Peso)[] Productos =
    [
        ("P01", "Mouse USB", "Periféricos", 20.00m, 8, 0.20m),
        ("P02", "Teclado", "Periféricos", 30.00m, 5, 0.70m),
        ("P03", "Memoria USB", "Almacenamiento", 10.00m, 20, 0.10m),
        ("P04", "Webcam", "Video", 40.00m, 4, 0.35m),
        ("P05", "Alfombrilla", "Accesorios", 5.00m, 30, 0.10m)
    ];

    /// Cuentas de DEMOSTRACIÓN (ficticias, solo para la práctica). Se guardan con hash.
    public static readonly (string Usuario, string Nombre, RolUsuario Rol, string Clave)[] CuentasDemo =
    [
        ("admin", "Administrador Demo", RolUsuario.ADMIN, "Admin123*"),
        ("ventas", "Vendedor Demo", RolUsuario.VENTAS, "Ventas123*"),
        ("bodega", "Bodeguero Demo", RolUsuario.BODEGA, "Bodega123*"),
        ("consulta", "Consulta Demo", RolUsuario.CONSULTA, "Consulta123*")
    ];

    private readonly LogiMarketDbContext _db;
    private readonly IHasherContrasenas _hasher;

    public SemillaDatos(LogiMarketDbContext db, IHasherContrasenas hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task EjecutarAsync(CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var ahora = DateTime.Now;

        foreach (var p in Productos)
        {
            if (await _db.Productos.AnyAsync(x => x.Sku == p.Sku, ct)) continue;
            var producto = new ProductoSimple(p.Sku, p.Nombre, p.Categoria, $"{p.Nombre} (catálogo inicial)", p.Precio, p.Peso, p.Stock);
            _db.Productos.Add(producto);
            await _db.SaveChangesAsync(ct);
            _db.MovimientosInventario.Add(new MovimientoInventario(producto.Id, producto.Sku, p.Stock,
                TipoMovimiento.CARGA_INICIAL, "SEED", p.Stock, ahora, "seed"));
        }
        await _db.SaveChangesAsync(ct);

        await _db.Kits.Include(k => k.Componentes).ThenInclude(c => c.Producto).LoadAsync(ct);
        var porSku = await _db.Productos.ToDictionaryAsync(x => x.Sku, ct);

        var oficina = _db.Kits.Local.FirstOrDefault(k => k.Sku == "KIT_OFICINA");
        if (oficina is null)
        {
            oficina = new KitCompuesto("KIT_OFICINA", "Kit Oficina", "Mouse USB + Teclado");
            oficina.AgregarComponente(porSku["P01"], 1);
            oficina.AgregarComponente(porSku["P02"], 1);
            _db.Kits.Add(oficina);
        }
        if (!_db.Kits.Local.Any(k => k.Sku == "KIT_TELETRABAJO"))
        {
            var teletrabajo = new KitCompuesto("KIT_TELETRABAJO", "Kit Teletrabajo", "Kit Oficina + Memoria USB");
            teletrabajo.AgregarComponente(oficina, 1);
            teletrabajo.AgregarComponente(porSku["P03"], 1);
            _db.Kits.Add(teletrabajo);
        }

        foreach (var cuenta in CuentasDemo)
        {
            if (await _db.Usuarios.AnyAsync(u => u.NombreUsuario == cuenta.Usuario, ct)) continue;
            _db.Usuarios.Add(new Usuario(cuenta.Usuario, cuenta.Nombre, cuenta.Rol, _hasher.Calcular(cuenta.Clave)));
        }

        (string Id, string Nombre, string Email, string Tel, CategoriaCliente Cat)[] clientes =
        [
            ("0100000001", "Ana López", "ana.lopez@example.com", "0990000001", CategoriaCliente.VIP),
            ("0100000002", "Luis Ruiz", "luis.ruiz@example.com", "0990000002", CategoriaCliente.NORMAL),
            ("0100000003", "Carmen Vega", "carmen.vega@example.com", "0990000003", CategoriaCliente.NORMAL)
        ];
        foreach (var c in clientes)
        {
            if (await _db.Clientes.AnyAsync(x => x.Identificacion == c.Id, ct)) continue;
            _db.Clientes.Add(new Cliente(c.Id, c.Nombre, c.Email, c.Tel, c.Cat));
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _db.ChangeTracker.Clear();
    }
}

/// Crea la base desde cero aplicando MIGRACIONES (no EnsureCreated) y ejecuta la semilla.
/// También restablece la base controlada para pruebas/demostración.
public sealed class InicializadorBaseDatos : IRestablecedorDatos
{
    private readonly LogiMarketDbContext _db;
    private readonly SemillaDatos _semilla;
    private readonly OpcionesInfraestructura _opciones;

    public InicializadorBaseDatos(LogiMarketDbContext db, SemillaDatos semilla, OpcionesInfraestructura opciones)
    {
        _db = db;
        _semilla = semilla;
        _opciones = opciones;
    }

    public async Task InicializarAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_opciones.DirectorioDatos);
        await _db.Database.MigrateAsync(ct);
        await _db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await _semilla.EjecutarAsync(ct);
    }

    public async Task RestablecerAsync(CancellationToken ct = default)
    {
        _db.ChangeTracker.Clear();
        SqliteConnection.ClearAllPools();
        await _db.Database.EnsureDeletedAsync(ct);
        await InicializarAsync(ct);
    }
}
