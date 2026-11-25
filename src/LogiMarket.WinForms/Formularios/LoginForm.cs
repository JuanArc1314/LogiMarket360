using LogiMarket.Application.Seguridad;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Formularios;

//01 - acceso: identificacion local, credenciales (verificadas con hash) y perfil
public sealed class LoginForm : Form
{
    private readonly ContextoUi _ui;
    private readonly TextBox _usuario;
    private readonly TextBox _clave;
    private readonly Label _mensaje;
    private readonly Button _ingresar;

    public LoginForm(ContextoUi ui)
    {
        _ui = ui;
        Text = "LogiMarket 360 · Acceso";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(440, 420);
        BackColor = Color.White;
        Font = Estilos.Normal;
        AutoScaleMode = AutoScaleMode.Dpi;

        _usuario = UiKit.Texto("Usuario", 360);
        _clave = UiKit.Texto("Contraseña", 360);
        _clave.UseSystemPasswordChar = true;
        _mensaje = new Label { AutoSize = false, Width = 380, Height = 40, ForeColor = Estilos.Error, AccessibleRole = AccessibleRole.Alert };
        _ingresar = UiKit.Boton("&Ingresar", async (_, _) => await IngresarAsync(), primario: true, ancho: 360);

        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(36, 24, 24, 12), WrapContents = false };
        panel.Controls.Add(new Label { Text = "LOGIMARKET 360", Font = Estilos.Titulo, AutoSize = true, ForeColor = Estilos.Texto });
        panel.Controls.Add(new Label { Text = "Identificación local y perfil de acceso", Font = Estilos.Subtitulo, ForeColor = Estilos.TextoSuave, AutoSize = true, Margin = new Padding(0, 0, 0, 16) });
        panel.Controls.Add(new Label { Text = "&Usuario", AutoSize = true, Font = Estilos.Negrita });
        panel.Controls.Add(_usuario);
        panel.Controls.Add(new Label { Text = "&Contraseña", AutoSize = true, Font = Estilos.Negrita });
        panel.Controls.Add(_clave);
        panel.Controls.Add(_ingresar);
        panel.Controls.Add(_mensaje);
        panel.Controls.Add(new Label
        {
            AutoSize = true, ForeColor = Estilos.TextoSuave,
            Text = "Cuentas de demostración (ficticias):\nadmin / Admin123*   ·   ventas / Ventas123*\nbodega / Bodega123*   ·   consulta / Consulta123*"
        });
        Controls.Add(panel);
        AcceptButton = _ingresar;
    }

    private async Task IngresarAsync()
    {
        _ingresar.Enabled = false;
        _mensaje.Text = "";
        try
        {
            var resultado = await _ui.EjecutarAsync(sp =>
                sp.GetRequiredService<ServicioAutenticacion>().IniciarSesionAsync(_usuario.Text, _clave.Text));
            if (resultado.Exito)
            {
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            _mensaje.Text = resultado.Mensaje;
            _clave.SelectAll();
            _clave.Focus();
        }
        catch (Exception ex)
        {
            _mensaje.Text = UiKit.Describir(ex);
        }
        finally
        {
            _ingresar.Enabled = true;
        }
    }
}
