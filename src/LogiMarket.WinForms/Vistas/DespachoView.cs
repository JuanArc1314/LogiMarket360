using LogiMarket.Application.Consultas;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Pedidos;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

//08 - despacho y entrega: gestion logistica y transicion de estados autorizada (BODEGA/ADMIN)
public sealed class DespachoView : VistaBase
{
    private readonly DataGridView _pendientes = UiKit.Grid("Pedidos para despacho o entrega");
    private readonly TextBox _seguimiento = UiKit.Texto("Código de pedido o número de guía", 200);
    private readonly TextBox _detalle = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Font = Estilos.Mono, ScrollBars = ScrollBars.Vertical, AccessibleName = "Seguimiento" };
    private readonly Button _despachar, _entregar;

    public DespachoView(ContextoUi ui) : base(ui, "Despacho y entrega", "Gestión logística y transición de estados autorizada")
    {
        _pendientes.Columna(nameof(PedidoResumenDto.Codigo), "Código", 60).Columna(nameof(PedidoResumenDto.Cliente), "Cliente", 110)
            .Columna(nameof(PedidoResumenDto.Estado), "Estado", 70).Columna(nameof(PedidoResumenDto.Entrega), "Familia", 60)
            .Columna(nameof(PedidoResumenDto.Total), "Total USD", 60, "M");
        _pendientes.SelectionChanged += async (_, _) =>
        {
            if (_pendientes.Seleccion<PedidoResumenDto>() is { } p) { _seguimiento.Text = p.Codigo; await IntentarAsync(SeguimientoAsync); }
        };

        _despachar = UiKit.Boton("&Despachar (genera guía)", async (_, _) => await IntentarAsync(DespacharAsync), primario: true);
        _entregar = UiKit.Boton("Registrar &entrega", async (_, _) => await IntentarAsync(EntregarAsync));

        var cuerpo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        cuerpo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        cuerpo.Controls.Add(UiKit.Grupo("PAGADO / DESPACHADO", _pendientes), 0, 0);
        cuerpo.Controls.Add(UiKit.Grupo("Seguimiento y guía simulada", _detalle), 1, 0);

        Contenido.Controls.Add(cuerpo);
        Contenido.Controls.Add(UiKit.Fila(_despachar, _entregar, UiKit.Etiqueta("Seguimiento:"), _seguimiento,
            UiKit.Boton("&Consultar", async (_, _) => await IntentarAsync(SeguimientoAsync))));
    }

    public override string NombreVista => NombresVista.Despacho;

    public override void AplicarPermisos()
    {
        _despachar.Enabled = Ui.Puede(Permiso.DespacharPedidos);
        _entregar.Enabled = Ui.Puede(Permiso.EntregarPedidos);
    }

    public override async Task RefrescarAsync()
    {
        var pedidos = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarPedidosAsync());
        _pendientes.Enlazar(pedidos.Where(p => p.Estado is EstadoPedido.PAGADO or EstadoPedido.DESPACHADO).ToList(), p => p.Id);
    }

    private async Task DespacharAsync()
    {
        if (_pendientes.Seleccion<PedidoResumenDto>() is not { } p) return;
        if (!UiKit.Confirmar(FindForm(), $"¿Despachar {p.Codigo} ({p.Entrega})?")) return;
        var r = await Ui.EjecutarComandoAsync(f => f.Despachar(p.Id));
        Informar(r);
        if (r.Exito) await NotificarAsync(EventoVista.DespachoRegistrado);
        await RefrescarAsync();
        await SeguimientoAsync();
    }

    private async Task EntregarAsync()
    {
        if (_pendientes.Seleccion<PedidoResumenDto>() is not { } p) return;
        if (!UiKit.Confirmar(FindForm(), $"¿Registrar la entrega de {p.Codigo}?")) return;
        var r = await Ui.EjecutarComandoAsync(f => f.Entregar(p.Id));
        Informar(r);
        if (r.Exito) await NotificarAsync(EventoVista.EntregaRegistrada);
        await RefrescarAsync();
    }

    private async Task SeguimientoAsync()
    {
        if (string.IsNullOrWhiteSpace(_seguimiento.Text)) return;
        var s = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ObtenerSeguimientoAsync(_seguimiento.Text));
        _detalle.Text = s is null
            ? "No se encontró el pedido o la guía."
            : $"Pedido   : {s.Codigo}\r\nCliente  : {s.Cliente}\r\nEstado   : {s.Estado}\r\nGuía     : {s.NumeroGuia ?? "(sin despachar)"}\r\n" +
              $"Etiqueta : {s.Etiqueta ?? "-"}\r\nEstimada : {s.FechaEstimada:dd/MM/yyyy}\r\nEntregado: {s.FechaEntrega:dd/MM/yyyy HH:mm}\r\n\r\nHistorial:\r\n" +
              string.Join("\r\n", s.Historial.Select(h => $"  {h.Fecha:dd/MM HH:mm} {h.Anterior?.ToString() ?? "-",-10} → {h.Nuevo,-10} {h.Usuario}"));
    }
}
