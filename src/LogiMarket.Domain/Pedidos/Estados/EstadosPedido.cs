using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Pedidos.Estados;

/// PATRÓN STATE — Estado abstracto. Por defecto toda acción es inválida; cada estado concreto
/// sobrescribe únicamente las acciones que admite. No existe un switch central de transiciones.
public abstract class EstadoPedidoBase
{
    public abstract EstadoPedido Codigo { get; }

    public virtual bool PermiteEdicion => false;

    /// Acciones válidas (para habilitar controles; la regla real está en los métodos).
    public abstract IReadOnlyList<string> AccionesPermitidas { get; }

    public void ExigirEdicion()
    {
        if (!PermiteEdicion)
            throw new ExcepcionDominio("PEDIDO_NO_EDITABLE", $"Un pedido en estado {Codigo} no se puede modificar.");
    }

    public virtual void Validar(Pedido pedido, DateTime fecha, string usuario) => throw Invalida("validar");
    public virtual void Pagar(Pedido pedido, string clave, DateTime fecha, string usuario) => throw Invalida("cobrar");
    public virtual void Despachar(Pedido pedido, DateTime fecha, string usuario) => throw Invalida("despachar");
    public virtual void Entregar(Pedido pedido, DateTime fecha, string usuario) => throw Invalida("entregar");
    public virtual void Cancelar(Pedido pedido, DateTime fecha, string usuario, string motivo) => throw Invalida("cancelar");
    public virtual void Devolver(Pedido pedido, DateTime fecha, string usuario, string motivo) => throw Invalida("devolver");

    protected ExcepcionDominio Invalida(string accion) =>
        new("TRANSICION_INVALIDA", $"No se puede {accion} un pedido en estado {Codigo}.");
}

public sealed class EstadoBorrador : EstadoPedidoBase
{
    public override EstadoPedido Codigo => EstadoPedido.BORRADOR;
    public override bool PermiteEdicion => true;
    public override IReadOnlyList<string> AccionesPermitidas { get; } = ["Editar", "Validar", "Cancelar"];

    public override void Validar(Pedido pedido, DateTime fecha, string usuario)
    {
        if (pedido.Lineas.Count == 0)
            throw new ExcepcionDominio("PEDIDO_SIN_LINEAS", "No se puede validar un pedido sin líneas.");
        pedido.TransicionarA(EstadoPedido.VALIDADO, fecha, usuario, "Pedido validado");
    }

    public override void Cancelar(Pedido pedido, DateTime fecha, string usuario, string motivo) =>
        pedido.TransicionarA(EstadoPedido.CANCELADO, fecha, usuario, $"Cancelado: {motivo}");
}

public sealed class EstadoValidado : EstadoPedidoBase
{
    public override EstadoPedido Codigo => EstadoPedido.VALIDADO;
    public override IReadOnlyList<string> AccionesPermitidas { get; } = ["Cobrar", "Cancelar"];

    public override void Validar(Pedido pedido, DateTime fecha, string usuario) =>
        throw new ExcepcionDominio("ESTADO_REPETIDO", "El pedido ya se encuentra VALIDADO.");

    public override void Pagar(Pedido pedido, string clave, DateTime fecha, string usuario)
    {
        if (pedido.Total <= 0m || pedido.PoliticaDescuento == "SIN_VALORAR")
            throw new ExcepcionDominio("PEDIDO_SIN_VALORAR", "El pedido debe valorarse antes del cobro.");
        pedido.MarcarPagado(clave, fecha);
        pedido.TransicionarA(EstadoPedido.PAGADO, fecha, usuario, $"Cobro aceptado ({clave})");
    }

    public override void Cancelar(Pedido pedido, DateTime fecha, string usuario, string motivo) =>
        pedido.TransicionarA(EstadoPedido.CANCELADO, fecha, usuario, $"Cancelado: {motivo}");
}

public sealed class EstadoPagado : EstadoPedidoBase
{
    public override EstadoPedido Codigo => EstadoPedido.PAGADO;
    public override IReadOnlyList<string> AccionesPermitidas { get; } = ["Despachar"];

    public override void Pagar(Pedido pedido, string clave, DateTime fecha, string usuario) =>
        throw new ExcepcionDominio("COBRO_DUPLICADO", "El pedido ya fue cobrado; no se permite un segundo cobro.");

    public override void Despachar(Pedido pedido, DateTime fecha, string usuario)
    {
        pedido.MarcarDespachado(fecha);
        pedido.TransicionarA(EstadoPedido.DESPACHADO, fecha, usuario, "Pedido despachado");
    }
}

public sealed class EstadoDespachado : EstadoPedidoBase
{
    public override EstadoPedido Codigo => EstadoPedido.DESPACHADO;
    public override IReadOnlyList<string> AccionesPermitidas { get; } = ["Entregar"];

    public override void Entregar(Pedido pedido, DateTime fecha, string usuario)
    {
        pedido.MarcarEntregado(fecha);
        pedido.TransicionarA(EstadoPedido.ENTREGADO, fecha, usuario, "Pedido entregado");
    }
}

public sealed class EstadoEntregado : EstadoPedidoBase
{
    public override EstadoPedido Codigo => EstadoPedido.ENTREGADO;
    public override IReadOnlyList<string> AccionesPermitidas { get; } = ["Devolver"];

    public override void Devolver(Pedido pedido, DateTime fecha, string usuario, string motivo)
    {
        if (pedido.Devuelto)
            throw new ExcepcionDominio("YA_DEVUELTO", "El pedido ya registra una devolución total.");
        pedido.MarcarDevuelto(fecha, usuario, motivo);
    }
}

public sealed class EstadoCancelado : EstadoPedidoBase
{
    public override EstadoPedido Codigo => EstadoPedido.CANCELADO;
    public override IReadOnlyList<string> AccionesPermitidas { get; } = [];
}

/// Registro de instancias de estado (sin estado propio, compartidas).
public static class EstadosPedido
{
    private static readonly IReadOnlyDictionary<EstadoPedido, EstadoPedidoBase> Instancias =
        new EstadoPedidoBase[]
        {
            new EstadoBorrador(), new EstadoValidado(), new EstadoPagado(),
            new EstadoDespachado(), new EstadoEntregado(), new EstadoCancelado()
        }.ToDictionary(e => e.Codigo);

    public static EstadoPedidoBase Para(EstadoPedido estado) => Instancias[estado];
}
