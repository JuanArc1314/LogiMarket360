using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Promociones;

//datos con los que una politica decide si aplica
public sealed record ContextoDescuento(CategoriaCliente Categoria, int UnidadesFisicas, decimal Subtotal);

//patron strategy - estrategia
//cada promocion declara su prioridad y su condicion
//añadir una politica nueva no obliga a editar una cadena central de if/else
public interface IPoliticaDescuento
{
    string Codigo { get; }

    //mayor numero = mayor prioridad
    //las politicas son excluyentes (nunca se acumulan)
    int Prioridad { get; }

    decimal Tasa { get; }

    bool Aplica(ContextoDescuento contexto);
}

//mayorista: 15 % si hay al menos 10 unidades fisicas finales (prioridad sobre vip)
public sealed class Mayorista15Politica : IPoliticaDescuento
{
    public const int UnidadesMinimas = 10;
    public string Codigo => "MAYORISTA";
    public int Prioridad => 100;
    public decimal Tasa => 0.15m;
    public bool Aplica(ContextoDescuento contexto) => contexto.UnidadesFisicas >= UnidadesMinimas;
}

//vip: 10 % si el cliente pertenece a la categoria vip
public sealed class Vip10Politica : IPoliticaDescuento
{
    public string Codigo => "VIP";
    public int Prioridad => 50;
    public decimal Tasa => 0.10m;
    public bool Aplica(ContextoDescuento contexto) => contexto.Categoria == CategoriaCliente.VIP;
}

//normal: 0 % en los demas casos (siempre aplica, menor prioridad)
public sealed class SinDescuentoPolitica : IPoliticaDescuento
{
    public string Codigo => "NORMAL";
    public int Prioridad => 0;
    public decimal Tasa => 0m;
    public bool Aplica(ContextoDescuento contexto) => true;
}

//contexto de la estrategia: elige una politica segun prioridad explicita
public sealed class SelectorPoliticaDescuento
{
    private readonly IReadOnlyList<IPoliticaDescuento> _politicas;

    public SelectorPoliticaDescuento(IEnumerable<IPoliticaDescuento> politicas)
    {
        _politicas = politicas.OrderByDescending(p => p.Prioridad).ToList();
        if (_politicas.Count == 0)
            throw new ArgumentException("Se requiere al menos una política de descuento.", nameof(politicas));
        if (_politicas.GroupBy(p => p.Prioridad).Any(g => g.Count() > 1))
            throw new ArgumentException("Dos políticas no pueden compartir prioridad (orden ambiguo).", nameof(politicas));
    }

    public static SelectorPoliticaDescuento PorDefecto() =>
        new([new Mayorista15Politica(), new Vip10Politica(), new SinDescuentoPolitica()]);

    public IReadOnlyList<IPoliticaDescuento> Politicas => _politicas;

    public IPoliticaDescuento Seleccionar(ContextoDescuento contexto) =>
        _politicas.FirstOrDefault(p => p.Aplica(contexto))
        ?? throw new ExcepcionDominio("SIN_POLITICA", "Ninguna política de descuento aplica.");

    public decimal CalcularDescuento(decimal subtotal, IPoliticaDescuento politica) =>
        Dinero.Redondear(subtotal * politica.Tasa);
}
