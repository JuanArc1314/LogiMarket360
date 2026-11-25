using LogiMarket.Application.Clientes;
using LogiMarket.Application.Comandos;
using LogiMarket.Application.Comun;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Consultas.Filtros;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Devoluciones;
using LogiMarket.Application.Eventos;
using LogiMarket.Application.Logistica;
using LogiMarket.Application.Metricas;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Pedidos.Validacion;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using LogiMarket.Tests.Soporte;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.Tests.Integracion;

//patrones comportamentales P23-P34 observados en ejecucion
public class ComportamentalesTests
{
    [Fact(DisplayName = "P23 Chain: cliente inactivo y sin stock → primero CLIENTE; al corregir aparece STOCK; sin cobro")]
    public async Task P23_CadenaDeValidacion()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var p = await e.CrearPedidoAsync("Luis", TipoEntrega.ESTANDAR, ("P04", 5)); //solo hay 4 webcams
        var luis = await e.ClienteIdAsync("Luis");
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioClientes>()
            .GuardarAsync(new(luis, "0100000002", "Luis Ruiz", "luis.ruiz@example.com", "0990000002", CategoriaCliente.NORMAL, false)));

        var r1 = await e.ConfirmarAsync(p.Id);
        Assert.Equal("CLIENTE_INACTIVO", r1.CodigoRechazo);
        Assert.Equal(["1-ClienteActivo"], r1.EslabonesEvaluados);

        await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioClientes>()
            .GuardarAsync(new(luis, "0100000002", "Luis Ruiz", "luis.ruiz@example.com", "0990000002", CategoriaCliente.NORMAL, true)));
        var r2 = await e.ConfirmarAsync(p.Id);
        Assert.Equal("STOCK_INSUFICIENTE", r2.CodigoRechazo);
        Assert.Equal(["1-ClienteActivo", "2-LineasCantidades", "3-ReglasStock"], r2.EslabonesEvaluados);

        Assert.Empty(e.Servicio<IBitacoraOperativa>().Consultar(CategoriasBitacora.Pago)); //no se ejecuto el cobro
        Assert.Equal(0, await e.ContarAsync(db => db.Pagos.Count()));

        e.IniciarSesion(RolUsuario.CONSULTA); //el ultimo eslabon: autorizacion
        var p2 = await e.EnAmbitoAsync(async sp => (await sp.GetRequiredService<IConsultasOperativas>().ListarPedidosAsync()).First().Id);
        await e.EnAmbitoAsync(async sp =>
        {
            var pedido = (await sp.GetRequiredService<Application.Puertos.IRepositorioPedidos>().ObtenerAsync(p2))!;
            pedido.CambiarCantidad("P04", 1);
            var cliente = await sp.GetRequiredService<Application.Puertos.IRepositorioClientes>().ObtenerAsync(pedido.ClienteId);
            var productos = await sp.GetRequiredService<Application.Puertos.IRepositorioCatalogo>().ObtenerProductosAsync(["P04"]);
            var r3 = new FabricaCadenaValidacion().CrearParaConfirmacion().Validar(new ContextoValidacionPedido
            { Pedido = pedido, Cliente = cliente, Productos = productos, Usuario = e.Sesion.Actual });
            Assert.Equal("NO_AUTORIZADO", r3.Rechazo!.Codigo);
            Assert.Equal("4-AutorizacionCondiciones", r3.Rechazo.Eslabon);
        });
    }

    [Fact(DisplayName = "P24 Command: el mismo comando desde la UI y desde un invocador de prueba; historial con entrada y resultado")]
    public async Task P24_Comandos()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var invocador = e.Servicio<InvocadorComandos>();
        var p = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));

        var resultado = await e.EnAmbitoAsync(sp =>
            invocador.EjecutarAsync(sp.GetRequiredService<FabricaComandos>().Confirmar(p.Id, "K-CMD")));
        var repetido = await e.EnAmbitoAsync(sp =>
            invocador.EjecutarAsync(sp.GetRequiredService<FabricaComandos>().Confirmar(p.Id, "K-CMD")));
        var cancelar = await e.EnAmbitoAsync(sp =>
            invocador.EjecutarAsync(sp.GetRequiredService<FabricaComandos>().Cancelar(p.Id, "cliente desiste")));

        Assert.True(resultado.Exito);
        Assert.True(repetido.Exito);
        Assert.False(cancelar.Exito);
        Assert.Equal("TRANSICION_INVALIDA", cancelar.Codigo);
        var historial = invocador.Historial;
        Assert.Equal(["ConfirmarPedido", "ConfirmarPedido", "CancelarPedido"], historial.Select(h => h.Comando));
        Assert.Contains("clave=K-CMD", historial[0].Entrada);
        Assert.Contains("PAGADO", historial[0].Resultado);
        Assert.Equal(1, await e.ContarAsync(db => db.Pagos.Count()));
        Assert.Equal(3, e.Servicio<MetricasSesion>().Instantanea().ComandosEjecutados);
    }

    [Fact(DisplayName = "P25 Interpreter: «ESTADO = PAGADO AND TOTAL >= 50» devuelve solo los pedidos que cumplen")]
    public async Task P25_InterpreteFiltra()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var ana = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));   //59 -> PAGADO
        var luis = await e.CrearPedidoAsync("Luis", TipoEntrega.ESTANDAR, ("P05", 2));             //15 -> PAGADO
        var carmen = await e.CrearPedidoAsync("Carmen", TipoEntrega.ESTANDAR, ("P03", 10));        //90 -> BORRADOR
        await e.ConfirmarAsync(ana.Id);
        await e.ConfirmarAsync(luis.Id);

        var r = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioReportes>().FiltrarPedidosAsync("ESTADO = PAGADO AND TOTAL >= 50"));
        Assert.Equal([ana.Codigo], r.Pedidos.Select(x => x.Codigo));

        var conCliente = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioReportes>().FiltrarPedidosAsync("CLIENTE = carmen OR CLIENTE = \"Luis Ruiz\""));
        Assert.Equal(new[] { carmen.Codigo, luis.Codigo }.Order(), conCliente.Pedidos.Select(x => x.Codigo).Order());
    }

    [Fact(DisplayName = "P26 Interpreter: precedencia AND > OR, paréntesis y errores de sintaxis legibles")]
    public void P26_PrecedenciaYErrores()
    {
        var parser = new ParserFiltroPedidos();
        PedidoResumenDto P(EstadoPedido estado, decimal total) => new(1, "PED", 1, "Ana López", estado, total, DateTime.Now, TipoEntrega.ESTANDAR, 1, false);

        var sinParentesis = parser.Analizar("ESTADO = BORRADOR OR ESTADO = PAGADO AND TOTAL >= 50");
        var conParentesis = parser.Analizar("(ESTADO = BORRADOR OR ESTADO = PAGADO) AND TOTAL >= 50");
        var borradorBarato = P(EstadoPedido.BORRADOR, 10);
        Assert.True(sinParentesis.Interpretar(borradorBarato));    //BORRADOR or (PAGADO and ≥50)
        Assert.False(conParentesis.Interpretar(borradorBarato));   //(BORRADOR or PAGADO) and ≥50
        Assert.Equal("(ESTADO = BORRADOR OR (ESTADO = PAGADO AND TOTAL >= 50))", sinParentesis.ToString());

        foreach (var invalido in new[] { "ESTADO = PAGADO AND", "(TOTAL >= 50", "TOTAL >= abc", "ESTADO >= PAGADO",
                     "ESTADO = VOLANDO", "PRECIO = 5", "TOTAL >= 50; DROP TABLE Pedidos", "" })
        {
            var error = Assert.Throws<ErrorSintaxisFiltro>(() => parser.Analizar(invalido));
            Assert.StartsWith("Error de sintaxis", error.Message);
        }
    }

    [Fact(DisplayName = "P28 Mediator: al confirmar, catálogo, pedidos y métricas se refrescan sin acoplamiento directo")]
    public async Task P28_Mediator()
    {
        var rutas = new Dictionary<EventoVista, string[]>
        {
            [EventoVista.PedidoConfirmado] = ["Catalogo", "Pedidos", "Metricas", "Inventario"]
        };
        var mediador = new MediadorVistas(rutas);
        var catalogo = new VistaDoble("Catalogo");
        var pedidos = new VistaDoble("Pedidos");
        var metricas = new VistaDoble("Metricas");
        var cobro = new VistaDoble("Cobro");
        foreach (var v in new[] { catalogo, pedidos, metricas, cobro }) mediador.Registrar(v);

        await mediador.NotificarAsync(cobro, EventoVista.PedidoConfirmado);

        Assert.Equal((1, 1, 1, 0), (catalogo.Refrescos, pedidos.Refrescos, metricas.Refrescos, cobro.Refrescos));
        Assert.Equal(["Catalogo", "Pedidos", "Metricas"], mediador.UltimasVistasRefrescadas);
    }

    private sealed class VistaDoble(string nombre) : IVistaColaboradora
    {
        public string NombreVista => nombre;
        public int Refrescos { get; private set; }
        public Task RefrescarAsync() { Refrescos++; return Task.CompletedTask; }
    }

    [Fact(DisplayName = "P30 Observer: PedidoPagado con tres suscriptores actualiza notificación, auditoría y métrica una vez")]
    public async Task P30_Observer()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var p = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        await e.ConfirmarAsync(p.Id);

        var r1 = await e.EnAmbitoAsync(sp =>
        {
            Assert.Equal(["Notificacion", "Metricas", "Auditoria"], sp.GetRequiredService<IPublicadorEventos>().Suscriptores);
            return sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync();
        });
        var r2 = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());

        Assert.Equal((1, 0), (r1.Procesados, r1.Fallidos));
        Assert.Equal(0, r2.Procesados); //no hay reprocesamiento
        Assert.Equal(1, await e.ContarAsync(db => db.Notificaciones.Count()));
        Assert.Equal(1, await e.ContarAsync(db => db.Auditorias.Count(a => a.Accion == "EVENTO_PedidoPagado")));
        Assert.Equal(1, e.Servicio<MetricasSesion>().Instantanea().PedidosPagados);
        Assert.Equal(0, await e.ContarAsync(db => db.EventosOutbox.Count(x => x.FechaProcesado == null)));
    }

    [Fact(DisplayName = "P33 Template Method: DEFECTO al día 20 aceptado; ARREPENTIMIENTO al día 10 rechazado sin reembolso/stock")]
    public async Task P33_TemplateMethod()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.ADMIN);
        var defecto = await PedidoEntregadoAsync(e, "Ana", ("KIT_TELETRABAJO", 1));
        var arrepentido = await PedidoEntregadoAsync(e, "Luis", ("P05", 2));
        e.Reloj.AvanzarDias(20);
        var stockAntes = await e.StockAsync("P05");

        var ok = await DevolverAsync(e, defecto, MotivoDevolucion.DEFECTO, sinUso: false);
        e.Reloj.AvanzarDias(-10); //dia 10 desde la entrega
        var rechazo = await DevolverAsync(e, arrepentido, MotivoDevolucion.ARREPENTIMIENTO, sinUso: true);

        Assert.True(ok.Aceptada);
        Assert.Equal(20, ok.DiasDesdeEntrega);
        Assert.Equal(["1-VerificarElegibilidad", "2-InspeccionarCondicion", "3-CalcularReembolso",
            "4-RegistrarDevolucionYStock", "5-GenerarEvento"], ok.PasosEjecutados);
        Assert.False(rechazo.Aceptada);
        Assert.Equal("FUERA_DE_PLAZO", rechazo.Codigo);
        Assert.Equal(["1-VerificarElegibilidad"], rechazo.PasosEjecutados);
        Assert.Equal(stockAntes, await e.StockAsync("P05"));
        Assert.Equal(1, await e.ContarAsync(db => db.Devoluciones.Count()));

        //misma estructura: arrepentimiento dentro de plazo pero con producto usado -> falla en el paso 2
        e.Reloj.AvanzarDias(-5);
        var usado = await DevolverAsync(e, arrepentido, MotivoDevolucion.ARREPENTIMIENTO, sinUso: false);
        Assert.Equal(("PRODUCTO_USADO", 2), (usado.Codigo, usado.PasosEjecutados.Count));
    }

    internal static async Task<int> PedidoEntregadoAsync(EntornoPrueba e, string cliente, params (string, int)[] lineas)
    {
        var p = await e.CrearPedidoAsync(cliente, TipoEntrega.ESTANDAR, lineas);
        Assert.True((await e.ConfirmarAsync(p.Id)).Exito);
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioLogistica>().DespacharAsync(p.Id));
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioLogistica>().EntregarAsync(p.Id));
        return p.Id;
    }

    internal static Task<ResultadoDevolucion> DevolverAsync(EntornoPrueba e, int pedidoId, MotivoDevolucion motivo, bool sinUso) =>
        e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioDevoluciones>().ProcesarAsync(new(pedidoId, motivo, sinUso)));
}
