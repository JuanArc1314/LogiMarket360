using LogiMarket.Application.Catalogo;
using LogiMarket.Application.Comprobantes;
using LogiMarket.Application.Consultas;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

//11 - reportes y comprobantes: filtro interpretado (interpreter)
//comprobante x medio (bridge) y demostracion de 1.000 filas con metadatos compartidos (flyweight)
public sealed class ReportesView : VistaBase
{
    private readonly TextBox _filtro = UiKit.Texto("Filtro de pedidos", 420);
    private readonly Label _arbol = new() { AutoSize = true, Font = Estilos.Mono, ForeColor = Estilos.TextoSuave, Margin = new Padding(0, 10, 0, 0) };
    private readonly DataGridView _resultado = UiKit.Grid("Resultado del filtro");
    private readonly ComboBox _pedido = UiKit.Combo("Pedido del comprobante", 230);
    private readonly ComboBox _tipo = UiKit.Combo("Tipo de comprobante", 120);
    private readonly ComboBox _medio = UiKit.Combo("Medio de salida", 130);
    private readonly DataGridView _flyweight = UiKit.Grid("Filas de catálogo (Flyweight)");
    private readonly Label _informe = new() { Font = Estilos.Mono, Dock = DockStyle.Bottom, Height = 70, AccessibleName = "Informe Flyweight" };

    public ReportesView(ContextoUi ui) : base(ui, "Reportes y comprobantes", "Filtros de consulta y combinación de formatos y salidas")
    {
        _filtro.Text = "ESTADO = PAGADO AND TOTAL >= 50";
        _resultado.Columna(nameof(PedidoResumenDto.Codigo), "Código", 60).Columna(nameof(PedidoResumenDto.Cliente), "Cliente", 110)
            .Columna(nameof(PedidoResumenDto.Estado), "Estado", 70).Columna(nameof(PedidoResumenDto.Total), "Total USD", 60, "M")
            .Columna(nameof(PedidoResumenDto.Fecha), "Fecha", 80, "dd/MM/yyyy");
        _pedido.Format += (_, e) => e.Value = e.ListItem is PedidoResumenDto p ? $"{p.Codigo} · {p.Cliente} · {p.Estado}" : e.Value;
        _tipo.Items.AddRange(Enum.GetNames<TipoComprobante>());
        _tipo.SelectedIndex = 0;
        _medio.Items.AddRange([MediosSalida.VistaPrevia, MediosSalida.ArchivoTxt]);
        _medio.SelectedIndex = 0;
        _flyweight.Columna(nameof(FilaCatalogoVisual.Numero), "#", 25).Columna(nameof(FilaCatalogoVisual.Sku), "SKU", 35)
            .Columna(nameof(FilaCatalogoVisual.Nombre), "Nombre", 80).Columna(nameof(FilaCatalogoVisual.Categoria), "Categoría", 70)
            .Columna(nameof(FilaCatalogoVisual.Stock), "Stock", 35).Columna(nameof(FilaCatalogoVisual.Cantidad), "Cant.", 35)
            .Columna(nameof(FilaCatalogoVisual.PrecioVenta), "Precio", 45, "M");

        var panelFiltro = new Panel { Dock = DockStyle.Fill };
        panelFiltro.Controls.Add(_resultado);
        panelFiltro.Controls.Add(UiKit.Fila(_arbol));
        panelFiltro.Controls.Add(UiKit.Fila(_filtro, UiKit.Boton("&Ejecutar filtro", async (_, _) => await IntentarAsync(FiltrarAsync), primario: true)));
        panelFiltro.Controls.Add(UiKit.Fila(UiKit.Etiqueta("Gramática: ESTADO = valor · TOTAL >=|<=|>|<|= número · CLIENTE = texto · AND/OR · ( )")));

        var panelFly = new Panel { Dock = DockStyle.Fill };
        panelFly.Controls.Add(_flyweight);
        panelFly.Controls.Add(_informe);
        panelFly.Controls.Add(UiKit.Fila(UiKit.Boton("Generar 1.000 filas (Flyweight)", async (_, _) => await IntentarAsync(FlyweightAsync))));

        var cuerpo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        cuerpo.Controls.Add(UiKit.Grupo("Filtro interpretado (Interpreter)", panelFiltro), 0, 0);
        cuerpo.Controls.Add(UiKit.Grupo("Catálogo visual compartido", panelFly), 1, 0);

        Contenido.Controls.Add(cuerpo);
        Contenido.Controls.Add(UiKit.Grupo("Comprobantes (Bridge: comprobante × medio de salida)",
            UiKit.Fila(UiKit.Campo("Pedido", _pedido), UiKit.Campo("Comprobante", _tipo), UiKit.Campo("Salida", _medio),
                UiKit.Boton("Em&itir", async (_, _) => await IntentarAsync(EmitirAsync), primario: true)), alto: 92));
    }

    public override string NombreVista => NombresVista.Reportes;

    public override async Task RefrescarAsync()
    {
        var pedidos = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarPedidosAsync());
        var actual = (_pedido.SelectedItem as PedidoResumenDto)?.Id;
        _pedido.Items.Clear();
        _pedido.Items.AddRange(pedidos.Where(p => p.Estado is not (Domain.Pedidos.EstadoPedido.BORRADOR or Domain.Pedidos.EstadoPedido.VALIDADO
            or Domain.Pedidos.EstadoPedido.CANCELADO)).Cast<object>().ToArray());
        _pedido.SelectedItem = _pedido.Items.Cast<PedidoResumenDto>().FirstOrDefault(p => p.Id == actual);
        if (_pedido.SelectedIndex < 0 && _pedido.Items.Count > 0) _pedido.SelectedIndex = 0;
    }

    private async Task FiltrarAsync()
    {
        var r = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioReportes>().FiltrarPedidosAsync(_filtro.Text));
        _arbol.Text = $"Árbol: {r.ArbolInterpretado}";
        _resultado.Enlazar(r.Pedidos);
        MostrarExito($"{r.Pedidos.Count} pedido(s) cumplen el filtro.");
    }

    private async Task EmitirAsync()
    {
        if (_pedido.SelectedItem is not PedidoResumenDto p) return;
        var tipo = Enum.Parse<TipoComprobante>((string)_tipo.SelectedItem!);
        var medio = (string)_medio.SelectedItem!;
        var salida = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioComprobantes>().EmitirAsync(p.Id, tipo, medio));
        MostrarExito($"{tipo} → {medio}: {salida}");
    }

    private async Task FlyweightAsync()
    {
        var productos = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarProductosAsync());
        var (filas, i) = DemostracionFlyweight.Generar(productos, 1000);
        _flyweight.DataSource = filas.ToList();
        _informe.Text =
            $"Filas: {i.Filas} · SKU base: {i.ProductosBase} · metadatos compartidos: {i.InstanciasMetadatosCompartidas} vs sin compartir: {i.InstanciasMetadatosSinCompartir}\n" +
            $"ReferenceEquals(fila1, fila6) = {ReferenceEquals(filas[0].Metadatos, filas[5].Metadatos)} · " +
            $"bytes con Flyweight: {i.BytesConFlyweight:N0} · sin Flyweight: {i.BytesSinFlyweight:N0} · ahorro: {i.AhorroBytes:N0}\n{i.Condiciones}";
    }
}
