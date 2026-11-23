using LogiMarket.Application.Comun;
using LogiMarket.Application.Consultas;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Vistas;

/// 12. Auditoría y métricas: bitácora persistente, LOG de pagos/seguridad, Outbox, avisos y comandos.
public sealed class AuditoriaView : VistaBase
{
    private readonly ComboBox _categoria = UiKit.Combo("Categoría de auditoría", 130);
    private readonly DataGridView _auditoria = UiKit.Grid("Auditoría persistente");
    private readonly DataGridView _bitacora = UiKit.Grid("Bitácora operativa (LOG)");
    private readonly DataGridView _outbox = UiKit.Grid("Eventos Outbox");
    private readonly DataGridView _avisos = UiKit.Grid("Notificaciones emitidas");
    private readonly DataGridView _comandos = UiKit.Grid("Historial de comandos");
    private readonly Label _metricas = new() { Font = Estilos.Mono, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };

    public AuditoriaView(ContextoUi ui) : base(ui, "Auditoría y métricas", "Consulta de eventos, LOG de cobros, Outbox y comandos")
    {
        _categoria.Items.AddRange(["TODAS", "NEGOCIO", "SEGURIDAD", "SISTEMA"]);
        _categoria.SelectedIndex = 0;
        _categoria.SelectedIndexChanged += async (_, _) => await IntentarAsync(RefrescarAsync);

        _auditoria.Columna(nameof(AuditoriaDto.Fecha), "Fecha", 70, "dd/MM HH:mm:ss").Columna(nameof(AuditoriaDto.Usuario), "Usuario", 45)
            .Columna(nameof(AuditoriaDto.Categoria), "Cat.", 45).Columna(nameof(AuditoriaDto.Accion), "Acción", 80)
            .Columna(nameof(AuditoriaDto.Detalle), "Detalle", 200);
        _bitacora.Columna(nameof(EntradaBitacora.Fecha), "Hora", 55, "HH:mm:ss.fff").Columna(nameof(EntradaBitacora.Categoria), "Cat.", 45)
            .Columna(nameof(EntradaBitacora.Etapa), "Etapa", 55).Columna(nameof(EntradaBitacora.Correlacion), "Correlación", 60)
            .Columna(nameof(EntradaBitacora.Mensaje), "Mensaje", 220).Columna(nameof(EntradaBitacora.Usuario), "Usuario", 45);
        _outbox.Columna(nameof(OutboxDto.Tipo), "Evento", 70).Columna(nameof(OutboxDto.Referencia), "Pedido", 45)
            .Columna(nameof(OutboxDto.Creado), "Creado", 60, "dd/MM HH:mm:ss").Columna(nameof(OutboxDto.Procesado), "Procesado", 60, "dd/MM HH:mm:ss")
            .Columna(nameof(OutboxDto.Intentos), "Int.", 25).Columna(nameof(OutboxDto.UltimoError), "Último error", 120);
        _avisos.Columna(nameof(NotificacionDto.Fecha), "Fecha", 60, "dd/MM HH:mm").Columna(nameof(NotificacionDto.Canal), "Canal", 35)
            .Columna(nameof(NotificacionDto.Destino), "Destino", 90).Columna(nameof(NotificacionDto.Asunto), "Asunto", 140);
        _comandos.Columna("Fecha", "Hora", 50, "HH:mm:ss").Columna("Usuario", "Usuario", 45).Columna("Comando", "Comando", 70)
            .Columna("Entrada", "Entrada", 110).Columna("Resultado", "Resultado", 160);

        var pestanas = new TabControl { Dock = DockStyle.Fill, Font = Estilos.Normal };
        foreach (var (titulo, grid) in new (string, Control)[] { ("Auditoría (BD)", _auditoria), ("LOG pagos / seguridad / outbox", _bitacora),
                     ("Outbox", _outbox), ("Notificaciones", _avisos), ("Comandos", _comandos) })
        {
            var pagina = new TabPage(titulo) { Padding = new Padding(6) };
            pagina.Controls.Add(grid);
            pestanas.TabPages.Add(pagina);
        }

        Contenido.Controls.Add(pestanas);
        Contenido.Controls.Add(UiKit.Fila(UiKit.Campo("Categoría", _categoria),
            UiKit.Boton("&Actualizar", async (_, _) => await IntentarAsync(RefrescarAsync)), _metricas));
    }

    public override string NombreVista => NombresVista.Auditoria;

    public override async Task RefrescarAsync()
    {
        var categoria = _categoria.SelectedItem as string is "TODAS" or null ? null : (string)_categoria.SelectedItem!;
        var (auditoria, outbox, avisos) = await Ui.EjecutarAsync(async sp =>
        {
            var c = sp.GetRequiredService<IConsultasOperativas>();
            return (await c.ListarAuditoriaAsync(categoria), await c.ListarOutboxAsync(), await c.ListarNotificacionesAsync());
        });
        _auditoria.Enlazar(auditoria);
        _outbox.Enlazar(outbox);
        _avisos.Enlazar(avisos);
        _bitacora.Enlazar(Ui.Singleton<IBitacoraOperativa>().Consultar().Reverse().ToList());
        _comandos.Enlazar(Ui.Invocador.Historial.Reverse().ToList());

        var m = Ui.Metricas.Instantanea();
        _metricas.Text = $"Métricas (instancia {m.InstanciaId.ToString()[..8]}): pagados {m.PedidosPagados} · rechazados {m.CobrosRechazados} · " +
                         $"despachos {m.Despachos} · devoluciones {m.Devoluciones} · comandos {m.ComandosEjecutados}";
    }
}
