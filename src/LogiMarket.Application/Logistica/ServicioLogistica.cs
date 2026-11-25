using LogiMarket.Application.Comun;
using LogiMarket.Application.Eventos;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Application.Logistica;

//despacho y entrega (BODEGA/ADMIN)
//la guia, el plazo y la etiqueta provienen de la fabrica abstracta de la familia del pedido
//(estandar/expres) sin condicionales en el caso de uso
public sealed class ServicioLogistica
{
    private readonly IUnidadDeTrabajo _unidad;
    private readonly IRepositorioPedidos _pedidos;
    private readonly IRepositorioClientes _clientes;
    private readonly IRepositorioDespachos _despachos;
    private readonly IRepositorioOutbox _outbox;
    private readonly SelectorFabricaEntrega _fabricas;
    private readonly SerializadorEventos _serializador;
    private readonly Autorizador _autorizador;
    private readonly IReloj _reloj;

    public ServicioLogistica(IUnidadDeTrabajo unidad, IRepositorioPedidos pedidos, IRepositorioClientes clientes,
        IRepositorioDespachos despachos, IRepositorioOutbox outbox, SelectorFabricaEntrega fabricas,
        SerializadorEventos serializador, Autorizador autorizador, IReloj reloj)
    {
        _unidad = unidad;
        _pedidos = pedidos;
        _clientes = clientes;
        _despachos = despachos;
        _outbox = outbox;
        _fabricas = fabricas;
        _serializador = serializador;
        _autorizador = autorizador;
        _reloj = reloj;
    }

    public async Task<ResultadoOperacion> DespacharAsync(int pedidoId, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.DespacharPedidos);
        var guia = await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var pedido = await _pedidos.ObtenerAsync(pedidoId, t) ?? throw new RecursoNoEncontradoException("Pedido", pedidoId);
            var ahora = _reloj.Ahora;
            pedido.Despachar(ahora, usuario.NombreUsuario); //state: solo desde PAGADO

            var fabrica = _fabricas.Para(pedido.Entrega);
            var generada = fabrica.CrearGeneradorGuia()
                .Generar(pedido.Codigo, pedido.ClienteNombre, pedido.PesoTotalKg, ahora, fabrica.CrearCalculadorPlazo());
            _despachos.Agregar(new Despacho(pedido.Id, generada.Numero, generada.Familia, generada.Etiqueta, ahora,
                generada.FechaEstimada, usuario.NombreUsuario));

            var cliente = await _clientes.ObtenerAsync(pedido.ClienteId, t);
            _outbox.Agregar(_serializador.CrearRegistro(new DespachoRegistrado(Guid.NewGuid(), ahora, pedido.Id, pedido.Codigo,
                pedido.ClienteNombre, cliente?.Email ?? "", cliente?.Telefono ?? "", generada.Numero, generada.FechaEstimada)));
            return generada;
        }, ct);
        return ResultadoOperacion.Ok($"Despachado con guía {guia.Numero}. {guia.Etiqueta}", guia);
    }

    public async Task<ResultadoOperacion> EntregarAsync(int pedidoId, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.EntregarPedidos);
        var codigo = await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var pedido = await _pedidos.ObtenerAsync(pedidoId, t) ?? throw new RecursoNoEncontradoException("Pedido", pedidoId);
            var ahora = _reloj.Ahora;
            pedido.Entregar(ahora, usuario.NombreUsuario); //state: solo desde DESPACHADO
            var despacho = await _despachos.ObtenerPorPedidoAsync(pedidoId, t)
                           ?? throw new ExcepcionAplicacion("SIN_DESPACHO", "El pedido no tiene despacho registrado.");
            despacho.RegistrarEntrega(ahora);
            return pedido.Codigo;
        }, ct);
        return ResultadoOperacion.Ok($"Pedido {codigo} ENTREGADO.");
    }
}
