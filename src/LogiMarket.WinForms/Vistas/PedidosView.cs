using LogiMarket.Application.Comprobantes;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Pedidos;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

/// 06. Pedidos e historial: consulta de estados, detalle y trazabilidad de cambios.
public sealed class PedidosView : VistaBase
{
    private readonly DataGridView _pedidos = UiKit.Grid("Pedidos");
    private readonly DataGridView _lineas = UiKit.Grid("Detalle del pedido");
    private readonly DataGridView _historial = UiKit.Grid("Historial de estados");
    private readonly TextBox _buscar = UiKit.Texto("Buscar código o cliente", 200);
    private readonly ComboBox _estado = UiKit.Combo("Filtrar por estado", 130);
    private readonly Label _resumen = new() { AutoSize = true, Font = Estilos.Negrita, Margin = new Padding(0, 10, 0, 0) };
    private readonly Button _cancelar, _clonar;
    private IReadOnlyList<PedidoResumenDto> _todos = [];

    public PedidosView(ContextoUi ui) : base(ui, "Pedidos e historial", "Consulta de estados, detalle y trazabilidad de eventos")
    {
        _estado.Items.Add("TODOS");
        _estado.Items.AddRange(Enum.GetNames<EstadoPedido>());
        _estado.SelectedIndex = 0;
        _estado.SelectedIndexChanged += (_, _) => Filtrar();

        _pedidos.Columna(nameof(PedidoResumenDto.Codigo), "Código", 60).Columna(nameof(PedidoResumenDto.Cliente), "Cliente", 110)
            .Columna(nameof(PedidoResumenDto.Estado), "Estado", 70).Columna(nameof(PedidoResumenDto.Entrega), "Entrega", 60)
            .Columna(nameof(PedidoResumenDto.Unidades), "Uds.", 35).Columna(nameof(PedidoResumenDto.Total), "Total USD", 60, "M")
            .Columna(nameof(PedidoResumenDto.Fecha), "Fecha", 80, "dd/MM/yyyy HH:mm").Columna(nameof(PedidoResumenDto.Devuelto), "Devuelto", 45);
        _pedidos.SelectionChanged += async (_, _) => await IntentarAsync(CargarDetalleAsync);
        _lineas.Columna(nameof(LineaPedidoDto.Sku), "SKU", 70).Columna(nameof(LineaPedidoDto.Descripcion), "Descripción", 110)
            .Columna(nameof(LineaPedidoDto.Cantidad), "Cant.", 35).Columna(nameof(LineaPedidoDto.PrecioUnitario), "P. unit.", 55, "M")
            .Columna(nameof(LineaPedidoDto.Subtotal), "Subtotal", 55, "M");
        _historial.Columna(nameof(HistorialEstadoDto.Fecha), "Fecha", 80, "dd/MM/yyyy HH:mm:ss").Columna(nameof(HistorialEstadoDto.Anterior), "De", 55)
            .Columna(nameof(HistorialEstadoDto.Nuevo), "A", 55).Columna(nameof(HistorialEstadoDto.Usuario), "Usuario", 50)
            .Columna(nameof(HistorialEstadoDto.Nota), "Nota", 120);

        _cancelar = UiKit.Boton("&Cancelar pedido", async (_, _) => await IntentarAsync(CancelarAsync));
        _clonar = UiKit.Boton("C&lonar como borrador", async (_, _) => await IntentarAsync(ClonarAsync));

        var detalle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        detalle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        detalle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        detalle.Controls.Add(UiKit.Grupo("Detalle (instantánea de precios)", _lineas), 0, 0);
        detalle.Controls.Add(UiKit.Grupo("Historial de estados", _historial), 1, 0);

        var division = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        division.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        division.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        division.Controls.Add(_pedidos, 0, 0);
        division.Controls.Add(detalle, 0, 1);

        Contenido.Controls.Add(division);
        Contenido.Controls.Add(UiKit.Fila(_cancelar, _clonar,
            UiKit.Boton("Comprobante (vista previa)", async (_, _) => await IntentarAsync(ComprobanteAsync)), _resumen));
        Contenido.Controls.Add(UiKit.Fila(UiKit.Etiqueta("Buscar:"), _buscar, UiKit.Etiqueta("Estado:"), _estado,
            UiKit.Boton("&Filtrar", (_, _) => Filtrar()), UiKit.Boton("&Actualizar", async (_, _) => await IntentarAsync(RefrescarAsync))));
    }

    public override string NombreVista => NombresVista.Pedidos;

    public override void AplicarPermisos() => _cancelar.Enabled = _clonar.Enabled = Ui.Puede(Permiso.GestionarPedidos);

    public override async Task RefrescarAsync()
    {
        _todos = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarPedidosAsync());
        Filtrar();
    }

    private void Filtrar()
    {
        var texto = _buscar.Text.Trim();
        var estado = (string?)_estado.SelectedItem;
        var filtrados = _todos
            .Where(p => texto.Length == 0 || p.Codigo.Contains(texto, StringComparison.OrdinalIgnoreCase) || p.Cliente.Contains(texto, StringComparison.OrdinalIgnoreCase))
            .Where(p => estado is null or "TODOS" || p.Estado.ToString() == estado)
            .ToList();
        _pedidos.Enlazar(filtrados, p => p.Id);
        MostrarInfo($"{filtrados.Count} pedido(s).");
    }

    private async Task CargarDetalleAsync()
    {
        if (_pedidos.Seleccion<PedidoResumenDto>() is not { } p) return;
        var d = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ObtenerPedidoAsync(p.Id));
        if (d is null) return;
        _lineas.Enlazar(d.Lineas);
        _historial.Enlazar(d.Historial);
        _resumen.Text = $"{d.Codigo}: {UiKit.Dinero(d.Subtotal)} − {UiKit.Dinero(d.Descuento)} ({d.Politica}) + {UiKit.Dinero(d.Tarifa)} = {UiKit.Dinero(d.Total)}" +
                        (d.Pago is null ? "" : $" · cobro {d.Pago.Referencia}");
    }

    private async Task CancelarAsync()
    {
        if (_pedidos.Seleccion<PedidoResumenDto>() is not { } p) return;
        if (!UiKit.Confirmar(FindForm(), $"¿Cancelar el pedido {p.Codigo}? Esta acción no se puede deshacer.")) return;
        var r = await Ui.EjecutarComandoAsync(f => f.Cancelar(p.Id, "Cancelado desde Pedidos e historial"));
        Informar(r);
        if (r.Exito) await NotificarAsync(EventoVista.PedidoCancelado);
        await RefrescarAsync();
    }

    private async Task ClonarAsync()
    {
        if (_pedidos.Seleccion<PedidoResumenDto>() is not { } p) return;
        var copia = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().ClonarComoBorradorAsync(p.Id));
        MostrarExito($"{p.Codigo} clonado como {copia.Codigo} (BORRADOR).");
        await NotificarAsync(EventoVista.BorradorGuardado);
        await RefrescarAsync();
    }

    private async Task ComprobanteAsync()
    {
        if (_pedidos.Seleccion<PedidoResumenDto>() is not { } p) return;
        MostrarInfo(await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioComprobantes>()
            .EmitirAsync(p.Id, TipoComprobante.Venta, MediosSalida.VistaPrevia)));
    }
}
