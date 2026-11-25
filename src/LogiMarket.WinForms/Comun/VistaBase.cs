using LogiMarket.Application.Coordinacion;

namespace LogiMarket.WinForms.Comun;

//vista base de cada pantalla
//es colega del mediator: tras una operacion notifica al mediador (nunca llama a otra vista) y
//el mediador decide que vistas se refrescan
public abstract class VistaBase : UserControl, IVistaColaboradora
{
    private readonly Label _estado;
    private readonly ErrorProvider _errores = new() { BlinkStyle = ErrorBlinkStyle.NeverBlink };
    private readonly HashSet<Control> _camposMarcados = new();
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

    //recarga datos desde application (lo invoca el mediador o la navegacion)
    public abstract Task RefrescarAsync();

    //habilita/deshabilita controles segun el rol (solo ux
    //la regla real esta en application)
    public virtual void AplicarPermisos() { }

    public async Task MostrarAsync()
    {
        AplicarPermisos();
        if (_cargada) return;
        _cargada = true;
        await IntentarAsync(RefrescarAsync);
    }

    protected Task NotificarAsync(EventoVista evento) => Ui.Mediador.NotificarAsync(this, evento);

    //refresca la vista sin borrar el mensaje de la operacion que se acaba de ejecutar
    //(el refresco escribe su propio resumen en la barra de estado)
    protected async Task RefrescarConservandoMensajeAsync()
    {
        var (texto, color) = (_estado.Text, _estado.ForeColor);
        await RefrescarAsync();
        _estado.Text = texto;
        _estado.ForeColor = color;
    }

    protected void MostrarExito(string mensaje) { _estado.ForeColor = Estilos.Exito; _estado.Text = "✔ " + mensaje; }
    protected void MostrarError(string mensaje) { _estado.ForeColor = Estilos.Error; _estado.Text = "✖ " + mensaje.Replace('\n', ' '); }
    protected void MostrarInfo(string mensaje) { _estado.ForeColor = Estilos.TextoSuave; _estado.Text = mensaje; }

    //validacion cercana al campo: el icono de error aparece junto al control invalido
    //y el primer mensaje tambien se muestra en la barra de estado
    protected bool ValidarCampos(params (Control Campo, bool Valido, string Mensaje)[] reglas)
    {
        LimpiarErrores();
        var fallidas = reglas.Where(r => !r.Valido).ToList();
        foreach (var (campo, _, mensaje) in fallidas) Marcar(campo, mensaje);
        if (fallidas.Count == 0) return true;
        MostrarError(fallidas[0].Mensaje);
        fallidas[0].Campo.Focus();
        return false;
    }

    //marca un campo con un error que viene de application o del dominio (sku duplicado, correo invalido...)
    protected void MarcarCampo(Control campo, string mensaje)
    {
        Marcar(campo, mensaje);
        MostrarError(mensaje);
        campo.Focus();
    }

    protected void LimpiarErrores()
    {
        foreach (var campo in _camposMarcados) _errores.SetError(campo, string.Empty);
    }

    private void Marcar(Control campo, string mensaje)
    {
        _errores.SetError(campo, mensaje);
        //el error se quita en cuanto el usuario corrige el campo
        if (_camposMarcados.Add(campo)) campo.TextChanged += (_, _) => _errores.SetError(campo, string.Empty);
    }

    //ejecuta una accion de la UI mostrando errores de negocio de forma clara
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

    //muestra el resultado de una operacion/comando en la barra de estado
    protected void Informar(Application.Comun.ResultadoOperacion resultado)
    {
        if (resultado.Exito) MostrarExito(resultado.Mensaje);
        else MostrarError($"{resultado.Mensaje} ({resultado.Codigo})");
    }
}
