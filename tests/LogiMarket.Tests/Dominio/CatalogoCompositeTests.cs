using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Comun;
using LogiMarket.Tests.Soporte;

namespace LogiMarket.Tests.Dominio;

//composite (P18), iterator (P27) y visitor (P34) sobre el catalogo oficial
public class CatalogoCompositeTests
{
    private readonly CatalogoPrueba _c = new();

    [Fact(DisplayName = "P18 Composite: KIT_OFICINA = 50.00 y KIT_TELETRABAJO = 60.00 (recursivo)")]
    public void P18_PreciosDeKitsSeCalculanRecursivamente()
    {
        Assert.Equal(50.00m, _c.KitOficina.PrecioVenta);
        Assert.Equal(60.00m, _c.KitTeletrabajo.PrecioVenta);
        Assert.True(_c.KitTeletrabajo.EsCompuesto);
    }

    [Fact(DisplayName = "P18 Composite: ciclo directo e indirecto rechazado (KIT_CICLICO)")]
    public void P18_CicloRechazado()
    {
        var directo = Assert.Throws<ExcepcionDominio>(() => _c.KitOficina.AgregarComponente(_c.KitOficina, 1));
        Assert.Equal("KIT_CICLICO", directo.Codigo);

        //KIT_TELETRABAJO ya contiene KIT_OFICINA: meterlo dentro de KIT_OFICINA cerraria un ciclo
        var indirecto = Assert.Throws<ExcepcionDominio>(() => _c.KitOficina.AgregarComponente(_c.KitTeletrabajo, 1));
        Assert.Equal("KIT_CICLICO", indirecto.Codigo);
        Assert.Equal(50.00m, _c.KitOficina.PrecioVenta); //sin efectos colaterales
    }

    [Fact(DisplayName = "P27 Iterator: KIT_TELETRABAJO devuelve P01, P02, P03 una vez cada uno y termina")]
    public void P27_IteradorRecorreComponentesFisicos()
    {
        var iterador = _c.KitTeletrabajo.CrearIterador();
        var recorrido = new List<(string, int)>();
        var pasos = 0;
        while (iterador.MoverSiguiente())
        {
            recorrido.Add((iterador.Actual.Sku, iterador.Actual.Cantidad));
            Assert.True(++pasos < 100, "El iterador no debe recorrer indefinidamente");
        }
        Assert.Equal([("P01", 1), ("P02", 1), ("P03", 1)], recorrido);
        Assert.False(iterador.MoverSiguiente());
    }

    [Fact(DisplayName = "P27 Iterator: propaga cantidades (KIT_TELETRABAJO ×6 → 6 de cada SKU)")]
    public void P27_IteradorPropagaCantidades()
    {
        var expandido = ExpansionCatalogo.ExpandirAgregado(_c.KitTeletrabajo, 6);
        Assert.Equal(["P01:6", "P02:6", "P03:6"], expandido.Select(e => $"{e.Sku}:{e.Cantidad}"));
    }

    [Fact(DisplayName = "P34 Visitor: desglose P01/P02/P03 y peso logístico 1.00 kg con visita anidada")]
    public void P34_VisitantesDesgloseYPeso()
    {
        var desglose = new DesgloseFisicoVisitor();
        var peso = new PesoLogisticoVisitor();
        _c.KitTeletrabajo.Aceptar(desglose);
        _c.KitTeletrabajo.Aceptar(peso);

        Assert.Equal(["P01", "P02", "P03"], desglose.Lineas.Select(l => l.Sku));
        Assert.Equal(1.00m, peso.PesoTotalKg);
        Assert.Equal(0.90m, peso.PesoPorKit["KIT_OFICINA"]);
        Assert.Contains(desglose.Traza, t => t.Contains("[KIT_OFICINA]"));      //visita anidada verificable
        Assert.StartsWith("  ", desglose.Traza.First(t => t.Contains("[KIT_OFICINA]")));
    }

    [Fact(DisplayName = "Invariantes de producto: precio > 0, peso >= 0, stock no negativo")]
    public void InvariantesProducto()
    {
        Assert.Equal("MONTO_INVALIDO", Assert.Throws<ExcepcionDominio>(() =>
            new ProductoSimple("X1", "X", "C", "", 0m, 1m, 1)).Codigo);
        Assert.Equal("PESO_INVALIDO", Assert.Throws<ExcepcionDominio>(() =>
            new ProductoSimple("X1", "X", "C", "", 1m, -1m, 1)).Codigo);
        var stock = Assert.Throws<ExcepcionDominio>(() => _c.P02.DescontarStock(6));
        Assert.Equal("STOCK_INSUFICIENTE", stock.Codigo);
        Assert.Equal(5, _c.P02.Stock);
    }
}
