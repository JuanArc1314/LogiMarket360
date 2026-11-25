using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Domain.Logistica;

//guia de despacho simulada producida por la familia de entrega
public sealed record GuiaDespacho(string Numero, TipoEntrega Familia, string Etiqueta, DateTime FechaEstimada);

//productos abstractos

public interface ICalculadorTarifa
{
    TipoEntrega Familia { get; }
    decimal Calcular(decimal pesoKg);
}

public interface ICalculadorPlazo
{
    TipoEntrega Familia { get; }
    int DiasEstimados { get; }
    DateTime FechaEstimada(DateTime desde);
}

public interface IGeneradorGuia
{
    TipoEntrega Familia { get; }
    GuiaDespacho Generar(string codigoPedido, string cliente, decimal pesoKg, DateTime fecha, ICalculadorPlazo plazo);
}

//patron abstract factory
//cada fabrica produce una familia compatible de objetos (tarifa, plazo y guia)
//el caso de uso no cambia al cambiar de familia
public interface IFabricaEntrega
{
    TipoEntrega Tipo { get; }
    ICalculadorTarifa CrearCalculadorTarifa();
    ICalculadorPlazo CrearCalculadorPlazo();
    IGeneradorGuia CrearGeneradorGuia();
}

//base comun (DRY) para los productos concretos

internal abstract class TarifaPlana(TipoEntrega familia, decimal valor) : ICalculadorTarifa
{
    public TipoEntrega Familia { get; } = familia;
    public decimal Calcular(decimal pesoKg)
    {
        if (pesoKg < 0m) throw new ExcepcionDominio("PESO_INVALIDO", "El peso no puede ser negativo.");
        return Dinero.Redondear(valor);
    }
}

internal abstract class PlazoFijo(TipoEntrega familia, int dias) : ICalculadorPlazo
{
    public TipoEntrega Familia { get; } = familia;
    public int DiasEstimados { get; } = dias;
    public DateTime FechaEstimada(DateTime desde) => desde.Date.AddDays(DiasEstimados);
}

internal abstract class GeneradorGuiaBase(TipoEntrega familia, string prefijo, string titulo) : IGeneradorGuia
{
    public TipoEntrega Familia { get; } = familia;

    public GuiaDespacho Generar(string codigoPedido, string cliente, decimal pesoKg, DateTime fecha, ICalculadorPlazo plazo)
    {
        //impide mezclar inadvertidamente productos de familias distintas
        if (plazo.Familia != Familia)
            throw new ExcepcionDominio("MEZCLA_FAMILIAS",
                $"La guía {Familia} no puede usar un plazo de la familia {plazo.Familia}.");
        var estimada = plazo.FechaEstimada(fecha);
        var etiqueta = $"{titulo} | {codigoPedido} | {cliente} | {pesoKg:0.00} kg | " +
                       $"{plazo.DiasEstimados} día(s) | Llega: {estimada:dd/MM/yyyy}";
        return new GuiaDespacho($"{prefijo}-{codigoPedido}", Familia, etiqueta, estimada);
    }
}

//familia estandar

internal sealed class TarifaEstandar() : TarifaPlana(TipoEntrega.ESTANDAR, 5.00m);
internal sealed class PlazoEstandar() : PlazoFijo(TipoEntrega.ESTANDAR, 3);
internal sealed class GuiaEstandar() : GeneradorGuiaBase(TipoEntrega.ESTANDAR, "STD", "ENTREGA ESTÁNDAR");

public sealed class FabricaEntregaEstandar : IFabricaEntrega
{
    public TipoEntrega Tipo => TipoEntrega.ESTANDAR;
    public ICalculadorTarifa CrearCalculadorTarifa() => new TarifaEstandar();
    public ICalculadorPlazo CrearCalculadorPlazo() => new PlazoEstandar();
    public IGeneradorGuia CrearGeneradorGuia() => new GuiaEstandar();
}

//familia expres

internal sealed class TarifaExpres() : TarifaPlana(TipoEntrega.EXPRES, 12.00m);
internal sealed class PlazoExpres() : PlazoFijo(TipoEntrega.EXPRES, 1);
internal sealed class GuiaExpres() : GeneradorGuiaBase(TipoEntrega.EXPRES, "EXP", "ENTREGA EXPRÉS");

public sealed class FabricaEntregaExpres : IFabricaEntrega
{
    public TipoEntrega Tipo => TipoEntrega.EXPRES;
    public ICalculadorTarifa CrearCalculadorTarifa() => new TarifaExpres();
    public ICalculadorPlazo CrearCalculadorPlazo() => new PlazoExpres();
    public IGeneradorGuia CrearGeneradorGuia() => new GuiaExpres();
}
