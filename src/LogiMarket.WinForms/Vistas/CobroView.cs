using LogiMarket.Application.Confirmacion;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Eventos;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Pedidos;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

//07 - confirmacion y cobro: resumen de importes
//proveedor simulado y cobro protegido (command -> facade -> chain -> proxy -> decorator ->
//adapter) con clave de idempotencia
public sealed class CobroView : VistaBase
{
    private readonly ComboBox _pedido = UiKit.Combo("Pedido a cobrar", 260);
    private readonly TextBox _clave = UiKit.Texto("Clave de idempotencia", 300);
    private readonly Label _resumen = new() { Font = Estilos.Mono, Dock = DockStyle.Fill, AccessibleName = "Resumen de importes" };
    private readonly TextBox _resultado = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Font = Estilos.Mono, ScrollBars = ScrollBars.Vertical, AccessibleName = "Resultado del cobro" };
    private readonly Label _proveedor = new() { AutoSize = true, Font = Estilos.Negrita, Margin = new Padding(0, 26, 0, 0) };
    private readonly Button _confirmar, _repetir, _validar;

    public CobroView(ContextoUi ui) : base(ui, "Confirmación y cobro", "Resumen de importes, selección de proveedor y cobro protegido")
    {
        _pedido.Format += (_, e) => e.Value = e.ListItem is PedidoResumenDto p ? $"{p.Codigo} · {p.Cliente} · {p.Estado} · {UiKit.Dinero(p.Total)}" : e.Value;
        _pedido.SelectedIndexChanged += async (_, _) => await IntentarAsync(CargarResumenAsync);
        _clave.ReadOnly = true;

        _validar = UiKit.Boton("&Validar", async (_, _) => await IntentarAsync(ValidarAsync));
        _confirmar = UiKit.Boton("&Confirmar y cobrar", async (_, _) => await IntentarAsync(() => ConfirmarAsync(repetir: false)), primario: true);
        _repetir = UiKit.Boton("&Repetir con la misma clave", async (_, _) => await IntentarAsync(() => ConfirmarAsync(repetir: true)));

        var cuerpo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        cuerpo.Controls.Add(UiKit.Grupo("Resumen del pedido", _resumen), 0, 0);
        cuerpo.Controls.Add(UiKit.Grupo("Resultado (eslabones evaluados, referencia, idempotencia)", _resultado), 1, 0);

        Contenido.Controls.Add(cuerpo);
        Contenido.Controls.Add(UiKit.Fila(_validar, _confirmar, _repetir,
            UiKit.Boton("Nueva clave", (_, _) => NuevaClave())));
        Contenido.Controls.Add(UiKit.Fila(UiKit.Campo("Pedido (BORRADOR o VALIDADO)", _pedido), UiKit.Campo("Clave de idempotencia", _clave), _proveedor));
        ui.Singleton<ConfiguracionDemostracion>().Cambio += (_, _) => { if (IsHandleCreated) BeginInvoke((MethodInvoker)PintarProveedor); };
    }

    public override string NombreVista => NombresVista.Cobro;

    public override void AplicarPermisos()
    {
        //solo ux: aunque el boton este activo, el proxy y la cadena rechazan roles no autorizados
        _confirmar.Enabled = _repetir.Enabled = _validar.Enabled = Ui.Puede(Permiso.CobrarPedidos);
        PintarProveedor();
    }

    public override async Task RefrescarAsync()
    {
        var pedidos = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarPedidosAsync());
        var actual = (_pedido.SelectedItem as PedidoResumenDto)?.Id;
        _pedido.Items.Clear();
        _pedido.Items.AddRange(pedidos.Where(p => p.Estado is EstadoPedido.BORRADOR or EstadoPedido.VALIDADO).Cast<object>().ToArray());
        _pedido.SelectedItem = _pedido.Items.Cast<PedidoResumenDto>().FirstOrDefault(p => p.Id == actual);
        if (_pedido.SelectedIndex < 0)
        {
            _resumen.Text = _pedido.Items.Count == 0 ? "No hay pedidos pendientes de cobro." : "Seleccione un pedido.";
            _clave.Clear();
        }
        PintarProveedor();
    }

    private void PintarProveedor() =>
        _proveedor.Text = $"Proveedor simulado activo: {Ui.Singleton<ConfiguracionDemostracion>().ProveedorPago}";

    private void NuevaClave() => _clave.Text = $"CONF-{DateTime.Now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";

    private async Task CargarResumenAsync()
    {
        if (_pedido.SelectedItem is not PedidoResumenDto p) return;
        NuevaClave(); //una clave por intento de confirmacion se conserva para reintentos
        var d = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ObtenerPedidoAsync(p.Id));
        if (d is null) return;
        _resumen.Text =
            $"Pedido     : {d.Codigo} ({d.Estado})\nCliente    : {d.Cliente} ({d.CategoriaCliente})\n" +
            string.Join("\n", d.Lineas.Select(l => $"  {l.Sku,-16} x{l.Cantidad,-3} {UiKit.Dinero(l.Subtotal),12}")) +
            $"\n\nSubtotal   : {UiKit.Dinero(d.Subtotal),14}\nDescuento  : {UiKit.Dinero(-d.Descuento),14} ({d.Politica})" +
            $"\nEnvío      : {UiKit.Dinero(d.Tarifa),14} ({d.Entrega}, {d.PlazoDias} día/s)\nTOTAL      : {UiKit.Dinero(d.Total),14}";
    }

    private async Task ValidarAsync()
    {
        if (_pedido.SelectedItem is not PedidoResumenDto p) return;
        var r = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().ValidarAsync(p.Id));
        _resultado.Text = $"Eslabones: {string.Join(" → ", r.EslabonesEvaluados)}\r\n" + (r.EsValido ? "VÁLIDO" : $"RECHAZO: {r.Rechazo}");
        if (r.EsValido) { MostrarExito("Pedido VALIDADO."); await NotificarAsync(EventoVista.BorradorGuardado); await RefrescarAsync(); }
        else MostrarError(r.Rechazo!.ToString());
    }

    private async Task ConfirmarAsync(bool repetir)
    {
        var id = repetir ? _ultimoPedidoId : (_pedido.SelectedItem as PedidoResumenDto)?.Id;
        var clave = repetir ? _ultimaClave : _clave.Text;
        if (id is null || string.IsNullOrWhiteSpace(clave))
        {
            MostrarError(repetir ? "No hay una confirmación previa para repetir." : "Seleccione un pedido.");
            return;
        }
        if (!repetir && !UiKit.Confirmar(FindForm(), "Se cobrará el pedido por el total mostrado y se descontará el stock.\n¿Confirmar?"))
            return;

        var r = await Ui.EjecutarComandoAsync(f => f.Confirmar(id.Value, clave));
        var detalle = r.Datos as ResultadoConfirmacion;
        _resultado.Text =
            $"Comando  : ConfirmarPedido (pedido {id}, clave {clave})\r\nResultado: {(r.Exito ? "ÉXITO" : "RECHAZO")} {r.Codigo}\r\n{r.Mensaje}\r\n" +
            (detalle is null ? "" :
                $"Eslabones: {string.Join(" → ", detalle.EslabonesEvaluados)}\r\nReferencia: {detalle.ReferenciaPago}\r\n" +
                $"Repetición idempotente: {(detalle.EsRepeticion ? "SÍ (no se cobró de nuevo)" : "NO")}");
        Informar(r);

        if (r.Exito)
        {
            _ultimoPedidoId = id;
            _ultimaClave = clave;
            //post-confirmacion: se procesa el outbox (observer) y el mediator refresca las vistas
            await Ui.EjecutarAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());
            await NotificarAsync(EventoVista.PedidoConfirmado);
            await RefrescarAsync();
        }
    }

    private string? _ultimaClave;
    private int? _ultimoPedidoId;
}
