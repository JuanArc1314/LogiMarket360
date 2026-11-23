using LogiMarket.Application.Comprobantes;
using LogiMarket.Application.Coordinacion;

namespace LogiMarket.WinForms.Comun;

public static class NombresVista
{
    public const string Panel = "Panel";
    public const string Catalogo = "Catalogo";
    public const string Clientes = "Clientes";
    public const string Carrito = "Carrito";
    public const string Pedidos = "Pedidos";
    public const string Cobro = "Cobro";
    public const string Despacho = "Despacho";
    public const string Devoluciones = "Devoluciones";
    public const string Inventario = "Inventario";
    public const string Reportes = "Reportes";
    public const string Auditoria = "Auditoria";
    public const string Demostracion = "Demostracion";

    public static readonly string[] Todas =
        [Panel, Catalogo, Clientes, Carrito, Pedidos, Cobro, Despacho, Devoluciones, Inventario, Reportes, Auditoria, Demostracion];
}

/// PATRÓN MEDIATOR — Mediador concreto de la ventana principal. Define qué vistas se refrescan ante
/// cada evento; ninguna pantalla llama directamente a métodos de otra.
public sealed class FormularioPrincipalMediator : MediadorVistas
{
    public FormularioPrincipalMediator() : base(new Dictionary<EventoVista, string[]>
    {
        [EventoVista.SesionIniciada] = NombresVista.Todas,
        [EventoVista.DatosRestablecidos] = NombresVista.Todas,
        [EventoVista.CatalogoModificado] = [NombresVista.Catalogo, NombresVista.Carrito, NombresVista.Inventario, NombresVista.Panel],
        [EventoVista.ClienteModificado] = [NombresVista.Clientes, NombresVista.Carrito, NombresVista.Pedidos],
        [EventoVista.BorradorGuardado] = [NombresVista.Pedidos, NombresVista.Cobro, NombresVista.Panel, NombresVista.Carrito],
        [EventoVista.PedidoConfirmado] = [NombresVista.Catalogo, NombresVista.Inventario, NombresVista.Pedidos, NombresVista.Cobro,
            NombresVista.Despacho, NombresVista.Carrito, NombresVista.Panel, NombresVista.Auditoria, NombresVista.Reportes],
        [EventoVista.PedidoCancelado] = [NombresVista.Pedidos, NombresVista.Cobro, NombresVista.Carrito, NombresVista.Panel],
        [EventoVista.DespachoRegistrado] = [NombresVista.Despacho, NombresVista.Pedidos, NombresVista.Panel, NombresVista.Auditoria],
        [EventoVista.EntregaRegistrada] = [NombresVista.Despacho, NombresVista.Pedidos, NombresVista.Devoluciones, NombresVista.Panel],
        [EventoVista.DevolucionProcesada] = [NombresVista.Devoluciones, NombresVista.Catalogo, NombresVista.Inventario,
            NombresVista.Pedidos, NombresVista.Panel, NombresVista.Auditoria, NombresVista.Reportes],
        [EventoVista.InventarioAjustado] = [NombresVista.Inventario, NombresVista.Catalogo, NombresVista.Panel],
        [EventoVista.OutboxProcesado] = [NombresVista.Panel, NombresVista.Auditoria, NombresVista.Demostracion]
    })
    { }
}

/// Visor de la salida VISTA PREVIA del Bridge: muestra el comprobante en una ventana.
public sealed class VisorDocumentosWinForms : IVisorDocumentos
{
    public void Mostrar(DocumentoRenderizado documento)
    {
        using var ventana = new Form
        {
            Text = documento.Titulo,
            Width = 560,
            Height = 600,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            ShowInTaskbar = false
        };
        var texto = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = Estilos.Mono, Text = documento.Contenido.Replace("\n", Environment.NewLine).Replace("\r\r", "\r"),
            AccessibleName = "Contenido del comprobante", BackColor = Color.White
        };
        var cerrar = new Button { Text = "&Cerrar", Dock = DockStyle.Bottom, Height = 34, DialogResult = DialogResult.OK };
        ventana.Controls.Add(texto);
        ventana.Controls.Add(cerrar);
        ventana.AcceptButton = cerrar;
        ventana.CancelButton = cerrar;
        ventana.ShowDialog(Form.ActiveForm);
    }
}
