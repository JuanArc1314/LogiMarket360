using LogiMarket.Application.Catalogo;
using LogiMarket.Application.Comandos;
using LogiMarket.Application.Comprobantes;
using LogiMarket.Application.Comun;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Eventos;
using LogiMarket.Application.Metricas;
using LogiMarket.Application.Notificaciones;
using LogiMarket.Application.Pagos;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using LogiMarket.Infrastructure.Notificaciones;
using LogiMarket.Infrastructure.Pagos;
using LogiMarket.Tests.Soporte;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.Tests.Integracion;

//patrones creacionales y estructurales observados en el flujo real (P11, P15-P22)
public class PatronesIntegradosTests
{
    [Fact(DisplayName = "P11 Factory Method: alternar EMAIL/SMS tras eventos; cada creador emite con su notificador")]
    public async Task P11_FactoryMethodEmailSms()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var config = e.Servicio<ConfiguracionDemostracion>();

        config.UsarCanalNotificacion(CanalesNotificacion.Email);
        var p1 = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("P01", 1));
        await e.ConfirmarAsync(p1.Id);
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());

        config.UsarCanalNotificacion(CanalesNotificacion.Sms);
        var p2 = await e.CrearPedidoAsync("Luis", TipoEntrega.ESTANDAR, ("P05", 1));
        await e.ConfirmarAsync(p2.Id);
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());

        var avisos = await e.EnAmbitoAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarNotificacionesAsync());
        Assert.Contains(avisos, n => n.Canal == "EMAIL" && n.Destino == "ana.lopez@example.com" && n.Asunto.Contains(p1.Codigo));
        Assert.Contains(avisos, n => n.Canal == "SMS" && n.Destino == "0990000002" && n.Mensaje.Length <= 160);
        Assert.Single(Directory.GetFiles(Path.Combine(e.Opciones.DirectorioNotificaciones, "email")));

        //el creador concreto devuelve el notificador concreto (sin crear concretos en la UI)
        await e.EnAmbitoAsync(async sp =>
        {
            var selector = sp.GetRequiredService<SelectorCreadorNotificador>();
            Assert.IsType<CreadorNotificadorSms>(selector.Actual);
            var evento = new Domain.Eventos.PedidoPagado(Guid.NewGuid(), DateTime.Now, 1, "PED-X", "Ana", "a@example.com", "099", 1m, "R");
            Assert.IsType<NotificadorSmsSimulado>(await selector.Actual.NotificarAsync(evento));
            config.UsarCanalNotificacion(CanalesNotificacion.Email);
            Assert.IsType<NotificadorEmailSimulado>(await selector.Actual.NotificarAsync(evento));
        });
    }

    [Fact(DisplayName = "P15 Singleton: dos consumidores reciben la misma MetricasSesion; contadores coherentes y thread-safe")]
    public async Task P15_SingletonMetricas()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var vistaPanel = e.EnAmbitoAsync(sp => Task.FromResult(sp.GetRequiredService<MetricasSesion>()));
        var vistaAuditoria = e.EnAmbitoAsync(sp => Task.FromResult(sp.GetRequiredService<MetricasSesion>()));
        var (a, b) = (await vistaPanel, await vistaAuditoria);
        Assert.Same(a, b);
        Assert.Equal(a.InstanciaId, b.InstanciaId);

        await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(a.RegistrarCobroRechazado)));
        Assert.Equal(200, b.Instantanea().CobrosRechazados);

        var p = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        await e.ConfirmarAsync(p.Id);
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());
        Assert.Equal((1, 59.00m), (b.Instantanea().PedidosPagados, b.Instantanea().MontoCobrado));
    }

    [Fact(DisplayName = "P16 Adapter: proveedor legado y alterno cobran con el mismo contrato; el legado queda intacto")]
    public async Task P16_AdapterYProveedorAlterno()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var config = e.Servicio<ConfiguracionDemostracion>();
        var legado = e.Servicio<PasarelaLegadaSimulada>();

        var p1 = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("P01", 1));
        var r1 = await e.ConfirmarAsync(p1.Id);
        config.UsarProveedorPago(ProveedoresPago.Alterno);
        var p2 = await e.CrearPedidoAsync("Luis", TipoEntrega.ESTANDAR, ("P05", 1));
        var r2 = await e.ConfirmarAsync(p2.Id);

        Assert.True(r1.Exito && r2.Exito);
        Assert.StartsWith("LEG", r1.ReferenciaPago);
        Assert.StartsWith("ALT-", r2.ReferenciaPago);
        Assert.Equal(1, legado.TransaccionesProcesadas); //el legado solo proceso el primer cobro (en centavos)
        var bitacora = e.Servicio<IBitacoraOperativa>().Consultar(CategoriasBitacora.Pago);
        Assert.Contains(bitacora, b => b.Mensaje.Contains("LEGADO.CobrarCentavos(2300,")); //ana vip: 20.00 − 2.00 + 5.00 = 23.00 -> 2300 centavos
        Assert.Contains(bitacora, b => b.Mensaje.StartsWith("ALTERNO.charge("));
    }

    [Fact(DisplayName = "P17 Bridge: 2 comprobantes × 2 medios = 4 combinaciones; vista previa visible y TXT real")]
    public async Task P17_BridgeCuatroCombinaciones()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.ADMIN);
        var id = await PedidoEntregadoYDevueltoAsync(e);
        var visor = (VisorDocumentosMemoria)e.Servicio<IVisorDocumentos>();

        foreach (var tipo in new[] { TipoComprobante.Venta, TipoComprobante.Devolucion })
        {
            var vista = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioComprobantes>().EmitirAsync(id, tipo, MediosSalida.VistaPrevia));
            Assert.Contains("Vista previa", vista);
            Assert.Contains(tipo == TipoComprobante.Venta ? "COMPROBANTE DE VENTA" : "COMPROBANTE DE DEVOLUCIÓN", visor.Ultimo!.Contenido);

            var ruta = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioComprobantes>().EmitirAsync(id, tipo, MediosSalida.ArchivoTxt));
            Assert.True(File.Exists(ruta));
            Assert.Equal(visor.Ultimo.Contenido, await File.ReadAllTextAsync(ruta));
        }
        Assert.Equal(2, Directory.GetFiles(e.Opciones.DirectorioComprobantes, "*.txt").Length);
        Assert.Contains("USD 54.00", visor.Ultimo!.Contenido);
    }

    [Fact(DisplayName = "P18 Composite persistido: precios 50/60, expansión a SKU finales y ciclo rechazado vía servicio")]
    public async Task P18_CompositePersistido()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.ADMIN);
        var kits = await e.EnAmbitoAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarKitsAsync());
        Assert.Equal(50.00m, kits.Single(k => k.Sku == "KIT_OFICINA").Precio);
        Assert.Equal(60.00m, kits.Single(k => k.Sku == "KIT_TELETRABAJO").Precio);

        var ciclo = await Assert.ThrowsAsync<ExcepcionDominio>(() => e.EnAmbitoAsync(sp =>
            sp.GetRequiredService<ServicioCatalogo>().AgregarComponenteAsync("KIT_OFICINA", "KIT_TELETRABAJO", 1)));
        Assert.Equal("KIT_CICLICO", ciclo.Codigo);
        Assert.Equal(2, await e.ContarAsync(db => db.KitComponentes.Count(c => c.KitId == 1)));

        var desglose = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioCatalogo>().DesglosarAsync("KIT_TELETRABAJO"));
        Assert.Equal(["P01", "P02", "P03"], desglose.Lineas.Select(l => l.Sku));
        Assert.Equal(1.00m, desglose.PesoTotalKg);
    }

    [Fact(DisplayName = "P19 Decorator: INICIO → PASARELA → FIN en éxito e INICIO → PASARELA → FIN(RECHAZADO) en fallo, misma correlación")]
    public async Task P19_DecoratorRegistraCiclo()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var bitacora = e.Servicio<IBitacoraOperativa>();
        var pasarela = e.Servicio<IPasarelaPago>();

        var ok = await pasarela.CobrarAsync(10.00m, "K-DEC-1");
        Assert.True(ok.Aprobado);
        e.Servicio<ConfiguracionDemostracion>().ConfigurarRechazoCobro(true);
        var ko = await pasarela.CobrarAsync(10.00m, "K-DEC-2");
        Assert.False(ko.Aprobado); //el decorator no altera el resultado

        var pagos = bitacora.Consultar(CategoriasBitacora.Pago);
        foreach (var grupo in pagos.GroupBy(p => p.Correlacion))
            Assert.Equal(["INICIO", "PASARELA", "FIN"], grupo.Select(g => g.Etapa));
        Assert.Equal(2, pagos.Select(p => p.Correlacion).Distinct().Count());
        Assert.Contains(pagos, p => p.Etapa == "FIN" && p.Mensaje.StartsWith("RECHAZADO"));

        //excepcion del proveedor (monto invalido) -> inicio -> error
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pasarela.CobrarAsync(-1m, "K-DEC-3"));
        Assert.Equal("ERROR", bitacora.Consultar(CategoriasBitacora.Pago).Last().Etapa);
    }

    [Fact(DisplayName = "P20 Facade: fallo en un colaborador (pasarela) → sin persistencia parcial; facade sin SQL")]
    public async Task P20_FacadeConsistenteAnteFallo()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS, configurar: s =>
            s.AddSingleton<IPasarelaPago>(new PasarelaQueFalla()));
        var p = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        var antes = await e.StocksAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.ConfirmarAsync(p.Id));

        Assert.Equal(antes, await e.StocksAsync());
        Assert.Equal(EstadoPedido.BORRADOR, (await e.PedidoAsync(p.Id)).Estado);
        Assert.Equal(0, await e.ContarAsync(db => db.Pagos.Count() + db.EventosOutbox.Count()));
        var fuente = typeof(Application.Confirmacion.ConfirmacionPedidoFacade).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(fuente, a => a.Name!.Contains("EntityFramework") || a.Name!.Contains("Sqlite"));
    }

    private sealed class PasarelaQueFalla : IPasarelaPago
    {
        public string Nombre => "FALLA";
        public Task<ResultadoCobro> CobrarAsync(decimal monto, string clave, CancellationToken ct = default) =>
            throw new InvalidOperationException("Proveedor caído (doble de prueba)");
    }

    [Fact(DisplayName = "P21 Flyweight: 1.000 filas comparten metadatos (ReferenceEquals) y se compara la memoria")]
    public async Task P21_Flyweight()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.CONSULTA);
        var productos = await e.EnAmbitoAsync(sp => sp.GetRequiredService<IConsultasOperativas>().ListarProductosAsync());
        var (filas, informe) = DemostracionFlyweight.Generar(productos, 1000);

        Assert.Equal(1000, filas.Count);
        Assert.True(ReferenceEquals(filas[0].Metadatos, filas[5].Metadatos)); //misma fila SKU P01
        Assert.True(informe.ReferenciasCompartidas);
        Assert.Equal(5, informe.InstanciasMetadatosCompartidas);
        Assert.Equal(1000, informe.InstanciasMetadatosSinCompartir);
        Assert.True(informe.BytesConFlyweight < informe.BytesSinFlyweight,
            $"con={informe.BytesConFlyweight} sin={informe.BytesSinFlyweight}");
    }

    [Fact(DisplayName = "P22 Proxy: CONSULTA/BODEGA denegados antes de Decorator/Adapter; sin cobro ni LOG de cobro")]
    public async Task P22_ProxyDeniega()
    {
        foreach (var rol in new[] { RolUsuario.CONSULTA, RolUsuario.BODEGA })
        {
            await using var e = await EntornoPrueba.CrearAsync(rol);
            var legado = e.Servicio<PasarelaLegadaSimulada>();
            var bitacora = e.Servicio<IBitacoraOperativa>();

            await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.Servicio<IPasarelaPago>().CobrarAsync(59.00m, "K-PROXY"));

            Assert.Equal(0, legado.TransaccionesProcesadas);
            Assert.Empty(bitacora.Consultar(CategoriasBitacora.Pago));                 //ni inicio ni fin
            Assert.Single(bitacora.Consultar(CategoriasBitacora.Seguridad), b => b.Etapa == "COBRO_DENEGADO");
        }

        await using var ventas = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        Assert.True((await ventas.Servicio<IPasarelaPago>().CobrarAsync(59.00m, "K-PROXY-OK")).Aprobado);
    }

    internal static async Task<int> PedidoEntregadoYDevueltoAsync(EntornoPrueba e)
    {
        var p = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        await e.ConfirmarAsync(p.Id);
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<Application.Logistica.ServicioLogistica>().DespacharAsync(p.Id));
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<Application.Logistica.ServicioLogistica>().EntregarAsync(p.Id));
        e.Reloj.AvanzarDias(5);
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<Application.Devoluciones.ServicioDevoluciones>()
            .ProcesarAsync(new(p.Id, Domain.Operaciones.MotivoDevolucion.DEFECTO, false)));
        return p.Id;
    }
}
