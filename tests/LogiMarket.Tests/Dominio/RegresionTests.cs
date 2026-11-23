using LogiMarket.Application.Coordinacion;
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

    [Fact(DisplayName = "R03 Mediator: una vista que falla no impide refrescar las demás")]
    public async Task R03_MediadorAislaFallos()
    {
        var mediador = new MediadorVistas(new Dictionary<EventoVista, string[]>
        {
            [EventoVista.PedidoConfirmado] = ["A", "B", "C"]
        });
        var a = new Vista("A");
        var b = new Vista("B", falla: true);
        var c = new Vista("C");
        foreach (var v in new[] { a, b, c }) mediador.Registrar(v);
        string? aviso = null;
        mediador.Notificado += (_, texto) => aviso = texto;

        await mediador.NotificarAsync(null, EventoVista.PedidoConfirmado);

        Assert.Equal((1, 1), (a.Refrescos, c.Refrescos));
        Assert.Equal(["A", "C"], mediador.UltimasVistasRefrescadas);
        Assert.Contains("sin refrescar: B", aviso);
    }

    private sealed class Vista(string nombre, bool falla = false) : IVistaColaboradora
    {
        public string NombreVista => nombre;
        public int Refrescos { get; private set; }

        public Task RefrescarAsync()
        {
            if (falla) throw new InvalidOperationException("vista con error");
            Refrescos++;
            return Task.CompletedTask;
        }
    }
}
