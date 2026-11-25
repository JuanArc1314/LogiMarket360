using LogiMarket.Application.Comun;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Application.Catalogo;

public sealed record ProductoEdicionDto(string Sku, string Nombre, string Categoria, string Descripcion,
    decimal Precio, decimal PesoKg, int Stock);

public sealed record ComponenteKitDto(string Sku, int Cantidad);

public sealed record DesgloseKitDto(string Sku, string Nombre, decimal Precio, decimal PesoTotalKg,
    IReadOnlyList<LineaDesglose> Lineas, IReadOnlyList<string> Traza);

//casos de uso del catalogo: CRUD de productos, kits (composite) y ajustes de inventario
public sealed class ServicioCatalogo
{
    private readonly IRepositorioCatalogo _catalogo;
    private readonly IRepositorioMovimientos _movimientos;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly Autorizador _autorizador;
    private readonly IReloj _reloj;

    public ServicioCatalogo(IRepositorioCatalogo catalogo, IRepositorioMovimientos movimientos, IUnidadDeTrabajo unidad,
        Autorizador autorizador, IReloj reloj)
    {
        _catalogo = catalogo;
        _movimientos = movimientos;
        _unidad = unidad;
        _autorizador = autorizador;
        _reloj = reloj;
    }

    public async Task<ResultadoOperacion> CrearProductoAsync(ProductoEdicionDto dto, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.GestionarCatalogo);
        await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            if (await _catalogo.ExisteSkuAsync(dto.Sku, t))
                throw new ExcepcionDominio("SKU_DUPLICADO", $"El SKU {dto.Sku.ToUpperInvariant()} ya existe.");
            var producto = new ProductoSimple(dto.Sku, dto.Nombre, dto.Categoria, dto.Descripcion, dto.Precio, dto.PesoKg, dto.Stock);
            _catalogo.AgregarProducto(producto);
            await _unidad.GuardarCambiosAsync(t);
            if (producto.Stock > 0)
                _movimientos.Agregar(new MovimientoInventario(producto.Id, producto.Sku, producto.Stock,
                    TipoMovimiento.CARGA_INICIAL, "ALTA-PRODUCTO", producto.Stock, _reloj.Ahora, usuario.NombreUsuario));
            return true;
        }, ct);
        return ResultadoOperacion.Ok($"Producto {dto.Sku.ToUpperInvariant()} creado.");
    }

    //actualiza datos descriptivos y precio
    //los pedidos ya registrados conservan su instantanea
    public async Task<ResultadoOperacion> ActualizarProductoAsync(ProductoEdicionDto dto, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarCatalogo);
        await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var producto = await _catalogo.ObtenerProductoAsync(dto.Sku, t) ?? throw new RecursoNoEncontradoException("Producto", dto.Sku);
            producto.Actualizar(dto.Nombre, dto.Categoria, dto.Descripcion, dto.Precio, dto.PesoKg);
            return true;
        }, ct);
        return ResultadoOperacion.Ok($"Producto {dto.Sku} actualizado.");
    }

    public async Task<ResultadoOperacion> CambiarActivoProductoAsync(string sku, bool activo, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarCatalogo);
        await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var producto = await _catalogo.ObtenerProductoAsync(sku, t) ?? throw new RecursoNoEncontradoException("Producto", sku);
            if (activo) producto.Activar(); else producto.Desactivar();
            return true;
        }, ct);
        return ResultadoOperacion.Ok($"Producto {sku} {(activo ? "activado" : "desactivado")}.");
    }

    public async Task<ResultadoOperacion> AjustarStockAsync(string sku, int nuevoStock, string motivo, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.AjustarInventario);
        var diferencia = await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var producto = await _catalogo.ObtenerProductoAsync(sku, t) ?? throw new RecursoNoEncontradoException("Producto", sku);
            var dif = producto.AjustarStock(nuevoStock);
            if (dif != 0)
                _movimientos.Agregar(new MovimientoInventario(producto.Id, producto.Sku, dif, TipoMovimiento.AJUSTE,
                    string.IsNullOrWhiteSpace(motivo) ? "AJUSTE-MANUAL" : motivo, producto.Stock, _reloj.Ahora, usuario.NombreUsuario));
            return dif;
        }, ct);
        return ResultadoOperacion.Ok($"Stock de {sku} ajustado a {nuevoStock} (diferencia {diferencia:+#;-#;0}).");
    }

    public async Task<ResultadoOperacion> CrearKitAsync(string sku, string nombre, string descripcion,
        IReadOnlyList<ComponenteKitDto> componentes, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarCatalogo);
        if (componentes.Count == 0)
            throw new ExcepcionDominio("KIT_VACIO", "Un kit requiere al menos un componente.");
        await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            if (await _catalogo.ExisteSkuAsync(sku, t))
                throw new ExcepcionDominio("SKU_DUPLICADO", $"El SKU {sku.ToUpperInvariant()} ya existe.");
            var kit = new KitCompuesto(sku, nombre, descripcion);
            foreach (var c in componentes)
                kit.AgregarComponente(await ObtenerElementoAsync(c.Sku, t), c.Cantidad);
            _catalogo.AgregarKit(kit);
            return true;
        }, ct);
        return ResultadoOperacion.Ok($"Kit {sku.ToUpperInvariant()} creado.");
    }

    //agrega un componente a un kit existente
    //rechaza referencias circulares (KIT_CICLICO)
    public async Task<ResultadoOperacion> AgregarComponenteAsync(string kitSku, string componenteSku, int cantidad, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarCatalogo);
        await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var kit = await _catalogo.ObtenerKitAsync(kitSku, t) ?? throw new RecursoNoEncontradoException("Kit", kitSku);
            kit.AgregarComponente(await ObtenerElementoAsync(componenteSku, t), cantidad);
            return true;
        }, ct);
        return ResultadoOperacion.Ok($"{componenteSku} ×{cantidad} agregado a {kitSku}.");
    }

    public async Task<ResultadoOperacion> QuitarComponenteAsync(string kitSku, string componenteSku, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarCatalogo);
        await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var kit = await _catalogo.ObtenerKitAsync(kitSku, t) ?? throw new RecursoNoEncontradoException("Kit", kitSku);
            kit.QuitarComponente(componenteSku);
            return true;
        }, ct);
        return ResultadoOperacion.Ok($"{componenteSku} retirado de {kitSku}.");
    }

    //visitor: desglose fisico por SKU y peso logistico de un producto o kit
    public async Task<DesgloseKitDto> DesglosarAsync(string sku, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.ConsultarInformacion);
        var elemento = await ObtenerElementoAsync(sku, ct);
        var desglose = new DesgloseFisicoVisitor();
        var peso = new PesoLogisticoVisitor();
        elemento.Aceptar(desglose);
        elemento.Aceptar(peso);
        return new DesgloseKitDto(elemento.Sku, elemento.Nombre, elemento.PrecioVenta, peso.PesoTotalKg, desglose.Lineas, desglose.Traza);
    }

    public async Task<IReadOnlyList<ElementoVendibleDto>> ListarVendiblesAsync(CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.ConsultarInformacion);
        var productos = (await _catalogo.ListarProductosAsync(ct)).Where(p => p.Activo)
            .Select(p => new ElementoVendibleDto(p.Sku, p.Nombre, p.Precio, false, p.Stock));
        var kits = (await _catalogo.ListarKitsAsync(ct)).Where(k => k.Activo)
            .Select(k => new ElementoVendibleDto(k.Sku, k.Nombre, k.PrecioVenta, true, null));
        return productos.Concat(kits).ToList();
    }

    private async Task<IElementoCatalogo> ObtenerElementoAsync(string sku, CancellationToken ct) =>
        await _catalogo.ObtenerElementoAsync(sku, ct) ?? throw new RecursoNoEncontradoException("SKU", sku);
}
