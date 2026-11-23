using LogiMarket.Application.Consultas;
using LogiMarket.Domain.Pedidos;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

/// 02. Panel general: resumen operativo de pedidos, existencias, alertas y métricas de sesión.
public sealed class PanelGeneralView : VistaBase
{
    private readonly FlowLayoutPanel _tarjetas;
    private readonly ListBox _alertas;
    private readonly Label _metricas;

    public PanelGeneralView(ContextoUi ui) : base(ui, "Panel general", "Resumen operativo de pedidos, existencias y alertas")
    {
        _tarjetas = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 230, WrapContents = true, AutoScroll = true };
        _alertas = new ListBox { Font = Estilos.Normal, AccessibleName = "Alertas", IntegralHeight = false };
        _metricas = new Label { Font = Estilos.Mono, AutoSize = false, AccessibleName = "Métricas de sesión" };

        var inferior = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        inferior.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        inferior.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        inferior.Controls.Add(UiKit.Grupo("Alertas", _alertas), 0, 0);
        inferior.Controls.Add(UiKit.Grupo("Métricas de la sesión (Singleton compartido)", _metricas), 1, 0);

        Contenido.Controls.Add(inferior);
        Contenido.Controls.Add(UiKit.Fila(UiKit.Boton("&Actualizar", async (_, _) => await IntentarAsync(RefrescarAsync))));
        Contenido.Controls.Add(_tarjetas);
        ui.Metricas.Cambiaron += (_, _) => { if (IsHandleCreated) BeginInvoke((MethodInvoker)PintarMetricas); };
    }

    public override string NombreVista => NombresVista.Panel;

    public override async Task RefrescarAsync()
    {
        var indicadores = await Ui.EjecutarAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ObtenerIndicadoresAsync());
        _tarjetas.Controls.Clear();
        _tarjetas.Controls.Add(Tarjeta("Total cobrado", UiKit.Dinero(indicadores.TotalCobrado)));
        foreach (var estado in Enum.GetValues<EstadoPedido>())
            _tarjetas.Controls.Add(Tarjeta($"Pedidos {estado}", indicadores.PedidosPorEstado[estado].ToString()));
        _tarjetas.Controls.Add(Tarjeta("SKU con stock bajo", indicadores.ProductosConStockBajo.ToString()));
        _tarjetas.Controls.Add(Tarjeta("Outbox pendiente", indicadores.EventosPendientes.ToString()));
        _tarjetas.Controls.Add(Tarjeta("Clientes", indicadores.Clientes.ToString()));

        _alertas.Items.Clear();
        foreach (var alerta in indicadores.Alertas) _alertas.Items.Add(alerta);
        if (_alertas.Items.Count == 0) _alertas.Items.Add("Sin alertas.");
        PintarMetricas();
        MostrarInfo($"Actualizado {DateTime.Now:HH:mm:ss}");
    }

    private void PintarMetricas()
    {
        var m = Ui.Metricas.Instantanea();
        _metricas.Text =
            $"Instancia      : {m.InstanciaId.ToString()[..8]}\n" +
            $"Inicio sesión  : {m.Inicio:dd/MM HH:mm}\n" +
            $"Pedidos pagados: {m.PedidosPagados}  ({UiKit.Dinero(m.MontoCobrado)})\n" +
            $"Cobros rechaz. : {m.CobrosRechazados}\n" +
            $"Despachos      : {m.Despachos}\n" +
            $"Devoluciones   : {m.Devoluciones}  ({UiKit.Dinero(m.MontoReembolsado)})\n" +
            $"Comandos       : {m.ComandosEjecutados} (fallidos {m.ComandosFallidos})";
    }

    private static Control Tarjeta(string titulo, string valor)
    {
        var panel = new Panel { Width = 190, Height = 92, BackColor = Color.White, Margin = new Padding(0, 0, 12, 12), BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(new Label { Text = valor, Font = Estilos.Kpi, ForeColor = Estilos.Texto, AutoSize = true, Location = new Point(10, 36) });
        panel.Controls.Add(new Label { Text = titulo, ForeColor = Estilos.TextoSuave, AutoSize = true, Location = new Point(10, 10) });
        panel.AccessibleName = $"{titulo}: {valor}";
        return panel;
    }
}
