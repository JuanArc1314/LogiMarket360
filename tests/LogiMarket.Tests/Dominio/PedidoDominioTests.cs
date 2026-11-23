using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Logistica;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Promociones;
using LogiMarket.Tests.Soporte;

namespace LogiMarket.Tests.Dominio;

/// Builder (P13), Prototype (P14), Memento (P29), State (P31), Strategy (P32), Abstract Factory (P12).
public class PedidoDominioTests
{
    private readonly CatalogoPrueba _c = new();
    private readonly CalculadoraPedido _calculadora = new(SelectorPoliticaDescuento.PorDefecto());

    private Pedido PedidoAna() => new PedidoBuilder().ParaCliente(_c.Ana).AgregarKit(_c.KitTeletrabajo, 1).Construir();

    private Valoracion Valorar(Pedido p) =>
        _calculadora.Valorar(p, p.Entrega == TipoEntrega.ESTANDAR ? new FabricaEntregaEstandar() : new FabricaEntregaExpres());

    [Fact(DisplayName = "P13 Builder: sin cliente o sin líneas se rechaza; completo crea agregado válido")]
    public void P13_BuilderBloqueaIncompletos()
    {
        Assert.Equal("PEDIDO_SIN_CLIENTE", Assert.Throws<ExcepcionDominio>(() =>
            new PedidoBuilder().AgregarProducto(_c.P01, 1).Construir()).Codigo);
        Assert.Equal("PEDIDO_SIN_LINEAS", Assert.Throws<ExcepcionDominio>(() =>
            new PedidoBuilder().ParaCliente(_c.Luis).Construir()).Codigo);

        var pedido = new PedidoBuilder().ParaCliente(_c.Luis).ConEntrega(TipoEntrega.EXPRES)
            .AgregarProducto(_c.P04, 2).AgregarProducto(_c.P05, 2).AgregarKit(_c.KitOficina, 1).Construir();
        Assert.Equal(3, pedido.Lineas.Count);
        Assert.Equal(EstadoPedido.BORRADOR, pedido.Estado);
        Assert.Equal(140.00m, pedido.CalcularSubtotal());
    }

    [Fact(DisplayName = "P02/P03/P04 cálculo: Ana 59.00, Luis 102.00, Carmen 90.00")]
    public void CasosNumericosOficiales()
    {
        var ana = Valorar(PedidoAna());
        Assert.Equal((60.00m, 6.00m, 5.00m, 59.00m, "VIP"), (ana.Subtotal, ana.Descuento, ana.Tarifa, ana.Total, ana.Politica));

        var luis = Valorar(new PedidoBuilder().ParaCliente(_c.Luis).ConEntrega(TipoEntrega.EXPRES)
            .AgregarProducto(_c.P04, 2).AgregarProducto(_c.P05, 2).Construir());
        Assert.Equal((90.00m, 0.00m, 12.00m, 102.00m, "NORMAL"), (luis.Subtotal, luis.Descuento, luis.Tarifa, luis.Total, luis.Politica));

        var carmen = Valorar(new PedidoBuilder().ParaCliente(_c.Carmen).AgregarProducto(_c.P03, 10).Construir());
        Assert.Equal((100.00m, 15.00m, 5.00m, 90.00m, "MAYORISTA"), (carmen.Subtotal, carmen.Descuento, carmen.Tarifa, carmen.Total, carmen.Politica));
    }

    [Fact(DisplayName = "P32 Strategy: VIP con ≥10 unidades → MAYORISTA (prioridad, sin acumular)")]
    public void P32_MayoristaTienePrioridadSobreVip()
    {
        var pedido = new PedidoBuilder().ParaCliente(_c.Ana).AgregarProducto(_c.P03, 10).Construir();
        var v = Valorar(pedido);
        Assert.Equal("MAYORISTA", v.Politica);
        Assert.Equal(15.00m, v.Descuento); // 15 %, no 25 %
    }

    private sealed class PromocionTemporada : IPoliticaDescuento
    {
        public string Codigo => "TEMPORADA";
        public int Prioridad => 75;
        public decimal Tasa => 0.12m;
        public bool Aplica(ContextoDescuento c) => c.Subtotal >= 200m;
    }

    [Fact(DisplayName = "P32 Strategy: nueva promoción se agrega sin editar la calculadora")]
    public void P32_NuevaPoliticaSinEditarCalculadora()
    {
        var selector = new SelectorPoliticaDescuento([new Mayorista15Politica(), new PromocionTemporada(),
            new Vip10Politica(), new SinDescuentoPolitica()]);
        var calc = new CalculadoraPedido(selector);
        var pedido = new PedidoBuilder().ParaCliente(_c.Ana).AgregarProducto(_c.P04, 4).AgregarProducto(_c.P01, 2).Construir(); // 200, 6 uds
        var v = calc.Valorar(pedido, new FabricaEntregaEstandar());
        Assert.Equal("TEMPORADA", v.Politica);
        Assert.Equal(24.00m, v.Descuento);
    }

    [Fact(DisplayName = "P12 Abstract Factory: tarifas 5/12, plazos 3/1, guías coherentes y sin mezclar familias")]
    public void P12_FamiliasDeEntrega()
    {
        IFabricaEntrega std = new FabricaEntregaEstandar(), exp = new FabricaEntregaExpres();
        var fecha = new DateTime(2026, 11, 18);
        Assert.Equal(5.00m, std.CrearCalculadorTarifa().Calcular(1m));
        Assert.Equal(12.00m, exp.CrearCalculadorTarifa().Calcular(1m));
        Assert.Equal(3, std.CrearCalculadorPlazo().DiasEstimados);
        Assert.Equal(1, exp.CrearCalculadorPlazo().DiasEstimados);

        var guiaStd = std.CrearGeneradorGuia().Generar("PED-0001", "Ana", 1m, fecha, std.CrearCalculadorPlazo());
        var guiaExp = exp.CrearGeneradorGuia().Generar("PED-0002", "Luis", 1m, fecha, exp.CrearCalculadorPlazo());
        Assert.StartsWith("STD-", guiaStd.Numero);
        Assert.Contains("ESTÁNDAR", guiaStd.Etiqueta);
        Assert.Equal(fecha.AddDays(3), guiaStd.FechaEstimada);
        Assert.StartsWith("EXP-", guiaExp.Numero);
        Assert.Equal(fecha.AddDays(1), guiaExp.FechaEstimada);

        var mezcla = Assert.Throws<ExcepcionDominio>(() =>
            std.CrearGeneradorGuia().Generar("PED-0003", "X", 1m, fecha, exp.CrearCalculadorPlazo()));
        Assert.Equal("MEZCLA_FAMILIAS", mezcla.Codigo);
    }

    [Fact(DisplayName = "P31 State: BORRADOR→VALIDADO→PAGADO→DESPACHADO→ENTREGADO; PAGADO→CANCELADO y repetidos rechazados")]
    public void P31_CicloDeEstados()
    {
        var p = PedidoAna();
        var f = DateTime.Now;
        Assert.Throws<ExcepcionDominio>(() => p.Despachar(f, "t"));      // no despacha antes de pagar
        p.Validar(f, "t");
        Assert.Equal("ESTADO_REPETIDO", Assert.Throws<ExcepcionDominio>(() => p.Validar(f, "t")).Codigo);
        p.AplicarValoracion(Valorar(p));
        p.RegistrarPago("K1", f, "t");
        Assert.Equal("COBRO_DUPLICADO", Assert.Throws<ExcepcionDominio>(() => p.RegistrarPago("K2", f, "t")).Codigo);
        Assert.Equal("TRANSICION_INVALIDA", Assert.Throws<ExcepcionDominio>(() => p.Cancelar(f, "t", "x")).Codigo);
        Assert.Equal("PEDIDO_NO_EDITABLE", Assert.Throws<ExcepcionDominio>(() => p.CambiarCantidad("KIT_TELETRABAJO", 2)).Codigo);
        p.Despachar(f, "t");
        p.Entregar(f, "t");
        Assert.Equal(EstadoPedido.ENTREGADO, p.Estado);
        Assert.Equal(
            [EstadoPedido.BORRADOR, EstadoPedido.VALIDADO, EstadoPedido.PAGADO, EstadoPedido.DESPACHADO, EstadoPedido.ENTREGADO],
            p.Historial.Select(h => h.EstadoNuevo));
        Assert.IsAssignableFrom<Domain.Pedidos.Estados.EstadoEntregado>(p.EstadoActual);
    }

    [Fact(DisplayName = "P14 Prototype: clonar plantilla con KIT_TELETRABAJO; editar la copia no altera plantilla ni otra copia")]
    public void P14_ClonacionProfunda()
    {
        var plantilla = PedidoAna();
        var copia1 = plantilla.ClonarComoBorrador(DateTime.Now, "t");
        var copia2 = plantilla.ClonarComoBorrador(DateTime.Now, "t");

        copia1.CambiarCantidad("KIT_TELETRABAJO", 3);

        Assert.Equal(1, plantilla.Lineas[0].Cantidad);
        Assert.Equal(1, copia2.Lineas[0].Cantidad);
        Assert.Equal(3, copia1.Lineas[0].Cantidad);
        Assert.NotSame(plantilla.Lineas, copia1.Lineas);
        Assert.NotSame(plantilla.Lineas[0], copia1.Lineas[0]);
        Assert.NotSame(plantilla.Lineas[0].Componentes[0], copia1.Lineas[0].Componentes[0]);
        Assert.Equal(["P01", "P02", "P03"], copia1.Lineas[0].Componentes.Select(c => c.Sku));
        Assert.Equal(0, copia1.Id);
        Assert.Null(copia1.ClaveIdempotencia);
        Assert.Equal(EstadoPedido.BORRADOR, copia1.Estado);
    }

    [Fact(DisplayName = "P29 Memento: tres cambios, dos deshacer y un rehacer restauran valores exactos")]
    public void P29_DeshacerRehacer()
    {
        var p = new PedidoBuilder().ParaCliente(_c.Luis).AgregarProducto(_c.P01, 1).Construir();
        var historial = new HistorialEdiciones();

        historial.Capturar(p); p.CambiarCantidad("P01", 2);          // cambio 1
        historial.Capturar(p); p.AgregarLinea(_c.KitTeletrabajo, 1); // cambio 2 (ítem anidado)
        historial.Capturar(p); p.CambiarEntrega(TipoEntrega.EXPRES); // cambio 3

        historial.Deshacer(p);
        Assert.Equal(TipoEntrega.ESTANDAR, p.Entrega);
        Assert.Equal(2, p.Lineas.Count);
        historial.Deshacer(p);
        Assert.Single(p.Lineas);
        Assert.Equal(2, p.Lineas[0].Cantidad);
        Assert.Equal(40.00m, p.CalcularSubtotal());

        historial.Rehacer(p);
        Assert.Equal(2, p.Lineas.Count);
        Assert.Equal(["P01", "P02", "P03"], p.Lineas[1].Componentes.Select(c => c.Sku));
        Assert.Equal(100.00m, p.CalcularSubtotal());
    }

    [Fact(DisplayName = "P29 Memento: máximo 5 estados; tras confirmar el historial se bloquea (no deshace un pago)")]
    public void P29_CapacidadYBloqueo()
    {
        var p = new PedidoBuilder().ParaCliente(_c.Luis).AgregarProducto(_c.P01, 1).Construir();
        var historial = new HistorialEdiciones();
        for (var i = 2; i <= 8; i++) { historial.Capturar(p); p.CambiarCantidad("P01", i); }
        Assert.Equal(HistorialEdiciones.Capacidad, historial.EstadosGuardados);

        p.Validar(DateTime.Now, "t");
        p.AplicarValoracion(Valorar(p));
        p.RegistrarPago("K", DateTime.Now, "t");
        Assert.Equal("MEMENTO_BLOQUEADO", Assert.Throws<ExcepcionDominio>(() => historial.Deshacer(p)).Codigo);
        historial.BloquearYLimpiar();
        Assert.False(historial.PuedeDeshacer);
        Assert.Equal(EstadoPedido.PAGADO, p.Estado);
    }

    [Fact(DisplayName = "Redondeo monetario AwayFromZero a 2 decimales")]
    public void RedondeoMonetario()
    {
        Assert.Equal(0.13m, Dinero.Redondear(0.125m));
        Assert.Equal(2.68m, Dinero.Redondear(26.75m * 0.10m));
        Assert.Equal("USD 59.00", Dinero.Formatear(59m));
    }
}
