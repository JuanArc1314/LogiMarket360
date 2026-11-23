using LogiMarket.Application.Coordinacion;

namespace LogiMarket.WinForms.Comun;

/// Vista base de cada pantalla. Es COLEGA del Mediator: tras una operación notifica al mediador
/// (nunca llama a otra vista) y el mediador decide qué vistas se refrescan.
public abstract class VistaBase : UserControl, IVistaColaboradora
{
    private readonly Label _estado;
    private bool _cargada;

    protected VistaBase(ContextoUi ui, string titulo, string subtitulo)
    {
        Ui = ui;
        Dock = DockStyle.Fill;
        BackColor = Estilos.Fondo;
        Font = Estilos.Normal;
        Padding = new Padding(20, 12, 20, 8);

        Contenido = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        _estado = new Label
        {
            Dock = DockStyle.Bottom, Height = 26, Font = Estilos.Negrita, TextAlign = ContentAlignment.MiddleLeft,
            AccessibleName = "Mensaje de estado", AccessibleRole = AccessibleRole.StatusBar
        };
        var cabecera = new Panel { Dock = DockStyle.Top, Height = 62 };
        cabecera.Controls.Add(new Label { Text = subtitulo, Font = Estilos.Subtitulo, ForeColor = Estilos.TextoSuave, AutoSize = true, Location = new Point(0, 36) });
        cabecera.Controls.Add(new Label { Text = titulo, Font = Estilos.Titulo, ForeColor = Estilos.Texto, AutoSize = true, Location = new Point(0, 2) });

        Controls.Add(Contenido);
        Controls.Add(_estado);
        Controls.Add(cabecera);
    }

    protected ContextoUi Ui { get; }
    protected Panel Contenido { get; }

    public abstract string NombreVista { get; }

    /// Recarga datos desde Application (lo invoca el mediador o la navegación).
    public abstract Task RefrescarAsync();

    /// Habilita/deshabilita controles según el rol (solo UX; la regla real está en Application).
    public virtual void AplicarPermisos() { }

    public async Task MostrarAsync()
    {
        AplicarPermisos();
        if (_cargada) return;
        _cargada = true;
        await IntentarAsync(RefrescarAsync);
    }

    protected Task NotificarAsync(EventoVista evento) => Ui.Mediador.NotificarAsync(this, evento);

    protected void MostrarExito(string mensaje) { _estado.ForeColor = Estilos.Exito; _estado.Text = "✔ " + mensaje; }
    protected void MostrarError(string mensaje) { _estado.ForeColor = Estilos.Error; _estado.Text = "✖ " + mensaje.Replace('\n', ' '); }
    protected void MostrarInfo(string mensaje) { _estado.ForeColor = Estilos.TextoSuave; _estado.Text = mensaje; }

    /// Ejecuta una acción de la UI mostrando errores de negocio de forma clara.
    protected async Task IntentarAsync(Func<Task> accion)
    {
        UseWaitCursor = true;
        try
        {
            await accion();
        }
        catch (Exception ex)
        {
            MostrarError(UiKit.Describir(ex));
            if (ex is not (Domain.Comun.ExcepcionDominio or Application.Comun.ExcepcionAplicacion))
                MessageBox.Show(this, UiKit.Describir(ex), "LogiMarket 360", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// Muestra el resultado de una operación/comando en la barra de estado.
    protected void Informar(Application.Comun.ResultadoOperacion resultado)
    {
        if (resultado.Exito) MostrarExito(resultado.Mensaje);
        else MostrarError($"{resultado.Mensaje} ({resultado.Codigo})");
    }
}
