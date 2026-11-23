using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Eventos;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

/// Pantalla de demostración/pruebas (solo ADMIN): cambia el proveedor simulado, induce fallo de cobro
/// o de notificador, desplaza días y restablece la base controlada. No altera reglas ni resultados.
public sealed class DemostracionView : VistaBase
{
    private readonly RadioButton _legado = new() { Text = "Pasarela LEGADA (Adapter)", AutoSize = true, Checked = true };
    private readonly RadioButton _alterno = new() { Text = "Pasarela ALTERNA", AutoSize = true };
    private readonly RadioButton _email = new() { Text = "Notificar por EMAIL", AutoSize = true, Checked = true };
    private readonly RadioButton _sms = new() { Text = "Notificar por SMS", AutoSize = true };
    private readonly CheckBox _rechazo = new() { Text = "Inducir RECHAZO del próximo(s) cobro(s)", AutoSize = true };
    private readonly CheckBox _falloNotificador = new() { Text = "Inducir FALLO del notificador (Outbox queda pendiente)", AutoSize = true };
    private readonly NumericUpDown _dias = UiKit.Numero("Días transcurridos simulados", 0m, 365m);
    private readonly TextBox _salida = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Font = Estilos.Mono, ScrollBars = ScrollBars.Vertical, AccessibleName = "Resultado de la demostración" };

    public DemostracionView(ContextoUi ui) : base(ui, "Demostración controlada", "Escenarios simulados: proveedor, fallos inducidos, Outbox y reinicio de datos")
    {
        var proveedor = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        proveedor.Controls.AddRange([_legado, _alterno]);
        var canal = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        canal.Controls.AddRange([_email, _sms]);
        var fallos = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown };
        fallos.Controls.AddRange([_rechazo, _falloNotificador, UiKit.Campo("Días simulados (plazos de devolución)", _dias)]);

        Contenido.Controls.Add(UiKit.Grupo("Resultado", _salida));
        Contenido.Controls.Add(UiKit.Fila(
            UiKit.Boton("&Aplicar configuración", async (_, _) => await IntentarAsync(AplicarAsync), primario: true),
            UiKit.Boton("&Procesar Outbox ahora", async (_, _) => await IntentarAsync(ProcesarOutboxAsync)),
            UiKit.Boton("&Restablecer base de pruebas", async (_, _) => await IntentarAsync(RestablecerAsync))));
        var opciones = UiKit.Fila(UiKit.Grupo("Proveedor de cobro", proveedor, 150), UiKit.Grupo("Canal (Factory Method)", canal, 150),
            UiKit.Grupo("Fallos inducidos", fallos, 150));
        foreach (Control grupo in opciones.Controls) { grupo.Dock = DockStyle.None; grupo.Width = 340; }
        Contenido.Controls.Add(opciones);
    }

    public override string NombreVista => NombresVista.Demostracion;

    private ConfiguracionDemostracion Config => Ui.Singleton<ConfiguracionDemostracion>();

    public override async Task RefrescarAsync()
    {
        var c = Config;
        _legado.Checked = c.ProveedorPago == ProveedoresPago.Legado;
        _alterno.Checked = !_legado.Checked;
        _email.Checked = c.CanalNotificacion == CanalesNotificacion.Email;
        _sms.Checked = !_email.Checked;
        _rechazo.Checked = c.SimularRechazoCobro;
        _falloNotificador.Checked = c.SimularFalloNotificador;
        _dias.Value = c.DesfaseDias;
        var outbox = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarOutboxAsync());
        MostrarInfo($"{c.Describir()} · Outbox pendientes: {outbox.Count(o => o.Procesado is null)}");
    }

    private async Task AplicarAsync()
    {
        await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioDemostracion>().AplicarAsync(c =>
        {
            c.UsarProveedorPago(_alterno.Checked ? ProveedoresPago.Alterno : ProveedoresPago.Legado);
            c.UsarCanalNotificacion(_sms.Checked ? CanalesNotificacion.Sms : CanalesNotificacion.Email);
            c.ConfigurarRechazoCobro(_rechazo.Checked);
            c.ConfigurarFalloNotificador(_falloNotificador.Checked);
            c.ConfigurarDesfaseDias((int)_dias.Value);
        }));
        _salida.AppendText($"{DateTime.Now:HH:mm:ss} Configuración aplicada: {Config.Describir()}\r\n");
        MostrarExito("Configuración de demostración aplicada (queda auditada).");
    }

    private async Task ProcesarOutboxAsync()
    {
        var r = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());
        _salida.AppendText($"{DateTime.Now:HH:mm:ss} Outbox: {r.Procesados} procesado(s), {r.Fallidos} pendiente(s) con error\r\n");
        foreach (var linea in r.Detalle) _salida.AppendText($"   {linea}\r\n");
        await NotificarAsync(EventoVista.OutboxProcesado);
        await RefrescarAsync();
    }

    private async Task RestablecerAsync()
    {
        if (!UiKit.Confirmar(FindForm(), "Se ELIMINARÁ la base de datos de ejecución y se recreará con migraciones + seed.\n¿Continuar?")) return;
        await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioDemostracion>().RestablecerBaseControladaAsync());
        _salida.AppendText($"{DateTime.Now:HH:mm:ss} Base de pruebas restablecida (catálogo inicial: P01=8, P02=5, P03=20, P04=4, P05=30)\r\n");
        await NotificarAsync(EventoVista.DatosRestablecidos);
        MostrarExito("Base restablecida.");
    }
}
