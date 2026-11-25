using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Eventos;
using LogiMarket.Application.Seguridad;
using LogiMarket.WinForms.Comun;
using LogiMarket.WinForms.Composicion;
using LogiMarket.WinForms.Vistas;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Formularios;

//ventana principal: cabecera de sesion, navegacion lateral y area de contenido
public sealed class MainForm : Form
{
    private readonly ContextoUi _ui;
    private readonly Panel _area;
    private readonly FlowLayoutPanel _navegacion;
    private readonly ToolStripStatusLabel _estadoMediador;
    private readonly Dictionary<string, (Button Boton, VistaBase Vista)> _vistas = new();
    private readonly System.Windows.Forms.Timer _temporizadorOutbox;
    private VistaBase? _actual;
    private bool _procesandoOutbox;
    private readonly FormularioPrincipalMediator _mediador;

    public MainForm(ContextoUi ui, ConfiguracionApp configuracion)
    {
        _ui = ui;
        var usuario = ui.Sesion.Actual!;
        Text = $"LogiMarket 360 · {usuario.NombreCompleto} ({usuario.Rol})";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 700);
        Size = new Size(1360, 820);
        Font = Estilos.Normal;
        BackColor = Estilos.Fondo;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        //cabecera
        var cabecera = new Panel { Dock = DockStyle.Top, Height = 56, BackColor = Color.White, Padding = new Padding(20, 0, 20, 0) };
        cabecera.Controls.Add(new Label { Text = "LOGIMARKET 360", Font = Estilos.Titulo, ForeColor = Estilos.Texto, AutoSize = true, Location = new Point(18, 12) });
        //sin salto de linea: si la fila se partia, el boton quedaba fuera de la cabecera de 56 px
        var sesion = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 10, 0, 0)
        };
        sesion.Controls.Add(new Label { Text = $"{usuario.Rol} · {usuario.NombreUsuario} · Sesión local", AutoSize = true, ForeColor = Estilos.TextoSuave, Margin = new Padding(0, 8, 12, 0) });
        sesion.Controls.Add(UiKit.Boton("Cerrar &sesión", async (_, _) => await CerrarSesionAsync()));
        cabecera.Controls.Add(sesion);

        //navegacion lateral
        _navegacion = new FlowLayoutPanel
        {
            Dock = DockStyle.Left, Width = 220, BackColor = Estilos.Lateral, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(10, 14, 10, 10), AccessibleName = "Navegación"
        };

        _area = new Panel { Dock = DockStyle.Fill, BackColor = Estilos.Fondo };
        var barra = new StatusStrip();
        _estadoMediador = new ToolStripStatusLabel("Listo") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        barra.Items.Add(_estadoMediador);

        Controls.Add(_area);
        Controls.Add(_navegacion);
        Controls.Add(cabecera);
        Controls.Add(barra);

        RegistrarVistas();

        _mediador = ui.Singleton<FormularioPrincipalMediator>();
        _mediador.Notificado += AlNotificarMediador;

        //procesamiento periodico del outbox (eventos post-confirmacion)
        _temporizadorOutbox = new System.Windows.Forms.Timer { Interval = Math.Max(5, configuracion.ProcesarOutboxCadaSegundos) * 1000 };
        _temporizadorOutbox.Tick += async (_, _) => await ProcesarOutboxAsync();
        _temporizadorOutbox.Start();

        Shown += async (_, _) => await MostrarAsync(NombresVista.Panel);
    }

    public bool CerroSesion { get; private set; }

    private void RegistrarVistas()
    {
        Agregar("Panel general", new PanelGeneralView(_ui));
        Agregar("Catálogo y kits", new CatalogoView(_ui));
        Agregar("Clientes", new ClientesView(_ui));
        Agregar("Carrito / borrador", new CarritoView(_ui));
        Agregar("Pedidos e historial", new PedidosView(_ui));
        Agregar("Cobro", new CobroView(_ui));
        Agregar("Despacho", new DespachoView(_ui));
        Agregar("Devoluciones", new DevolucionesView(_ui));
        Agregar("Inventario", new InventarioView(_ui));
        Agregar("Reportes", new ReportesView(_ui));
        Agregar("Auditoría y métricas", new AuditoriaView(_ui));
        if (_ui.Puede(Permiso.AdministrarDemostracion))
            Agregar("Demostración", new DemostracionView(_ui));
    }

    private void Agregar(string texto, VistaBase vista)
    {
        var numero = _vistas.Count + 1;
        var boton = new Button
        {
            Text = $"{texto}",
            Width = 196, Height = 36, TextAlign = ContentAlignment.MiddleLeft, FlatStyle = FlatStyle.Flat,
            BackColor = Estilos.Lateral, ForeColor = Estilos.Texto, Font = Estilos.Normal, Margin = new Padding(0, 2, 0, 2),
            AccessibleName = texto, TabIndex = numero
        };
        boton.FlatAppearance.BorderSize = 0;
        boton.Click += async (_, _) => await MostrarAsync(vista.NombreVista);
        _navegacion.Controls.Add(boton);
        _vistas[vista.NombreVista] = (boton, vista);
        _ui.Mediador.Registrar(vista); //el mediador conoce a los colegas los colegas no se conocen entre si
    }

    private async Task MostrarAsync(string nombre)
    {
        if (!_vistas.TryGetValue(nombre, out var entrada)) return;
        foreach (var (boton, _) in _vistas.Values)
        {
            boton.Font = Estilos.Normal;
            boton.BackColor = Estilos.Lateral;
        }
        entrada.Boton.Font = Estilos.Negrita;
        entrada.Boton.BackColor = Color.White;

        if (_actual is not null) _area.Controls.Remove(_actual);
        _actual = entrada.Vista;
        _area.Controls.Add(_actual);
        await _actual.MostrarAsync();
        _actual.SelectNextControl(_actual, true, true, true, true);
    }

    private async Task ProcesarOutboxAsync()
    {
        if (_procesandoOutbox) return;
        _procesandoOutbox = true;
        try
        {
            var resultado = await _ui.EjecutarAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());
            if (resultado.Procesados > 0 || resultado.Fallidos > 0)
                await _ui.Mediador.NotificarAsync(null, EventoVista.OutboxProcesado);
        }
        catch (Exception ex)
        {
            _estadoMediador.Text = $"Outbox: {ex.Message}";
        }
        finally
        {
            _procesandoOutbox = false;
        }
    }

    private async Task CerrarSesionAsync()
    {
        if (!UiKit.Confirmar(this, "¿Cerrar la sesión actual?")) return;
        await _ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioAutenticacion>().CerrarSesionAsync());
        CerroSesion = true;
        Close();
    }

    private void AlNotificarMediador(object? emisor, string texto) => _estadoMediador.Text = $"Mediator: {texto}";

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _mediador.Notificado -= AlNotificarMediador;
        _temporizadorOutbox.Stop();
        _temporizadorOutbox.Dispose();
        base.OnFormClosed(e);
    }
}
