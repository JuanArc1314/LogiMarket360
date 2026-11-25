using LogiMarket.Application.Comun;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;
using LogiMarket.Tests.Soporte;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.Tests.Integracion;

//prueba de regresion de la compensacion del cobro (recomendacion 1 del informe)
//antes solo se anulaba el cobro ante errores de unicidad, concurrencia o reglas de dominio
public class CompensacionTests
{
    [Fact(DisplayName = "R04 Facade: un error imprevisto tras un cobro aprobado anula el cobro y no deja cambios")]
    public async Task R04_ErrorTrasCobroAprobadoSeCompensa()
    {
        await using var e = await EntornoPrueba.CrearAsync(RolUsuario.VENTAS, configurar: s =>
            s.AddSingleton<IRepositorioOutbox>(new OutboxQueFalla()));
        var p = await e.CrearPedidoAsync("Ana", TipoEntrega.ESTANDAR, ("KIT_TELETRABAJO", 1));
        var antes = await e.StocksAsync();

        var r = await e.ConfirmarAsync(p.Id);

        Assert.False(r.Exito);
        Assert.Equal("ERROR_CONFIRMACION", r.CodigoRechazo);
        Assert.Equal(antes, await e.StocksAsync());
        Assert.Equal(EstadoPedido.BORRADOR, (await e.PedidoAsync(p.Id)).Estado);
        Assert.Equal(0, await e.ContarAsync(db => db.Pagos.Count()));

        var bitacora = e.Servicio<IBitacoraOperativa>().Consultar(CategoriasBitacora.Pago);
        Assert.Contains(bitacora, b => b.Etapa == "REEMBOLSO");
        Assert.Contains(bitacora, b => b.Etapa == "COMPENSACION" && b.Mensaje.Contains("OK"));
        Assert.Equal(1, await e.ContarAsync(db => db.Auditorias.Count(a => a.Accion == "COBRO_COMPENSADO")));
    }

    //el outbox falla justo despues del cobro aprobado, dentro de la transaccion
    private sealed class OutboxQueFalla : IRepositorioOutbox
    {
        public Task<IReadOnlyList<Guid>> ObtenerIdsPendientesAsync(int maximo, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<EventoOutbox?> ObtenerAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<EventoOutbox?>(null);

        public void Agregar(EventoOutbox evento) =>
            throw new InvalidOperationException("Outbox no disponible (doble de prueba)");
    }
}
