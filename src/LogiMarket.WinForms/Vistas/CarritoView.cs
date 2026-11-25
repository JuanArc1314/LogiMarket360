using LogiMarket.Application.Catalogo;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Pedidos;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

//05 - carrito y borrador: seleccion de lineas (productos y kits)
//totales verificables y deshacer/rehacer (memento)
//nada se pierde sin confirmacion
public sealed class CarritoView : VistaBase
{
    private sealed record LineaVista(string Sku, string Descripcion, string Tipo, int Cantidad, decimal PrecioUnitario,
        decimal Subtotal, string Composicion);

    private readonly ComboBox _cliente = UiKit.Combo("Cliente", 210);
    private readonly ComboBox _entrega = UiKit.Combo("Forma de entrega", 110);
    private readonly ComboBox _elemento = UiKit.Combo("Producto o kit", 300);
    private readonly NumericUpDown _cantidad = UiKit.Numero("Cantidad", 1m, 999m);
    private readonly ComboBox _borradores = UiKit.Combo("Borradores guardados", 200);
    private readonly DataGridView _lineas = UiKit.Grid("Líneas del borrador");
    private readonly Label _totales = new() { Font = Estilos.Mono, AutoSize = false, Dock = DockStyle.Fill, AccessibleName = "Totales" };
    private readonly Label _cabecera = new() { Font = Estilos.Negrita, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
    private readonly Button _deshacer, _rehacer;
    private readonly List<Control> _edicion = new();
    private EditorBorrador? _editor;
    private bool _sinGuardar;
    private bool _cargando;

    public CarritoView(ContextoUi ui) : base(ui, "Carrito / borrador", "Selección de líneas, totales y operaciones Deshacer/Rehacer")
    {
        _entrega.Items.AddRange(Enum.GetNames<TipoEntrega>());
        _entrega.SelectedIndex = 0;
        _entrega.SelectedIndexChanged += async (_, _) => await IntentarAsync(() => Editar(e => e.CambiarEntrega(EntregaSeleccionada)));
        _cliente.DisplayMember = nameof(ClienteDto.Nombre);
        _cliente.SelectedIndexChanged += async (_, _) => await IntentarAsync(CambiarClienteAsync);
        _elemento.Format += (_, e) => e.Value = e.ListItem is ElementoVendibleDto v
            ? $"{v.Sku} · {v.Nombre} · {UiKit.Dinero(v.Precio)}{(v.EsKit ? " (kit)" : $" · stock {v.Stock}")}" : e.Value;
        _borradores.Format += (_, e) => e.Value = e.ListItem is PedidoResumenDto p ? $"{p.Codigo} · {p.Cliente}" : e.Value;

        _lineas.Columna(nameof(LineaVista.Sku), "SKU", 70).Columna(nameof(LineaVista.Descripcion), "Descripción", 120)
            .Columna(nameof(LineaVista.Tipo), "Tipo", 45).Columna(nameof(LineaVista.Cantidad), "Cant.", 40)
            .Columna(nameof(LineaVista.PrecioUnitario), "P. unit.", 60, "M").Columna(nameof(LineaVista.Subtotal), "Subtotal", 60, "M")
            .Columna(nameof(LineaVista.Composicion), "Composición (instantánea)", 130);

        var agregar = UiKit.Boton("&Agregar", async (_, _) => await IntentarAsync(AgregarAsync), primario: true);
        var cambiar = UiKit.Boton("Cambiar &cantidad", async (_, _) => await IntentarAsync(() => Editar(e => e.CambiarCantidad(SkuSeleccionado(), (int)_cantidad.Value))));
        var quitar = UiKit.Boton("&Quitar línea", async (_, _) => await IntentarAsync(QuitarAsync));
        _deshacer = UiKit.Boton("&Deshacer", async (_, _) => await IntentarAsync(() => Editar(e => e.Deshacer())));
        _rehacer = UiKit.Boton("&Rehacer", async (_, _) => await IntentarAsync(() => Editar(e => e.Rehacer())));
        var guardar = UiKit.Boton("&Guardar borrador", async (_, _) => await IntentarAsync(GuardarAsync), primario: true);
        var validar = UiKit.Boton("&Validar (cadena)", async (_, _) => await IntentarAsync(ValidarAsync));
        var nuevo = UiKit.Boton("&Nuevo borrador", async (_, _) => await IntentarAsync(NuevoAsync));
        var abrir = UiKit.Boton("Abrir", async (_, _) => await IntentarAsync(AbrirAsync));
        var plantilla = UiKit.Boton("Usar como plantilla (clonar)", async (_, _) => await IntentarAsync(ClonarAsync));
        _edicion.AddRange([agregar, cambiar, quitar, _deshacer, _rehacer, guardar, validar, nuevo, abrir, plantilla, _cliente, _entrega]);

        var resumen = new TableLayoutPanel { Dock = DockStyle.Right, Width = 330, RowCount = 1 };
        resumen.Controls.Add(UiKit.Grupo("Resumen verificable", _totales));

        var centro = new Panel { Dock = DockStyle.Fill };
        centro.Controls.Add(_lineas);
        centro.Controls.Add(resumen);

        Contenido.Controls.Add(centro);
        Contenido.Controls.Add(UiKit.Fila(cambiar, quitar, _deshacer, _rehacer, guardar, validar));
        Contenido.Controls.Add(UiKit.Fila(UiKit.Campo("Producto o kit", _elemento), UiKit.Campo("Cantidad", _cantidad), agregar));
        Contenido.Controls.Add(UiKit.Fila(UiKit.Campo("Cliente", _cliente), UiKit.Campo("Entrega", _entrega), nuevo,
            UiKit.Campo("Borradores", _borradores), abrir, plantilla, _cabecera));
    }

    public override string NombreVista => NombresVista.Carrito;

    private TipoEntrega EntregaSeleccionada => Enum.Parse<TipoEntrega>((string)_entrega.SelectedItem!);

    public override void AplicarPermisos()
    {
        var puede = Ui.Puede(Permiso.GestionarPedidos);
        foreach (var c in _edicion) c.Enabled = puede;
        if (puede) PintarBotonesHistorial();
    }

    public override async Task RefrescarAsync()
    {
        _cargando = true;
        try
        {
            var (clientes, vendibles, pedidos) = await Ui.EjecutarAsync(async sp =>
            {
                var consultas = sp.GetRequiredService<IConsultasOperativas>();
                var elementos = Ui.Puede(Permiso.ConsultarInformacion)
                    ? await sp.GetRequiredService<ServicioCatalogo>().ListarVendiblesAsync()
                    : [];
                return (await consultas.ListarClientesAsync(), elementos, await consultas.ListarPedidosAsync());
            });

            var clienteActual = (_cliente.SelectedItem as ClienteDto)?.Id;
            _cliente.Items.Clear();
            _cliente.Items.AddRange(clientes.Where(c => c.Activo).Cast<object>().ToArray());
            _cliente.SelectedItem = _cliente.Items.Cast<ClienteDto>().FirstOrDefault(c => c.Id == clienteActual);

            var elementoActual = (_elemento.SelectedItem as ElementoVendibleDto)?.Sku;
            _elemento.Items.Clear();
            _elemento.Items.AddRange(vendibles.Cast<object>().ToArray());
            _elemento.SelectedItem = _elemento.Items.Cast<ElementoVendibleDto>().FirstOrDefault(e => e.Sku == elementoActual);
            if (_elemento.SelectedIndex < 0 && _elemento.Items.Count > 0) _elemento.SelectedIndex = 0;

            _borradores.Items.Clear();
            _borradores.Items.AddRange(pedidos.Where(p => p.Estado == EstadoPedido.BORRADOR).Cast<object>().ToArray());

            //si el borrador en edicion ya fue confirmado en otra pantalla, se bloquea su historial
            if (_editor?.PedidoId is { } id && pedidos.FirstOrDefault(p => p.Id == id) is { Estado: not EstadoPedido.BORRADOR } confirmado)
            {
                _editor.BloquearHistorial();
                _sinGuardar = false;
                MostrarInfo($"{confirmado.Codigo} ya está {confirmado.Estado}: el historial de edición quedó bloqueado.");
            }
        }
        finally
        {
            _cargando = false;
        }
        Pintar();
    }

    private async Task CambiarClienteAsync()
    {
        if (_cargando || _cliente.SelectedItem is not ClienteDto c) return;
        if (_editor is null || _editor.Lineas.Count == 0)
        {
            _editor = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().AbrirEditorAsync(c.Id));
            _editor.CambiarEntrega(EntregaSeleccionada);
            Pintar();
            return;
        }
        if (_editor.Cliente.Id == c.Id) return;
        var cliente = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().ObtenerClienteAsync(c.Id));
        await Editar(e => e.CambiarCliente(cliente));
    }

    private async Task AgregarAsync()
    {
        if (!ValidarCampos(
                (_cliente, _editor is not null, "Seleccione primero el cliente."),
                (_elemento, _elemento.SelectedItem is ElementoVendibleDto, "Seleccione un producto o kit.")))
            return;
        var v = (ElementoVendibleDto)_elemento.SelectedItem!;
        var elemento = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().ObtenerElementoAsync(v.Sku));
        await Editar(e => e.Agregar(elemento, (int)_cantidad.Value));
    }

    private async Task QuitarAsync()
    {
        var sku = SkuSeleccionado();
        if (!UiKit.Confirmar(FindForm(), $"¿Quitar la línea {sku}? (podrá deshacerlo)")) return;
        await Editar(e => e.Quitar(sku));
    }

    private Task Editar(Action<EditorBorrador> cambio)
    {
        if (_cargando || _editor is null) return Task.CompletedTask;
        cambio(_editor);
        _sinGuardar = true;
        Pintar();
        return Task.CompletedTask;
    }

    private async Task GuardarAsync()
    {
        if (_editor is null) return;
        var guardado = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().GuardarBorradorAsync(_editor.ObtenerContenido()));
        _editor.MarcarGuardado(guardado.Id);
        _sinGuardar = false;
        MostrarExito($"Borrador {guardado.Codigo} guardado · total {UiKit.Dinero(guardado.Valoracion.Total)}.");
        await NotificarAsync(EventoVista.BorradorGuardado);
        Pintar();
    }

    private async Task ValidarAsync()
    {
        if (_editor?.PedidoId is not { } id || _sinGuardar)
        {
            MostrarError("Guarde el borrador antes de validarlo.");
            return;
        }
        var r = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().ValidarAsync(id));
        if (r.EsValido)
        {
            _editor.BloquearHistorial();
            MostrarExito($"Pedido VALIDADO ({string.Join(" → ", r.EslabonesEvaluados)}). Continúe en la pantalla Cobro.");
            await NotificarAsync(EventoVista.BorradorGuardado);
        }
        else MostrarError($"Rechazado por {r.Rechazo}");
        Pintar();
    }

    private async Task NuevoAsync()
    {
        if (_sinGuardar && !UiKit.Confirmar(FindForm(), "Hay cambios sin guardar. ¿Descartarlos y empezar un borrador nuevo?")) return;
        _editor = null;
        _sinGuardar = false;
        await CambiarClienteAsync();
        MostrarInfo("Nuevo borrador.");
    }

    private async Task AbrirAsync()
    {
        if (_borradores.SelectedItem is not PedidoResumenDto p) return;
        if (_sinGuardar && !UiKit.Confirmar(FindForm(), "Hay cambios sin guardar. ¿Descartarlos?")) return;
        _editor = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().AbrirEditorAsync(p.ClienteId, p.Id));
        _sinGuardar = false;
        _cargando = true;
        _cliente.SelectedItem = _cliente.Items.Cast<ClienteDto>().FirstOrDefault(c => c.Id == p.ClienteId);
        _entrega.SelectedItem = _editor.Entrega.ToString();
        _cargando = false;
        MostrarInfo($"Editando {p.Codigo}.");
        Pintar();
    }

    private async Task ClonarAsync()
    {
        var origen = _borradores.SelectedItem as PedidoResumenDto;
        var id = origen?.Id ?? _editor?.PedidoId;
        if (id is null)
        {
            MostrarError("Elija un borrador guardado para usarlo como plantilla.");
            return;
        }
        var copia = await Ui.EjecutarAsync(sp => sp.GetRequiredService<ServicioPedidos>().ClonarComoBorradorAsync(id.Value));
        MostrarExito($"Plantilla clonada como {copia.Codigo} (copia profunda, nuevo BORRADOR sin pago).");
        await NotificarAsync(EventoVista.BorradorGuardado);
        await RefrescarAsync();
    }

    private string SkuSeleccionado() =>
        _lineas.Seleccion<LineaVista>()?.Sku ?? throw new Domain.Comun.ExcepcionDominio("SIN_SELECCION", "Seleccione una línea.");

    private void PintarBotonesHistorial()
    {
        _deshacer.Enabled = _editor?.PuedeDeshacer ?? false;
        _rehacer.Enabled = _editor?.PuedeRehacer ?? false;
    }

    private void Pintar()
    {
        var lineas = _editor?.Lineas.Select(l => new LineaVista(l.Sku, l.Descripcion, l.EsKit ? "Kit" : "Producto", l.Cantidad,
            l.PrecioUnitario, l.Subtotal, string.Join(", ", l.Componentes.Select(c => $"{c.Sku}×{c.CantidadPorUnidad}")))).ToList() ?? [];
        _lineas.Enlazar(lineas, l => l.Sku);

        var v = _editor?.Valoracion;
        _cabecera.Text = _editor is null ? "Sin borrador" :
            $"{(_editor.PedidoId is { } id ? $"PED-{id:0000}" : "Nuevo")}{(_sinGuardar ? " · cambios sin guardar" : "")}";
        _totales.Text = v is null
            ? "Agregue productos o kits."
            : $"Unidades físicas : {_editor!.Pedido!.UnidadesFisicas}\n" +
              $"Subtotal         : {UiKit.Dinero(v.Subtotal),14}\n" +
              $"Política         : {v.Politica} ({v.TasaDescuento:P0})\n" +
              $"Descuento        : {UiKit.Dinero(-v.Descuento),14}\n" +
              $"Envío {v.Familia,-9}  : {UiKit.Dinero(v.Tarifa),14}\n" +
              $"Plazo estimado   : {v.PlazoDias} día(s)\n" +
              $"TOTAL            : {UiKit.Dinero(v.Total),14}\n\n" +
              $"Historial: {_editor.Historial.EstadosGuardados}/{HistorialEdiciones.Capacidad}" +
              (_editor.Historial.Bloqueado ? " (bloqueado)" : "");
        if (Ui.Puede(Permiso.GestionarPedidos)) PintarBotonesHistorial();
    }
}
