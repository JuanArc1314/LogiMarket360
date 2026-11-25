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

//familia de entrega (cada una tiene su fabrica abstracta)
public enum TipoEntrega
{
    ESTANDAR,
    EXPRES
}
