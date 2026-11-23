using LogiMarket.Application.Pedidos;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Logistica;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Promociones;
using LogiMarket.Tests.Soporte;

namespace LogiMarket.Tests.Dominio;

/// <summary>Pruebas de regresión de defectos reales encontrados durante la construcción (ver docs/RIESGOS_DISENO.md).</summary>
public class RegresionTests
{
    private readonly CatalogoPrueba _c = new();

    private EditorBorrador NuevoEditor() => new(_c.Luis, null,
        new ValoradorPedidos(new CalculadoraPedido(SelectorPoliticaDescuento.PorDefecto()),
            new SelectorFabricaEntrega([new FabricaEntregaEstandar(), new FabricaEntregaExpres()])),
        new RelojControlado());

    [Fact(DisplayName = "R01 Memento: un cambio inválido no deja rastro ni contamina la pila de rehacer")]
    public void R01_CambioInvalidoNoContaminaHistorial()
    {
        var editor = NuevoEditor();
        editor.Agregar(_c.P01, 1);
        editor.CambiarCantidad("P01", 2);

        Assert.Throws<ExcepcionDominio>(() => editor.CambiarCantidad("NO-EXISTE", 1));

        Assert.Equal(2, editor.Lineas.Single().Cantidad);
        Assert.False(editor.PuedeRehacer);          // antes del ajuste quedaba un "rehacer" fantasma
        Assert.Equal(1, editor.Historial.EstadosGuardados);
        editor.Deshacer();
        Assert.Equal(1, editor.Lineas.Single().Cantidad);
    }

    [Fact(DisplayName = "R02 Memento: deshacer sobre un pedido no BORRADOR falla sin alterar las pilas")]
    public void R02_DeshacerNoBorradorNoAlteraPilas()
    {
        var p = new PedidoBuilder().ParaCliente(_c.Luis).AgregarProducto(_c.P01, 1).Construir();
        var historial = new HistorialEdiciones();
        historial.Capturar(p);
        p.CambiarCantidad("P01", 2);
        p.Validar(DateTime.Now, "t");

        Assert.Equal("MEMENTO_BLOQUEADO", Assert.Throws<ExcepcionDominio>(() => historial.Deshacer(p)).Codigo);
        Assert.Equal(1, historial.EstadosGuardados);
        Assert.False(historial.PuedeRehacer);
    }
}
