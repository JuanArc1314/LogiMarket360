using System.Reflection;
using LogiMarket.Application.Comun;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Eventos;
using LogiMarket.Application.Pagos;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using LogiMarket.Infrastructure.Pagos;
using LogiMarket.Tests.Soporte;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.Tests.Integracion;

/// Integridad, recuperación y arquitectura P35–P38.
public class IntegridadArquitecturaTests
{
    [Fact(DisplayName = "P35 Devolución DEFECTO de Ana: reembolso 54.00 sin flete; repetir no reintegra ni reembolsa otra vez")]
    public async Task P35_DevolucionUnica()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.ADMIN);
        var id = await ComportamentalesTests.PedidoEntregadoAsync(e, "Ana", ("KIT_TELETRABAJO", 1));
        e.Reloj.AvanzarDias(3);

        var primera = await ComportamentalesTests.DevolverAsync(e, id, MotivoDevolucion.DEFECTO, false);
        var segunda = await ComportamentalesTests.DevolverAsync(e, id, MotivoDevolucion.DEFECTO, false);

        Assert.True(primera.Aceptada);
        Assert.Equal(54.00m, primera.Reembolso);
        Assert.False(segunda.Aceptada);
        Assert.Equal("YA_DEVUELTO", segunda.Codigo);
        var s = await e.StocksAsync();
        Assert.Equal((8, 5, 20), (s["P01"], s["P02"], s["P03"])); // reintegrado UNA vez
        Assert.Equal(1, await e.ContarAsync(db => db.Devoluciones.Count()));
        Assert.Equal(3, await e.ContarAsync(db => db.MovimientosInventario.Count(m => m.Tipo == TipoMovimiento.DEVOLUCION)));
        Assert.True((await e.PedidoAsync(id)).Devuelto);
    }

    [Fact(DisplayName = "P36 Dos cobros concurrentes con stock = 1: máximo un cobro aprobado, stock no negativo")]
    public async Task P36_ConcurrenciaSinSobreventa()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.ADMIN);
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<Application.Catalogo.ServicioCatalogo>().AjustarStockAsync("P04", 1, "PRUEBA-P36"));
        var a = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("P04", 1));
        var b = await e.CrearPedidoAsync("Luis", TipoEntrega.ESTANDAR, ("P04", 1));

        var resultados = await Task.WhenAll(
            Task.Run(() => e.ConfirmarAsync(a.Id, "K-CONC-A")),
            Task.Run(() => e.ConfirmarAsync(b.Id, "K-CONC-B")));

        Assert.Equal(1, resultados.Count(r => r.Exito));
        Assert.Contains(resultados, r => r.CodigoRechazo is "STOCK_INSUFICIENTE" or "CONFLICTO_CONCURRENCIA");
        Assert.Equal(0, await e.StockAsync("P04"));
        Assert.Equal(1, await e.ContarAsync(db => db.Pagos.Count()));
        Assert.Equal(1, e.Servicio<PasarelaLegadaSimulada>().TransaccionesProcesadas);
    }

    [Fact(DisplayName = "P37 Outbox: falla el notificador, el evento queda pendiente y se recupera una sola vez")]
    public async Task P37_RecuperacionOutbox()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var config = e.Servicio<ConfiguracionDemostracion>();
        var p = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        await e.ConfirmarAsync(p.Id);

        config.ConfigurarFalloNotificador(true);
        var fallo = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());
        Assert.Equal((0, 1), (fallo.Procesados, fallo.Fallidos));
        var pendiente = await e.ContarAsync(db => db.EventosOutbox.Single());
        Assert.Null(pendiente.FechaProcesado);
        Assert.Equal(1, pendiente.Intentos);
        Assert.Contains("no disponible", pendiente.UltimoError);
        Assert.Equal(0, await e.ContarAsync(db => db.Notificaciones.Count() + db.Auditorias.Count(a => a.Accion.StartsWith("EVENTO_"))));

        config.ConfigurarFalloNotificador(false); // "reiniciar procesador"
        var recuperado = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());
        var otraVez = await e.EnAmbitoAsync(sp => sp.GetRequiredService<ProcesadorOutbox>().ProcesarPendientesAsync());

        Assert.Equal(1, recuperado.Procesados);
        Assert.Equal(0, otraVez.Procesados);
        var procesado = await e.ContarAsync(db => db.EventosOutbox.Single());
        Assert.NotNull(procesado.FechaProcesado);
        Assert.Equal(2, procesado.Intentos);
        Assert.Equal(1, await e.ContarAsync(db => db.Notificaciones.Count()));
        Assert.Equal(1, await e.ContarAsync(db => db.Pagos.Count()));          // sin cobro duplicado
        Assert.Equal(1, e.Servicio<Application.Metricas.MetricasSesion>().Instantanea().PedidosPagados);
    }

    [Fact(DisplayName = "P38 LSP: legado (Adapter) y alterno son sustituibles con el mismo contrato")]
    public async Task P38_LiskovProveedores()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        IPasarelaPago[] implementaciones = [e.Servicio<PasarelaLegadaAdapter>(), e.Servicio<PasarelaAlternaSimulada>()];
        foreach (var pasarela in implementaciones)
        {
            var r = await pasarela.CobrarAsync(59.00m, $"LSP-{pasarela.Nombre}");
            Assert.True(r.Aprobado);
            Assert.False(string.IsNullOrWhiteSpace(r.Referencia));
            Assert.Equal(pasarela.Nombre, r.Proveedor);
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pasarela.CobrarAsync(0m, "X"));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pasarela.CobrarAsync(1.001m, "X"));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => pasarela.CobrarAsync(1m, " "));
        }
        e.Servicio<ConfiguracionDemostracion>().ConfigurarRechazoCobro(true);
        foreach (var pasarela in implementaciones)
        {
            var rechazo = await pasarela.CobrarAsync(10.00m, $"LSP-R-{pasarela.Nombre}");
            Assert.False(rechazo.Aprobado);         // rechazo de negocio: se devuelve, no se lanza
            Assert.Null(rechazo.Referencia);
            Assert.False(string.IsNullOrWhiteSpace(rechazo.Motivo));
        }
    }

    [Fact(DisplayName = "P38 ISP: interfaces pequeñas; el legado no implementa reembolso; DIP: dependencias por constructor")]
    public void P38_IspYDip()
    {
        Assert.Single(typeof(IPasarelaPago).GetMethods().Where(m => !m.IsSpecialName)); // una operación (+ propiedad Nombre)
        Assert.Single(typeof(IPasarelaReembolso).GetMethods().Where(m => !m.IsSpecialName));
        Assert.False(typeof(IPasarelaReembolso).IsAssignableFrom(typeof(PasarelaLegadaAdapter)));
        Assert.False(typeof(IPasarelaReembolso).IsAssignableFrom(typeof(PasarelaAlternaSimulada)));

        // Los casos de uso dependen de abstracciones (puertos), no de repositorios EF concretos.
        var casosDeUso = typeof(Application.Confirmacion.ConfirmacionPedidoFacade).Assembly.GetTypes()
            .Where(t => t.Name.StartsWith("Servicio") || t.Name.EndsWith("Facade"));
        foreach (var tipo in casosDeUso)
            foreach (var parametro in tipo.GetConstructors().SelectMany(c => c.GetParameters()))
                Assert.False(parametro.ParameterType.Namespace?.StartsWith("LogiMarket.Infrastructure") ?? false,
                    $"{tipo.Name} depende de {parametro.ParameterType}");
    }

    [Fact(DisplayName = "P38 Arquitectura: Domain sin referencias; Application solo Domain; nada interno usa EF/SQLite/WinForms")]
    public void P38_ReglaDeDependencias()
    {
        static string[] Refs(Assembly a) => a.GetReferencedAssemblies().Select(r => r.Name!).ToArray();
        var dominio = Refs(typeof(Pedido).Assembly);
        var aplicacion = Refs(typeof(ProcesadorOutbox).Assembly);

        Assert.DoesNotContain(dominio, r => r.StartsWith("LogiMarket") || r.Contains("EntityFramework") || r.Contains("Sqlite"));
        Assert.Contains("LogiMarket.Domain", aplicacion);
        Assert.DoesNotContain(aplicacion, r => r is "LogiMarket.Infrastructure" or "LogiMarket.WinForms");
        Assert.DoesNotContain(aplicacion.Concat(dominio), r =>
            r.Contains("EntityFramework") || r.Contains("Sqlite") || r.Contains("Windows.Forms"));
    }

    [Fact(DisplayName = "Seguridad: login con hash válido, contraseña errónea rechazada y auditada")]
    public async Task Autenticacion()
    {
        await using var e = await EntornoPrueba.CrearAsync(rol: null);
        var auth = (Func<IServiceProvider, Application.Seguridad.ServicioAutenticacion>)(sp =>
            sp.GetRequiredService<Application.Seguridad.ServicioAutenticacion>());

        var mal = await e.EnAmbitoAsync(sp => auth(sp).IniciarSesionAsync("ventas", "incorrecta"));
        Assert.False(mal.Exito);
        Assert.False(e.Sesion.Autenticada);

        var bien = await e.EnAmbitoAsync(sp => auth(sp).IniciarSesionAsync("VENTAS", "Ventas123*"));
        Assert.True(bien.Exito);
        Assert.Equal(RolUsuario.VENTAS, e.Sesion.Actual!.Rol);
        Assert.Equal(1, await e.ContarAsync(db => db.Auditorias.Count(a => a.Accion == "LOGIN_FALLIDO")));

        await e.EnAmbitoAsync(sp => auth(sp).CerrarSesionAsync());
        Assert.False(e.Sesion.Autenticada);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() =>
            e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("P01", 1)));
    }
}
