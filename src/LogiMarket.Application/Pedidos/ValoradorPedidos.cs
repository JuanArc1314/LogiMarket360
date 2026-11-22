using LogiMarket.Domain.Logistica;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Pedidos;

/// Elige la fábrica abstracta de la familia de entrega (registro por DI, sin switch).
public sealed class SelectorFabricaEntrega
{
    private readonly IReadOnlyDictionary<TipoEntrega, IFabricaEntrega> _fabricas;

    public SelectorFabricaEntrega(IEnumerable<IFabricaEntrega> fabricas) =>
        _fabricas = fabricas.ToDictionary(f => f.Tipo);

    public IFabricaEntrega Para(TipoEntrega tipo) =>
        _fabricas.TryGetValue(tipo, out var fabrica)
            ? fabrica
            : throw new InvalidOperationException($"No hay fábrica registrada para la entrega {tipo}.");
}

/// Une la calculadora (Strategy) con la fábrica de la familia (Abstract Factory).
public sealed class ValoradorPedidos
{
    private readonly CalculadoraPedido _calculadora;
    private readonly SelectorFabricaEntrega _fabricas;

    public ValoradorPedidos(CalculadoraPedido calculadora, SelectorFabricaEntrega fabricas)
    {
        _calculadora = calculadora;
        _fabricas = fabricas;
    }

    public Valoracion Valorar(Pedido pedido) => _calculadora.Valorar(pedido, _fabricas.Para(pedido.Entrega));

    public Valoracion Aplicar(Pedido pedido)
    {
        var valoracion = Valorar(pedido);
        pedido.AplicarValoracion(valoracion);
        return valoracion;
    }
}
