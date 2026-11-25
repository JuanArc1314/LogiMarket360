using LogiMarket.Domain.Logistica;
using LogiMarket.Domain.Promociones;

namespace LogiMarket.Domain.Pedidos;

//calculadora pura: subtotal = Σ(precio x cantidad)
//descuento = redondear(subtotal x tasa, 2)
//total = subtotal − descuento + tarifa
//usa la interfaz de estrategia y la fabrica de la familia
public sealed class CalculadoraPedido
{
    private readonly SelectorPoliticaDescuento _selector;

    public CalculadoraPedido(SelectorPoliticaDescuento selector) => _selector = selector;

    public Valoracion Valorar(Pedido pedido, IFabricaEntrega fabrica)
    {
        if (fabrica.Tipo != pedido.Entrega)
            throw new ArgumentException("La fábrica no corresponde a la forma de entrega del pedido.", nameof(fabrica));

        var subtotal = pedido.CalcularSubtotal();
        var politica = _selector.Seleccionar(new ContextoDescuento(pedido.CategoriaCliente, pedido.UnidadesFisicas, subtotal));
        var descuento = _selector.CalcularDescuento(subtotal, politica);
        var tarifa = fabrica.CrearCalculadorTarifa().Calcular(pedido.PesoTotalKg);
        var plazo = fabrica.CrearCalculadorPlazo().DiasEstimados;
        return new Valoracion(subtotal, politica.Codigo, politica.Tasa, descuento, fabrica.Tipo, tarifa, plazo);
    }
}
