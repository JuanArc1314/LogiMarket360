using LogiMarket.Application.Clientes;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Clientes;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

//04 - clientes: busqueda, registro, categoria y edicion
public sealed class ClientesView : VistaBase
{
    private readonly DataGridView _grid = UiKit.Grid("Clientes");
    private readonly TextBox _buscar = UiKit.Texto("Buscar cliente", 220);
    private readonly TextBox _identificacion = UiKit.Texto("Identificación", 130);
    private readonly TextBox _nombre = UiKit.Texto("Nombre", 200);
    private readonly TextBox _email = UiKit.Texto("Correo", 200);
    private readonly TextBox _telefono = UiKit.Texto("Teléfono", 120);
    private readonly ComboBox _categoria = UiKit.Combo("Categoría", 110);
    private readonly CheckBox _activo = new() { Text = "Activo", Checked = true, AutoSize = true, Margin = new Padding(0, 26, 12, 0) };
    private readonly Button _guardar;
    private readonly Button _nuevo;
    private int? _idEdicion;

    public ClientesView(ContextoUi ui) : base(ui, "Clientes", "Búsqueda, registro, categoría y edición de clientes")
    {
        _categoria.Items.AddRange(Enum.GetNames<CategoriaCliente>());
        _categoria.SelectedIndex = 0;
        _grid.Columna(nameof(ClienteDto.Identificacion), "Identificación", 70).Columna(nameof(ClienteDto.Nombre), "Nombre", 120)
            .Columna(nameof(ClienteDto.Email), "Correo", 120).Columna(nameof(ClienteDto.Telefono), "Teléfono", 70)
            .Columna(nameof(ClienteDto.Categoria), "Categoría", 50).Columna(nameof(ClienteDto.Activo), "Activo", 40);
        _grid.SelectionChanged += (_, _) => Cargar();

        _guardar = UiKit.Boton("&Guardar cliente", async (_, _) => await IntentarAsync(GuardarAsync), primario: true);
        _nuevo = UiKit.Boton("&Nuevo", (_, _) => Limpiar());

        Contenido.Controls.Add(_grid);
        Contenido.Controls.Add(UiKit.Fila(_guardar, _nuevo));
        Contenido.Controls.Add(UiKit.Fila(UiKit.Campo("Identificación", _identificacion), UiKit.Campo("Nombre", _nombre),
            UiKit.Campo("Correo", _email), UiKit.Campo("Teléfono", _telefono), UiKit.Campo("Categoría", _categoria), _activo));
        Contenido.Controls.Add(UiKit.Fila(UiKit.Etiqueta("Buscar:"), _buscar,
            UiKit.Boton("&Filtrar", async (_, _) => await IntentarAsync(RefrescarAsync))));
    }

    public override string NombreVista => NombresVista.Clientes;

    public override void AplicarPermisos()
    {
        var puede = Ui.Puede(Permiso.GestionarClientes);
        _guardar.Enabled = _nuevo.Enabled = puede;
    }

    public override async Task RefrescarAsync()
    {
        var clientes = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarClientesAsync(_buscar.Text));
        _grid.Enlazar(clientes, c => c.Id);
    }

    private void Cargar()
    {
        if (_grid.Seleccion<ClienteDto>() is not { } c) return;
        _idEdicion = c.Id;
        _identificacion.Text = c.Identificacion;
        _nombre.Text = c.Nombre;
        _email.Text = c.Email;
        _telefono.Text = c.Telefono;
        _categoria.SelectedItem = c.Categoria.ToString();
        _activo.Checked = c.Activo;
    }

    private void Limpiar()
    {
        _idEdicion = null;
        foreach (var t in new[] { _identificacion, _nombre, _email, _telefono }) t.Clear();
        _categoria.SelectedIndex = 0;
        _activo.Checked = true;
        _identificacion.Focus();
    }

    private async Task GuardarAsync()
    {
        var dto = new ClienteEdicionDto(_idEdicion, _identificacion.Text, _nombre.Text, _email.Text, _telefono.Text,
            Enum.Parse<CategoriaCliente>((string)_categoria.SelectedItem!), _activo.Checked);
        Informar(await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioClientes>().GuardarAsync(dto)));
        await NotificarAsync(EventoVista.ClienteModificado);
        await RefrescarAsync();
    }
}
