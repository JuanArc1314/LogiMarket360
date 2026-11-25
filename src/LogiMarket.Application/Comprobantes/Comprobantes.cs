using System.Globalization;
using System.Text;
using LogiMarket.Application.Comun;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Application.Comprobantes;

public sealed record DocumentoRenderizado(string Titulo, string NombreArchivo, string Contenido);

//patron bridge - implementador: medio de salida del documento
public interface ISalidaDocumento
{
    string Medio { get; }

    //transmite realmente el contenido
    //devuelve la ubicacion o descripcion de la salida
    Task<string> TransmitirAsync(DocumentoRenderizado documento, CancellationToken ct = default);
}

//puerto del visor de documentos (la ventana de vista previa vive en WinForms)
public interface IVisorDocumentos
{
    void Mostrar(DocumentoRenderizado documento);
}

//visor por defecto en memoria (pruebas y ejecucion sin interfaz)
public sealed class VisorDocumentosMemoria : IVisorDocumentos
{
    public DocumentoRenderizado? Ultimo { get; private set; }
    public void Mostrar(DocumentoRenderizado documento) => Ultimo = documento;
}

public static class MediosSalida
{
    public const string VistaPrevia = "VISTA_PREVIA";
    public const string ArchivoTxt = "ARCHIVO_TXT";
}

//implementador concreto: vista previa en pantalla
public sealed class SalidaVistaPrevia : ISalidaDocumento
{
    private readonly IVisorDocumentos _visor;

    public SalidaVistaPrevia(IVisorDocumentos visor) => _visor = visor;

    public string Medio => MediosSalida.VistaPrevia;

    public Task<string> TransmitirAsync(DocumentoRenderizado documento, CancellationToken ct = default)
    {
        _visor.Mostrar(documento);
        return Task.FromResult($"Vista previa mostrada: {documento.Titulo}");
    }
}

//patron bridge - abstraccion
//un comprobante sabe que contenido produce y delega el como se emite en ISalidaDocumento
//2 comprobantes x 2 salidas = 4 combinaciones sin 4 clases
public abstract class Comprobante
{
    protected static readonly CultureInfo Cultura = CultureInfo.InvariantCulture;

    protected Comprobante(ISalidaDocumento salida) => Salida = salida;

    public ISalidaDocumento Salida { get; }

    public Task<string> EmitirAsync(CancellationToken ct = default) => Salida.TransmitirAsync(Componer(), ct);

    public abstract DocumentoRenderizado Componer();

    protected static void Encabezado(StringBuilder sb, string titulo, PedidoDetalleDto p)
    {
        sb.AppendLine("==============================================");
        sb.AppendLine("            LOGIMARKET 360");
        sb.AppendLine($"   {titulo}");
        sb.AppendLine("   Comprobante interno de demostración");
        sb.AppendLine("   (no es factura tributaria)");
        sb.AppendLine("==============================================");
        sb.AppendLine($"Pedido : {p.Codigo}");
        sb.AppendLine($"Cliente: {p.Cliente} ({p.CategoriaCliente})");
        sb.AppendLine($"Estado : {p.Estado}");
        sb.AppendLine("----------------------------------------------");
        foreach (var l in p.Lineas)
        {
            sb.AppendLine($"{l.Sku,-16} {l.Cantidad,3} x {l.PrecioUnitario.ToString("0.00", Cultura),8} = {l.Subtotal.ToString("0.00", Cultura),9}");
            if (l.EsKit)
                foreach (var c in l.Componentes)
                    sb.AppendLine($"   · {c.Sku} x{c.CantidadPorUnidad} {c.Nombre}");
        }
        sb.AppendLine("----------------------------------------------");
    }
}

//abstraccion refinada: comprobante de venta
public sealed class ComprobanteVenta : Comprobante
{
    private readonly PedidoDetalleDto _pedido;

    public ComprobanteVenta(PedidoDetalleDto pedido, ISalidaDocumento salida) : base(salida) => _pedido = pedido;

    public override DocumentoRenderizado Componer()
    {
        var p = _pedido;
        var sb = new StringBuilder();
        Encabezado(sb, "COMPROBANTE DE VENTA", p);
        sb.AppendLine($"Subtotal            {Dinero.Formatear(p.Subtotal),20}");
        sb.AppendLine($"Descuento ({p.Politica,-9}) {Dinero.Formatear(-p.Descuento),17}");
        sb.AppendLine($"Envío {p.Entrega,-8}      {Dinero.Formatear(p.Tarifa),20}");
        sb.AppendLine($"TOTAL               {Dinero.Formatear(p.Total),20}");
        if (p.Pago is { } pago)
            sb.AppendLine($"Cobro: {pago.Referencia} ({pago.Proveedor}) {pago.Fecha:dd/MM/yyyy HH:mm}");
        if (p.Despacho is { } d)
            sb.AppendLine($"Guía : {d.NumeroGuia} · estimada {d.FechaEstimada:dd/MM/yyyy}");
        return new DocumentoRenderizado($"Comprobante de venta {p.Codigo}", $"VENTA_{p.Codigo}", sb.ToString());
    }
}

//abstraccion refinada: comprobante de devolucion
public sealed class ComprobanteDevolucion : Comprobante
{
    private readonly PedidoDetalleDto _pedido;

    public ComprobanteDevolucion(PedidoDetalleDto pedido, ISalidaDocumento salida) : base(salida)
    {
        if (pedido.Devolucion is null)
            throw new ExcepcionAplicacion("SIN_DEVOLUCION", $"El pedido {pedido.Codigo} no tiene devolución registrada.");
        _pedido = pedido;
    }

    public override DocumentoRenderizado Componer()
    {
        var p = _pedido;
        var d = p.Devolucion!;
        var sb = new StringBuilder();
        Encabezado(sb, "COMPROBANTE DE DEVOLUCIÓN", p);
        sb.AppendLine($"Motivo              {d.Motivo,20}");
        sb.AppendLine($"Días desde entrega  {d.DiasDesdeEntrega,20}");
        sb.AppendLine($"Total pagado        {Dinero.Formatear(p.Total),20}");
        sb.AppendLine($"Envío no reembolsable {Dinero.Formatear(-p.Tarifa),18}");
        sb.AppendLine($"REEMBOLSO           {Dinero.Formatear(d.MontoReembolso),20}");
        sb.AppendLine($"Ref. reembolso: {d.Referencia}");
        sb.AppendLine($"Stock reintegrado: {(d.StockReintegrado ? "SÍ" : "NO")}");
        return new DocumentoRenderizado($"Comprobante de devolución {p.Codigo}", $"DEVOLUCION_{p.Codigo}", sb.ToString());
    }
}

public enum TipoComprobante { Venta, Devolucion }

//combina en tiempo de ejecucion el tipo de comprobante con el medio de salida elegido
public sealed class ServicioComprobantes
{
    private readonly IConsultasOperativas _consultas;
    private readonly IReadOnlyDictionary<string, ISalidaDocumento> _salidas;
    private readonly Autorizador _autorizador;

    public ServicioComprobantes(IConsultasOperativas consultas, IEnumerable<ISalidaDocumento> salidas, Autorizador autorizador)
    {
        _consultas = consultas;
        _salidas = salidas.ToDictionary(s => s.Medio);
        _autorizador = autorizador;
    }

    public IReadOnlyCollection<string> Medios => _salidas.Keys.ToList();

    public async Task<string> EmitirAsync(int pedidoId, TipoComprobante tipo, string medio, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.ConsultarInformacion);
        var pedido = await _consultas.ObtenerPedidoAsync(pedidoId, ct) ?? throw new RecursoNoEncontradoException("Pedido", pedidoId);
        if (pedido.Pago is null)
            throw new ExcepcionAplicacion("SIN_PAGO", "Solo se emiten comprobantes de pedidos cobrados.");
        if (!_salidas.TryGetValue(medio, out var salida))
            throw new ExcepcionAplicacion("MEDIO_DESCONOCIDO", $"Medio de salida no disponible: {medio}");

        Comprobante comprobante = tipo == TipoComprobante.Venta
            ? new ComprobanteVenta(pedido, salida)
            : new ComprobanteDevolucion(pedido, salida);
        return await comprobante.EmitirAsync(ct);
    }
}
