using LogiMarket.Application.Catalogo;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Seguridad;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

//10 - inventario y movimientos: existencias, alertas y registros trazables (+/−, motivo, referencia)
public sealed class InventarioView : VistaBase
{
    private readonly DataGridView _existencias = UiKit.Grid("Existencias");
    private readonly DataGridView _movimientos = UiKit.Grid("Movimientos de inventario");
    private readonly ComboBox _filtroSku = UiKit.Combo("Filtrar movimientos por SKU", 120);
    private readonly NumericUpDown _nuevoStock = UiKit.Numero("Nuevo stock", 0m, 100000m);
    private readonly TextBox _motivo = UiKit.Texto("Motivo del ajuste", 220);
    private readonly Button _ajustar;

    public InventarioView(ContextoUi ui) : base(ui, "Inventario y movimientos", "Existencias, alertas y registros trazables de movimiento")
    {
        _existencias.Columna(nameof(ProductoDto.Sku), "SKU", 50).Columna(nameof(ProductoDto.Nombre), "Producto", 120)
            .Columna(nameof(ProductoDto.Stock), "Stock", 50);
        _existencias.CellFormatting += (_, e) =>
        {
            if (_existencias.Rows[e.RowIndex].DataBoundItem is ProductoDto { Stock: <= 3 } && e.CellStyle is not null)
                e.CellStyle.ForeColor = Estilos.Aviso; //alerta visual de stock bajo
        };
        _existencias.SelectionChanged += (_, _) => { if (_existencias.Seleccion<ProductoDto>() is { } p) _nuevoStock.Value = p.Stock; };
        _movimientos.Columna(nameof(MovimientoDto.Fecha), "Fecha", 80, "dd/MM/yyyy HH:mm").Columna(nameof(MovimientoDto.Sku), "SKU", 40)
            .Columna(nameof(MovimientoDto.Cantidad), "+/−", 35, "+#;-#;0").Columna(nameof(MovimientoDto.Tipo), "Motivo", 70)
            .Columna(nameof(MovimientoDto.Referencia), "Referencia", 70).Columna(nameof(MovimientoDto.StockResultante), "Stock result.", 50)
            .Columna(nameof(MovimientoDto.Usuario), "Usuario", 50);
        _filtroSku.SelectedIndexChanged += async (_, _) => await IntentarAsync(CargarMovimientosAsync);
        _ajustar = UiKit.Boton("A&justar stock", async (_, _) => await IntentarAsync(AjustarAsync), primario: true);

        var cuerpo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
        cuerpo.Controls.Add(UiKit.Grupo("Existencias (naranja = stock ≤ 3)", _existencias), 0, 0);
        cuerpo.Controls.Add(UiKit.Grupo("Movimientos", _movimientos), 1, 0);

        Contenido.Controls.Add(cuerpo);
        Contenido.Controls.Add(UiKit.Fila(UiKit.Campo("Nuevo stock (producto seleccionado)", _nuevoStock),
            UiKit.Campo("Motivo", _motivo), _ajustar, UiKit.Campo("Movimientos de", _filtroSku)));
    }

    public override string NombreVista => NombresVista.Inventario;

    public override void AplicarPermisos() => _ajustar.Enabled = Ui.Puede(Permiso.AjustarInventario);

    public override async Task RefrescarAsync()
    {
        var productos = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarProductosAsync());
        _existencias.Enlazar(productos, p => p.Sku);
        var actual = _filtroSku.SelectedItem as string;
        _filtroSku.Items.Clear();
        _filtroSku.Items.Add("TODOS");
        _filtroSku.Items.AddRange(productos.Select(p => (object)p.Sku).ToArray());
        _filtroSku.SelectedItem = actual is not null && _filtroSku.Items.Contains(actual) ? actual : "TODOS";
        await CargarMovimientosAsync();
    }

    private async Task CargarMovimientosAsync()
    {
        var sku = _filtroSku.SelectedItem as string;
        var movimientos = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>()
            .ListarMovimientosAsync(sku is null or "TODOS" ? null : sku));
        _movimientos.Enlazar(movimientos);
        MostrarInfo($"{movimientos.Count} movimiento(s).");
    }

    private async Task AjustarAsync()
    {
        if (_existencias.Seleccion<ProductoDto>() is not { } p) return;
        if (!UiKit.Confirmar(FindForm(), $"¿Ajustar el stock de {p.Sku} de {p.Stock} a {_nuevoStock.Value}?")) return;
        Informar(await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioCatalogo>().AjustarStockAsync(p.Sku, (int)_nuevoStock.Value, _motivo.Text)));
        await NotificarAsync(EventoVista.InventarioAjustado);
        await RefrescarAsync();
    }
}
