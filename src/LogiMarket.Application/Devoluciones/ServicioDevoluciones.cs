using LogiMarket.Application.Comun;
using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Application.Devoluciones;

/// Caso de uso de devolución TOTAL: elige la variante por motivo y la ejecuta en una transacción. 
public sealed class ServicioDevoluciones
{
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IRepositorioPedidos _pedidos;
    private readonly IRepositorioClientes _clientes;
    private readonly IRepositorioCatalogo _catalogo;
    private readonly IRepositorioPagos _pagos;
    private readonly IReadOnlyDictionary<MotivoDevolucion, ProcesoDevolucion> _procesos;
    private readonly Autorizador _autorizador;
    private readonly IReloj _reloj;
    private readonly ServicioAuditoria _auditoria;

    public ServicioDevoluciones(IUnidadDeTrabajo unidad, IRepositorioPedidos pedidos, IRepositorioClientes clientes,
        IRepositorioCatalogo catalogo, IRepositorioPagos pagos, IEnumerable<ProcesoDevolucion> procesos,
        Autorizador autorizador, IReloj reloj, ServicioAuditoria auditoria)
    {
        _unidad = unidad;
        _pedidos = pedidos;
        _clientes = clientes;
        _catalogo = catalogo;
        _pagos = pagos;
        _procesos = procesos.ToDictionary(p => p.Motivo);
        _autorizador = autorizador;
        _reloj = reloj;
        _auditoria = auditoria;
    }

    public async Task<ResultadoDevolucion> ProcesarAsync(SolicitudDevolucion solicitud, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.ProcesarDevoluciones);
        var resultado = await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var pedido = await _pedidos.ObtenerAsync(solicitud.PedidoId, t)
                         ?? throw new RecursoNoEncontradoException("Pedido", solicitud.PedidoId);
            var pago = await _pagos.ObtenerPorPedidoAsync(pedido.Id, t)
                       ?? throw new ExcepcionAplicacion("SIN_PAGO", "El pedido no registra un cobro aprobado.");
            var contexto = new ContextoDevolucion
            {
                Pedido = pedido,
                Cliente = await _clientes.ObtenerAsync(pedido.ClienteId, t),
                Pago = pago,
                Solicitud = solicitud,
                Productos = await _catalogo.ObtenerProductosAsync(pedido.RequerimientosStock().Select(r => r.Sku), t),
                Ahora = _reloj.Ahora,
                Usuario = usuario.NombreUsuario
            };
            return await _procesos[solicitud.Motivo].ProcesarAsync(contexto, t);
        }, ct);

        if (!resultado.Aceptada)
            await _auditoria.RegistrarAsync(CategoriaAuditoria.NEGOCIO, "DEVOLUCION_RECHAZADA",
                $"Pedido {solicitud.PedidoId} ({solicitud.Motivo}): {resultado.Codigo} - {resultado.Mensaje}", ct: ct);
        return resultado;
    }
}
