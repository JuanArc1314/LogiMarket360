using LogiMarket.Application.Catalogo;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Seguridad;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

//03 - catalogo y kits: CRUD de SKU, precios
//existencias y estructura de kits anidados (composite)
public sealed class CatalogoView : VistaBase
{
    private readonly DataGridView _productos = UiKit.Grid("Productos");
    private readonly DataGridView _kits = UiKit.Grid("Kits");
    private readonly TextBox _buscar = UiKit.Texto("Buscar producto", 220);
    private readonly TextBox _sku = UiKit.Texto("SKU", 90);
    private readonly TextBox _nombre = UiKit.Texto("Nombre", 180);
    private readonly TextBox _categoria = UiKit.Texto("Categoría", 130);
    private readonly TextBox _descripcion = UiKit.Texto("Descripción", 200);
    private readonly NumericUpDown _precio = UiKit.Numero("Precio USD", 0.01m, 100000m, 2);
    private readonly NumericUpDown _peso = UiKit.Numero("Peso kg", 0m, 1000m, 3);
    private readonly NumericUpDown _stock = UiKit.Numero("Stock inicial", 0m, 100000m);
    private readonly ComboBox _kit = UiKit.Combo("Kit", 170);
    private readonly ComboBox _componente = UiKit.Combo("Componente", 170);
    private readonly NumericUpDown _cantidadComponente = UiKit.Numero("Cantidad del componente", 1m, 99m);
    private readonly TextBox _nuevoKitSku = UiKit.Texto("SKU del nuevo kit", 140);
    private readonly TextBox _nuevoKitNombre = UiKit.Texto("Nombre del nuevo kit", 170);
    private readonly TextBox _desglose = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Font = Estilos.Mono, AccessibleName = "Desglose del kit" };
    private readonly List<Control> _soloAdmin = new();

    public CatalogoView(ContextoUi ui) : base(ui, "Catálogo y kits", "Gestión de SKU, precios, existencias y estructura de kits anidados")
    {
        _productos.Columna(nameof(ProductoDto.Sku), "SKU", 50).Columna(nameof(ProductoDto.Nombre), "Producto", 140)
            .Columna(nameof(ProductoDto.Categoria), "Categoría", 90).Columna(nameof(ProductoDto.Precio), "Precio USD", 70, "M")
            .Columna(nameof(ProductoDto.PesoKg), "Peso kg", 60, "0.000").Columna(nameof(ProductoDto.Stock), "Stock", 50)
            .Columna(nameof(ProductoDto.Activo), "Activo", 45);
        _productos.SelectionChanged += (_, _) => CargarProductoSeleccionado();
        _kits.Columna(nameof(KitDto.Sku), "Kit", 90).Columna(nameof(KitDto.Nombre), "Nombre", 110)
            .Columna(nameof(KitDto.Composicion), "Composición", 150).Columna(nameof(KitDto.Precio), "Precio USD", 70, "M")
            .Columna(nameof(KitDto.PesoKg), "Peso kg", 60, "0.000");

        var guardar = UiKit.Boton("&Guardar producto", async (_, _) => await IntentarAsync(GuardarProductoAsync), primario: true);
        var nuevo = UiKit.Boton("&Nuevo", (_, _) => LimpiarFormulario());
        var activar = UiKit.Boton("Activar/Desactivar", async (_, _) => await IntentarAsync(AlternarActivoAsync));
        var agregarComp = UiKit.Boton("Agregar componente", async (_, _) => await IntentarAsync(AgregarComponenteAsync));
        var quitarComp = UiKit.Boton("Quitar componente", async (_, _) => await IntentarAsync(QuitarComponenteAsync));
        var crearKit = UiKit.Boton("Crear kit con componente", async (_, _) => await IntentarAsync(CrearKitAsync));
        _soloAdmin.AddRange([guardar, nuevo, activar, agregarComp, quitarComp, crearKit]);

        var formulario = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true };
        formulario.Controls.AddRange([UiKit.Campo("SKU", _sku), UiKit.Campo("Nombre", _nombre), UiKit.Campo("Categoría", _categoria),
            UiKit.Campo("Descripción", _descripcion), UiKit.Campo("Precio USD", _precio), UiKit.Campo("Peso kg", _peso),
            UiKit.Campo("Stock (solo alta)", _stock)]);
        var panelProductos = new Panel { Dock = DockStyle.Fill };
        panelProductos.Controls.Add(_productos);
        panelProductos.Controls.Add(UiKit.Fila(guardar, nuevo, activar));
        panelProductos.Controls.Add(formulario);
        panelProductos.Controls.Add(UiKit.Fila(UiKit.Etiqueta("Buscar:"), _buscar,
            UiKit.Boton("&Filtrar", async (_, _) => await IntentarAsync(RefrescarAsync))));

        var panelKits = new Panel { Dock = DockStyle.Fill };
        _desglose.Dock = DockStyle.Bottom;
        _desglose.Height = 130;
        panelKits.Controls.Add(_kits);
        panelKits.Controls.Add(_desglose);
        panelKits.Controls.Add(UiKit.Fila(UiKit.Campo("Nuevo kit SKU", _nuevoKitSku), UiKit.Campo("Nombre", _nuevoKitNombre), crearKit));
        panelKits.Controls.Add(UiKit.Fila(UiKit.Campo("Kit", _kit), UiKit.Campo("Componente (SKU o kit)", _componente),
            UiKit.Campo("Cant.", _cantidadComponente), agregarComp, quitarComp,
            UiKit.Boton("Desglose y peso (Visitor)", async (_, _) => await IntentarAsync(DesglosarAsync))));

        var division = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        division.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        division.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        division.Controls.Add(UiKit.Grupo("Productos", panelProductos), 0, 0);
        division.Controls.Add(UiKit.Grupo("Kits (Composite: productos y otros kits, sin ciclos)", panelKits), 0, 1);
        Contenido.Controls.Add(division);
    }

    public override string NombreVista => NombresVista.Catalogo;

    public override void AplicarPermisos()
    {
        var admin = Ui.Puede(Permiso.GestionarCatalogo);
        foreach (var control in _soloAdmin) control.Enabled = admin;
    }

    public override async Task RefrescarAsync()
    {
        var (productos, kits) = await Ui.EjecutarAsync(async sp =>
        {
            var consultas = sp.GetRequiredService<IConsultasOperativas>();
            return (await consultas.ListarProductosAsync(_buscar.Text), await consultas.ListarKitsAsync());
        });
        _productos.Enlazar(productos, p => p.Sku);
        _kits.Enlazar(kits, k => k.Sku);

        var kitActual = _kit.SelectedItem as string;
        _kit.Items.Clear();
        _kit.Items.AddRange(kits.Select(k => (object)k.Sku).ToArray());
        if (kitActual is not null && _kit.Items.Contains(kitActual)) _kit.SelectedItem = kitActual;
        else if (_kit.Items.Count > 0) _kit.SelectedIndex = 0;

        _componente.Items.Clear();
        _componente.Items.AddRange(productos.Select(p => (object)p.Sku).Concat(kits.Select(k => (object)k.Sku)).ToArray());
        if (_componente.Items.Count > 0) _componente.SelectedIndex = 0;
    }

    private void CargarProductoSeleccionado()
    {
        if (_productos.Seleccion<ProductoDto>() is not { } p) return;
        _sku.Text = p.Sku;
        _sku.ReadOnly = true;
        _nombre.Text = p.Nombre;
        _categoria.Text = p.Categoria;
        _descripcion.Text = p.Descripcion;
        _precio.Value = p.Precio;
        _peso.Value = p.PesoKg;
        _stock.Value = p.Stock;
        _stock.Enabled = false; //el stock se cambia en inventario (con movimiento trazable)
    }

    private void LimpiarFormulario()
    {
        _sku.ReadOnly = false;
        _stock.Enabled = true;
        foreach (var t in new[] { _sku, _nombre, _categoria, _descripcion }) t.Clear();
        _precio.Value = 1m;
        _peso.Value = 0m;
        _stock.Value = 0m;
        _sku.Focus();
    }

    private async Task GuardarProductoAsync()
    {
        var dto = new ProductoEdicionDto(_sku.Text, _nombre.Text, _categoria.Text, _descripcion.Text, _precio.Value, _peso.Value, (int)_stock.Value);
        var esNuevo = !_sku.ReadOnly;
        var r = await Ui.EjecutarAsync(sp => esNuevo
            ? sp.GetRequiredService<ServicioCatalogo>().CrearProductoAsync(dto)
            : sp.GetRequiredService<ServicioCatalogo>().ActualizarProductoAsync(dto));
        Informar(r);
        await NotificarAsync(EventoVista.CatalogoModificado);
        await RefrescarAsync();
    }

    private async Task AlternarActivoAsync()
    {
        if (_productos.Seleccion<ProductoDto>() is not { } p) return;
        Informar(await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioCatalogo>().CambiarActivoProductoAsync(p.Sku, !p.Activo)));
        await NotificarAsync(EventoVista.CatalogoModificado);
        await RefrescarAsync();
    }

    private async Task AgregarComponenteAsync()
    {
        if (_kit.SelectedItem is not string kit || _componente.SelectedItem is not string comp) return;
        Informar(await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioCatalogo>().AgregarComponenteAsync(kit, comp, (int)_cantidadComponente.Value)));
        await NotificarAsync(EventoVista.CatalogoModificado);
        await RefrescarAsync();
    }

    private async Task QuitarComponenteAsync()
    {
        if (_kit.SelectedItem is not string kit || _componente.SelectedItem is not string comp) return;
        if (!UiKit.Confirmar(FindForm(), $"¿Quitar {comp} del kit {kit}?")) return;
        Informar(await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioCatalogo>().QuitarComponenteAsync(kit, comp)));
        await NotificarAsync(EventoVista.CatalogoModificado);
        await RefrescarAsync();
    }

    private async Task CrearKitAsync()
    {
        if (_componente.SelectedItem is not string comp) return;
        Informar(await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioCatalogo>().CrearKitAsync(
            _nuevoKitSku.Text, _nuevoKitNombre.Text, "", [new ComponenteKitDto(comp, (int)_cantidadComponente.Value)])));
        await NotificarAsync(EventoVista.CatalogoModificado);
        await RefrescarAsync();
    }

    private async Task DesglosarAsync()
    {
        var sku = _kits.Seleccion<KitDto>()?.Sku ?? _kit.SelectedItem as string;
        if (sku is null) return;
        var d = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioCatalogo>().DesglosarAsync(sku));
        _desglose.Text = $"{d.Sku} · {d.Nombre} · {UiKit.Dinero(d.Precio)} · peso logístico {d.PesoTotalKg:0.00} kg" + Environment.NewLine +
                         "Desglose físico (SKU finales):" + Environment.NewLine +
                         string.Join(Environment.NewLine, d.Lineas.Select(l => $"  {l.Sku,-6} {l.Nombre,-16} x{l.Cantidad}  {l.PesoTotalKg:0.00} kg")) +
                         Environment.NewLine + "Visita anidada:" + Environment.NewLine + string.Join(Environment.NewLine, d.Traza);
        MostrarExito($"Desglose de {sku} generado por los visitantes.");
    }
}
