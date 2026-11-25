using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Domain.Operaciones;

//cobro simulado aprobado
//unicos: referencia, clave de idempotencia y pedido
public sealed class Pago
{
    private Pago() { }

    public Pago(int pedidoId, string referencia, string claveIdempotencia, decimal monto,
        string proveedor, DateTime fecha, string usuario)
    {
        PedidoId = pedidoId;
        Referencia = Guardia.Texto(referencia, "La referencia de cobro", 60);
        ClaveIdempotencia = Guardia.Texto(claveIdempotencia, "La clave de idempotencia", 80);
        Dinero.ExigirPositivo(monto, "El monto cobrado");
        Monto = monto;
        Proveedor = Guardia.Texto(proveedor, "El proveedor", 40);
        Fecha = fecha;
        Usuario = usuario;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public string Referencia { get; private set; } = string.Empty;
    public string ClaveIdempotencia { get; private set; } = string.Empty;
    public decimal Monto { get; private set; }
    public string Proveedor { get; private set; } = string.Empty;
    public DateTime Fecha { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
}

public enum TipoMovimiento
{
    CARGA_INICIAL,
    VENTA,
    DEVOLUCION,
    AJUSTE
}

//movimiento de inventario trazable (+ ingreso / − salida) con motivo y referencia
public sealed class MovimientoInventario
{
    private MovimientoInventario() { }

    public MovimientoInventario(int productoId, string sku, int cantidad, TipoMovimiento tipo,
        string referencia, int stockResultante, DateTime fecha, string usuario)
    {
        if (cantidad == 0)
            throw new ExcepcionDominio("MOVIMIENTO_VACIO", "Un movimiento de inventario no puede ser cero.");
        ProductoId = productoId;
        Sku = sku;
        Cantidad = cantidad;
        Tipo = tipo;
        Referencia = Guardia.Texto(referencia, "La referencia", 60);
        StockResultante = stockResultante;
        Fecha = fecha;
        Usuario = usuario;
    }

    public int Id { get; private set; }
    public int ProductoId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public int Cantidad { get; private set; }
    public TipoMovimiento Tipo { get; private set; }
    public string Referencia { get; private set; } = string.Empty;
    public int StockResultante { get; private set; }
    public DateTime Fecha { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
}

//despacho con guia simulada (uno por pedido)
public sealed class Despacho
{
    private Despacho() { }

    public Despacho(int pedidoId, string numeroGuia, TipoEntrega familia, string etiqueta,
        DateTime fechaDespacho, DateTime fechaEstimada, string usuario)
    {
        PedidoId = pedidoId;
        NumeroGuia = Guardia.Texto(numeroGuia, "El número de guía", 40);
        Familia = familia;
        Etiqueta = Guardia.Texto(etiqueta, "La etiqueta", 300);
        FechaDespacho = fechaDespacho;
        FechaEstimada = fechaEstimada;
        Usuario = usuario;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public string NumeroGuia { get; private set; } = string.Empty;
    public TipoEntrega Familia { get; private set; }
    public string Etiqueta { get; private set; } = string.Empty;
    public DateTime FechaDespacho { get; private set; }
    public DateTime FechaEstimada { get; private set; }
    public DateTime? FechaEntrega { get; private set; }
    public string Usuario { get; private set; } = string.Empty;

    public void RegistrarEntrega(DateTime fecha)
    {
        if (FechaEntrega is not null)
            throw new ExcepcionDominio("ENTREGA_REPETIDA", "La entrega ya fue registrada.");
        FechaEntrega = fecha;
    }
}

public enum MotivoDevolucion
{
    DEFECTO,
    ARREPENTIMIENTO
}

//devolucion total aceptada (una por pedido) con reembolso simulado unico
public sealed class Devolucion
{
    private Devolucion() { }

    public Devolucion(int pedidoId, MotivoDevolucion motivo, int diasDesdeEntrega, decimal montoReembolso,
        string referenciaReembolso, bool stockReintegrado, DateTime fecha, string usuario)
    {
        PedidoId = pedidoId;
        Motivo = motivo;
        DiasDesdeEntrega = diasDesdeEntrega;
        Dinero.ExigirPositivo(montoReembolso, "El reembolso");
        MontoReembolso = montoReembolso;
        ReferenciaReembolso = Guardia.Texto(referenciaReembolso, "La referencia de reembolso", 60);
        StockReintegrado = stockReintegrado;
        Fecha = fecha;
        Usuario = usuario;
    }

    public int Id { get; private set; }
    public int PedidoId { get; private set; }
    public MotivoDevolucion Motivo { get; private set; }
    public int DiasDesdeEntrega { get; private set; }
    public decimal MontoReembolso { get; private set; }
    public string ReferenciaReembolso { get; private set; } = string.Empty;
    public bool StockReintegrado { get; private set; }
    public DateTime Fecha { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
}

public enum CategoriaAuditoria
{
    NEGOCIO,
    SEGURIDAD,
    SISTEMA
}

//bitacora persistente de negocio y seguridad
public sealed class Auditoria
{
    private Auditoria() { }

    public Auditoria(DateTime fecha, string usuario, CategoriaAuditoria categoria, string accion,
        string detalle, string? referencia = null)
    {
        Fecha = fecha;
        Usuario = string.IsNullOrWhiteSpace(usuario) ? "sistema" : usuario;
        Categoria = categoria;
        Accion = Guardia.Texto(accion, "La acción", 60);
        Detalle = Guardia.TextoOpcional(detalle, 500);
        Referencia = referencia;
    }

    public long Id { get; private set; }
    public DateTime Fecha { get; private set; }
    public string Usuario { get; private set; } = string.Empty;
    public CategoriaAuditoria Categoria { get; private set; }
    public string Accion { get; private set; } = string.Empty;
    public string Detalle { get; private set; } = string.Empty;
    public string? Referencia { get; private set; }
}

//aviso emitido por un notificador simulado (unico por evento y canal)
public sealed class Notificacion
{
    private Notificacion() { }

    public Notificacion(Guid eventoId, string canal, string destino, string asunto, string mensaje, DateTime fecha)
    {
        EventoId = eventoId;
        Canal = canal;
        Destino = destino;
        Asunto = asunto;
        Mensaje = mensaje;
        Fecha = fecha;
    }

    public long Id { get; private set; }
    public Guid EventoId { get; private set; }
    public string Canal { get; private set; } = string.Empty;
    public string Destino { get; private set; } = string.Empty;
    public string Asunto { get; private set; } = string.Empty;
    public string Mensaje { get; private set; } = string.Empty;
    public DateTime Fecha { get; private set; }
}
