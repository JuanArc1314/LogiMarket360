using LogiMarket.Application.Comun;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Devoluciones;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

//09 - devoluciones: validacion de politica por motivo (template method)
//reintegro y reembolso unico
public sealed class DevolucionesView : VistaBase
{
    private readonly DataGridView _entregados = UiKit.Grid("Pedidos entregados");
    private readonly ComboBox _motivo = UiKit.Combo("Motivo", 170);
    private readonly CheckBox _sinUso = new() { Text = "Producto sin uso", AutoSize = true, Margin = new Padding(0, 26, 12, 0) };
    private readonly Label _politica = new() { Font = Estilos.Normal, Dock = DockStyle.Fill, AccessibleName = "Validación de política" };
    private readonly Label _resumen = new() { Font = Estilos.Mono, Dock = DockStyle.Fill, AccessibleName = "Resumen del reembolso" };
    private readonly Button _confirmar;

    public DevolucionesView(ContextoUi ui) : base(ui, "Devoluciones y reintegros", "Solicitud total, política de motivo y reembolso único")
    {
        _motivo.Items.AddRange(Enum.GetNames<MotivoDevolucion>());
        _motivo.SelectedIndex = 0;
        _motivo.SelectedIndexChanged += async (_, _) => await IntentarAsync(PintarAsync);
        _sinUso.CheckedChanged += async (_, _) => await IntentarAsync(PintarAsync);
        _entregados.Columna(nameof(PedidoResumenDto.Codigo), "Código", 60).Columna(nameof(PedidoResumenDto.Cliente), "Cliente", 110)
            .Columna(nameof(PedidoResumenDto.Estado), "Estado", 70).Columna(nameof(PedidoResumenDto.Total), "Total pagado", 60, "M")
            .Columna(nameof(PedidoResumenDto.Devuelto), "Devuelto", 45);
        _entregados.SelectionChanged += async (_, _) => await IntentarAsync(PintarAsync);

        _confirmar = UiKit.Boton("&Confirmar devolución", async (_, _) => await IntentarAsync(ConfirmarAsync), primario: true);

        var derecha = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        derecha.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        derecha.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        derecha.Controls.Add(UiKit.Grupo("Validación de política", _politica), 0, 0);
        derecha.Controls.Add(UiKit.Grupo("Resumen", _resumen), 0, 1);

        var cuerpo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        cuerpo.Controls.Add(UiKit.Grupo("Pedidos ENTREGADOS", _entregados), 0, 0);
        cuerpo.Controls.Add(derecha, 1, 0);

        Contenido.Controls.Add(cuerpo);
        Contenido.Controls.Add(UiKit.Fila(UiKit.Campo("Motivo", _motivo), _sinUso, _confirmar));
    }

    public override string NombreVista => NombresVista.Devoluciones;

    public override void AplicarPermisos() => _confirmar.Enabled = Ui.Puede(Permiso.ProcesarDevoluciones);

    public override async Task RefrescarAsync()
    {
        var pedidos = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarPedidosAsync());
        _entregados.Enlazar(pedidos.Where(p => p.Estado == EstadoPedido.ENTREGADO).ToList(), p => p.Id);
        await PintarAsync();
    }

    private async Task PintarAsync()
    {
        if (_entregados.Seleccion<PedidoResumenDto>() is not { } p)
        {
            _politica.Text = "Seleccione un pedido entregado.";
            _resumen.Text = "";
            return;
        }
        var d = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ObtenerPedidoAsync(p.Id));
        if (d is null) return;
        var ahora = Ui.Singleton<IReloj>().Ahora;
        var dias = d.FechaEntrega is { } f ? (ahora.Date - f.Date).Days : -1;
        var motivo = Enum.Parse<MotivoDevolucion>((string)_motivo.SelectedItem!);
        var limite = motivo == MotivoDevolucion.DEFECTO ? 30 : 7;
        _politica.Text =
            $"☐ Entrega registrada: {d.FechaEntrega:dd/MM/yyyy}  (hace {dias} día/s)\n" +
            $"☐ DEFECTO: hasta 30 días\n☐ ARREPENTIMIENTO: hasta 7 días y sin uso\n" +
            $"☐ Reintegrar stock: según motivo aprobado\n\n" +
            $"Evaluación previa ({motivo}): " +
            (d.Devuelto ? "YA DEVUELTO" : dias > limite ? $"FUERA DE PLAZO ({dias} > {limite})" :
                motivo == MotivoDevolucion.ARREPENTIMIENTO && !_sinUso.Checked ? "REQUIERE PRODUCTO SIN USO" : "dentro de la política");
        _resumen.Text =
            $"Total pagado        : {UiKit.Dinero(d.Total),12}\nEnvío (no se reembolsa): {UiKit.Dinero(-d.Tarifa),9}\n" +
            $"Reembolso a emitir  : {UiKit.Dinero(d.ImporteProductosNeto),12}" +
            (d.Devolucion is { } dev ? $"\n\nDevolución registrada: {dev.Motivo} · {dev.Referencia}" : "");
    }

    private async Task ConfirmarAsync()
    {
        if (_entregados.Seleccion<PedidoResumenDto>() is not { } p) return;
        var motivo = Enum.Parse<MotivoDevolucion>((string)_motivo.SelectedItem!);
        if (!UiKit.Confirmar(FindForm(), $"¿Registrar la devolución TOTAL de {p.Codigo} por {motivo}? Se emitirá un reembolso único.")) return;
        var r = await Ui.EjecutarComandoAsync(f => f.Devolver(new SolicitudDevolucion(p.Id, motivo, _sinUso.Checked)));
        Informar(r);
        if (r.Datos is ResultadoDevolucion d)
            MostrarInfo($"{(d.Aceptada ? "✔" : "✖")} {d.Mensaje} · Pasos: {string.Join(" → ", d.PasosEjecutados)}");
        if (r.Exito) await NotificarAsync(EventoVista.DevolucionProcesada);
        await RefrescarAsync();
    }
}
