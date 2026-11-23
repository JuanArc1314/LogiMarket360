using LogiMarket.Application.Comun;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Pedidos;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using LogiMarket.Infrastructure.Persistencia;
using LogiMarket.Tests.Soporte;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.Tests.Integracion;

/// Funcionalidad y reglas P01–P10 sobre SQLite real (base aislada y recién preparada).
public class FlujoVentasTests
{
    [Fact(DisplayName = "P01 Migraciones + seed desde archivo inexistente; relanzar seed no duplica")]
    public async Task P01_BaseDesdeCeroYSeedIdempotente()
    {
        await using var e = await EntornoPrueba.CrearAsync();
        Assert.True(File.Exists(e.Opciones.RutaBaseDatos));

        await e.EnAmbitoAsync(async sp =>
        {
            var db = sp.GetRequiredService<LogiMarketDbContext>();
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        });
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<SemillaDatos>().EjecutarAsync()); // segunda vez
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<SemillaDatos>().EjecutarAsync()); // tercera vez

        Assert.Equal(5, await e.ContarAsync(db => db.Productos.Count()));
        Assert.Equal(2, await e.ContarAsync(db => db.Kits.Count()));
        Assert.Equal(4, await e.ContarAsync(db => db.Usuarios.Count()));
        Assert.Equal(4, await e.ContarAsync(db => db.Usuarios.Select(u => u.Rol).Distinct().Count()));
        Assert.Equal(5, await e.ContarAsync(db => db.MovimientosInventario.Count()));
        Assert.DoesNotContain("Admin123*", await e.ContarAsync(db => db.Usuarios.Select(u => u.HashContrasena).First()));
    }

    [Fact(DisplayName = "P02 Ana VIP KIT_TELETRABAJO ×1 ESTÁNDAR → 59.00; P01=7, P02=4, P03=19; PAGADO y un pago")]
    public async Task P02_Ana()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var pedido = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        var r = await e.ConfirmarAsync(pedido.Id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(59.00m, r.Total);
        var detalle = await e.PedidoAsync(pedido.Id);
        Assert.Equal((60.00m, 6.00m, 5.00m, 59.00m), (detalle.Subtotal, detalle.Descuento, detalle.Tarifa, detalle.Total));
        Assert.Equal(EstadoPedido.PAGADO, detalle.Estado);
        var s = await e.StocksAsync();
        Assert.Equal((7, 4, 19, 4, 30), (s["P01"], s["P02"], s["P03"], s["P04"], s["P05"]));
        Assert.Equal(1, await e.ContarAsync(db => db.Pagos.Count()));
    }

    [Fact(DisplayName = "P03 Luis NORMAL 2 P04 + 2 P05 EXPRÉS → 102.00; P04=2, P05=28; un pago")]
    public async Task P03_Luis()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var pedido = await e.CrearPedidoAsync("Luis", TipoEntrega.EXPRES, ("P04", 2), ("P05", 2));
        var r = await e.ConfirmarAsync(pedido.Id);

        Assert.True(r.Exito, r.Mensaje);
        Assert.Equal(102.00m, r.Total);
        Assert.Equal(2, await e.StockAsync("P04"));
        Assert.Equal(28, await e.StockAsync("P05"));
        Assert.Equal(1, await e.ContarAsync(db => db.Pagos.Count()));
    }

    [Fact(DisplayName = "P04 Carmen MAYORISTA P03 ×10 ESTÁNDAR → 90.00; descuento 15.00; P03=10")]
    public async Task P04_Carmen()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var pedido = await e.CrearPedidoAsync("Carmen", TipoEntrega.ESTANDAR, ("P03", 10));
        var r = await e.ConfirmarAsync(pedido.Id);

        Assert.True(r.Exito, r.Mensaje);
        var d = await e.PedidoAsync(pedido.Id);
        Assert.Equal(("MAYORISTA", 15.00m, 90.00m), (d.Politica, d.Descuento, d.Total));
        Assert.Equal(10, await e.StockAsync("P03"));
    }

    [Fact(DisplayName = "P05 KIT_TELETRABAJO ×6: rechazo por P02 insuficiente; sin pago, stock ni evento de éxito")]
    public async Task P05_StockInsuficiente()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var antes = await e.StocksAsync();
        var pedido = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 6));
        var r = await e.ConfirmarAsync(pedido.Id);

        Assert.False(r.Exito);
        Assert.Equal("STOCK_INSUFICIENTE", r.CodigoRechazo);
        Assert.Contains("P02", r.Mensaje);
        Assert.Equal(antes, await e.StocksAsync());
        Assert.Equal(0, await e.ContarAsync(db => db.Pagos.Count()));
        Assert.Equal(0, await e.ContarAsync(db => db.EventosOutbox.Count()));
        Assert.Equal(EstadoPedido.BORRADOR, (await e.PedidoAsync(pedido.Id)).Estado);
    }

    [Fact(DisplayName = "P06 Error de cobro: inventario y estado previos; sin pago aprobado ni Outbox")]
    public async Task P06_CobroRechazado()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var pedido = await e.CrearPedidoAsync("Luis", TipoEntrega.EXPRES, ("P04", 2), ("P05", 2));
        e.Servicio<ConfiguracionDemostracion>().ConfigurarRechazoCobro(true);
        var antes = await e.StocksAsync();

        var r = await e.ConfirmarAsync(pedido.Id, "CLAVE-P06");

        Assert.False(r.Exito);
        Assert.Equal("COBRO_RECHAZADO", r.CodigoRechazo);
        Assert.Equal(antes, await e.StocksAsync());
        Assert.Equal(EstadoPedido.BORRADOR, (await e.PedidoAsync(pedido.Id)).Estado);
        Assert.Equal(0, await e.ContarAsync(db => db.Pagos.Count()));
        Assert.Equal(0, await e.ContarAsync(db => db.EventosOutbox.Count()));
        Assert.Equal(0, await e.ContarAsync(db => db.MovimientosInventario.Count(m => m.Tipo == Domain.Operaciones.TipoMovimiento.VENTA)));

        // Al corregir la causa, la misma operación se completa.
        e.Servicio<ConfiguracionDemostracion>().ConfigurarRechazoCobro(false);
        Assert.True((await e.ConfirmarAsync(pedido.Id, "CLAVE-P06")).Exito);
    }

    [Fact(DisplayName = "P07 Misma clave de idempotencia: un pago, un movimiento por SKU y resultado original")]
    public async Task P07_Idempotencia()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var pedido = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        var primero = await e.ConfirmarAsync(pedido.Id, "CLAVE-UNICA-P07");
        var segundo = await e.ConfirmarAsync(pedido.Id, "CLAVE-UNICA-P07");
        var tercero = await e.ConfirmarAsync(pedido.Id, "CLAVE-UNICA-P07");

        Assert.True(primero.Exito && !primero.EsRepeticion);
        Assert.True(segundo.Exito && segundo.EsRepeticion);
        Assert.Equal(primero.ReferenciaPago, segundo.ReferenciaPago);
        Assert.Equal(primero.ReferenciaPago, tercero.ReferenciaPago);
        Assert.Equal(1, await e.ContarAsync(db => db.Pagos.Count()));
        Assert.Equal(3, await e.ContarAsync(db => db.MovimientosInventario.Count(m => m.Referencia == pedido.Codigo)));
        Assert.Equal(1, await e.ContarAsync(db => db.EventosOutbox.Count()));
        Assert.Equal(7, await e.StockAsync("P01"));

        // Otra clave sobre un pedido ya pagado tampoco cobra de nuevo.
        var otraClave = await e.ConfirmarAsync(pedido.Id, "OTRA-CLAVE");
        Assert.False(otraClave.Exito);
        Assert.Equal(1, await e.ContarAsync(db => db.Pagos.Count()));
    }

    [Fact(DisplayName = "P08 Pedido PAGADO: cancelar y modificar (cantidad/precio) denegados; datos e historial intactos")]
    public async Task P08_PagadoNoSeModifica()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var pedido = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        await e.ConfirmarAsync(pedido.Id);
        var antes = await e.PedidoAsync(pedido.Id);

        var cancelar = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioPedidos>().CancelarAsync(pedido.Id, "prueba")));
        Assert.Equal("TRANSICION_INVALIDA", cancelar.Codigo);
        var modificar = await Assert.ThrowsAsync<ExcepcionDominio>(() =>
            e.EnAmbitoAsync(sp => sp.GetRequiredService<ServicioPedidos>().CambiarCantidadAsync(pedido.Id, "KIT_TELETRABAJO", 3)));
        Assert.Equal("PEDIDO_NO_EDITABLE", modificar.Codigo);

        // Cambiar el precio del catálogo no altera el pedido pagado (instantánea).
        e.IniciarSesion(RolUsuario.ADMIN);
        await e.EnAmbitoAsync(sp => sp.GetRequiredService<Application.Catalogo.ServicioCatalogo>()
            .ActualizarProductoAsync(new("P01", "Mouse USB", "Periféricos", "", 99.00m, 0.20m, 0)));

        var despues = await e.PedidoAsync(pedido.Id);
        Assert.Equal(antes.Total, despues.Total);
        Assert.Equal(60.00m, despues.Lineas[0].PrecioUnitario);
        Assert.Equal(antes.Historial.Count, despues.Historial.Count);
        Assert.Equal(EstadoPedido.PAGADO, despues.Estado);
    }

    [Fact(DisplayName = "P09 BODEGA despacha y entrega; despachar sin pago se rechaza sin cambios")]
    public async Task P09_DespachoYEntrega()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS);
        var pagado = await e.CrearPedidoAsync("Luis", TipoEntrega.EXPRES, ("P04", 2), ("P05", 2));
        var sinPagar = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("P01", 1));
        await e.ConfirmarAsync(pagado.Id);

        e.IniciarSesion(RolUsuario.BODEGA);
        var logistica = (Func<IServiceProvider, Application.Logistica.ServicioLogistica>)(sp => sp.GetRequiredService<Application.Logistica.ServicioLogistica>());

        var rechazo = await Assert.ThrowsAsync<ExcepcionDominio>(() => e.EnAmbitoAsync(sp => logistica(sp).DespacharAsync(sinPagar.Id)));
        Assert.Equal("TRANSICION_INVALIDA", rechazo.Codigo);
        Assert.Equal(EstadoPedido.BORRADOR, (await e.PedidoAsync(sinPagar.Id)).Estado);
        Assert.Equal(0, await e.ContarAsync(db => db.Despachos.Count(d => d.PedidoId == sinPagar.Id)));

        var despacho = await e.EnAmbitoAsync(sp => logistica(sp).DespacharAsync(pagado.Id));
        Assert.True(despacho.Exito);
        Assert.Contains("EXP-", despacho.Mensaje);
        await e.EnAmbitoAsync(sp => logistica(sp).EntregarAsync(pagado.Id));
        var d = await e.PedidoAsync(pagado.Id);
        Assert.Equal(EstadoPedido.ENTREGADO, d.Estado);
        Assert.Equal(e.Reloj.Ahora.Date.AddDays(1), d.Despacho!.FechaEstimada);

        e.IniciarSesion(RolUsuario.VENTAS); // VENTAS no despacha
        var otro = await e.CrearPedidoAsync("Carmen", TipoEntrega.ESTANDAR, ("P05", 1));
        await e.ConfirmarAsync(otro.Id);
        await Assert.ThrowsAsync<AccesoDenegadoException>(() => e.EnAmbitoAsync(sp => logistica(sp).DespacharAsync(otro.Id)));
    }

    [Fact(DisplayName = "P10 Cerrar y reabrir: pedido, stock, pago y auditoría persisten en SQLite")]
    public async Task P10_PersistenciaTrasReinicio()
    {
        var directorio = Path.Combine(Path.GetTempPath(), "logimarket-tests", Guid.NewGuid().ToString("N"));
        int pedidoId;
        string? referencia;
        await using (var e1 = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS, directorio))
        {
            var pedido = await e1.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
            var r = await e1.ConfirmarAsync(pedido.Id);
            pedidoId = pedido.Id;
            referencia = r.ReferenciaPago;
            await e1.EnAmbitoAsync(sp => sp.GetRequiredService<Application.Eventos.ProcesadorOutbox>().ProcesarPendientesAsync());
            await e1.Proveedor.DisposeAsync(); // "cerrar" la aplicación sin borrar el archivo
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.CreateDirectory(directorio + "-copia");
            foreach (var f in Directory.GetFiles(directorio)) File.Copy(f, Path.Combine(directorio + "-copia", Path.GetFileName(f)));
        }

        await using var e2 = await EntornoPrueba.CrearAsync(RolUsuario.CONSULTA, directorio + "-copia");
        var d = await e2.PedidoAsync(pedidoId);
        Assert.Equal((EstadoPedido.PAGADO, 59.00m), (d.Estado, d.Total));
        Assert.Equal(referencia, d.Pago!.Referencia);
        Assert.Equal(7, await e2.StockAsync("P01"));
        Assert.Equal(1, await e2.ContarAsync(db => db.Auditorias.Count(a => a.Accion == "EVENTO_PedidoPagado")));
        Assert.Equal(5, await e2.ContarAsync(db => db.Productos.Count())); // seed no duplicó al reabrir
    }
}
