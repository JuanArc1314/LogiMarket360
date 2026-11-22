namespace LogiMarket.Domain.Pedidos;

public enum EstadoPedido
{
    BORRADOR,
    VALIDADO,
    PAGADO,
    DESPACHADO,
    ENTREGADO,
    CANCELADO
}

/// Familia de entrega (cada una tiene su fábrica abstracta).
public enum TipoEntrega
{
    ESTANDAR,
    EXPRES
}
