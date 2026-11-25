using LogiMarket.Application.Comun;
using LogiMarket.Application.Pedidos.Validacion;
using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Pedidos;

public sealed record LineaBorradorDto(string Sku, int Cantidad);

public sealed record BorradorDto(int? PedidoId, int ClienteId, TipoEntrega Entrega, IReadOnlyList<LineaBorradorDto> Lineas);

public sealed record PedidoGuardadoDto(int Id, string Codigo, EstadoPedido Estado, Valoracion Valoracion);

//casos de uso de borradores: crear/editar (builder), validar (chain), cancelar (state)
//clonar (prototype)
public sealed class ServicioPedidos
{
    private readonly IRepositorioPedidos _pedidos;
    private readonly IRepositorioClientes _clientes;
    private readonly IRepositorioCatalogo _catalogo;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly Autorizador _autorizador;
    private readonly IReloj _reloj;
    private readonly ValoradorPedidos _valorador;
    private readonly FabricaCadenaValidacion _cadenas;
    private readonly ServicioAuditoria _auditoria;

    public ServicioPedidos(IRepositorioPedidos pedidos, IRepositorioClientes clientes, IRepositorioCatalogo catalogo,
        IUnidadDeTrabajo unidad, Autorizador autorizador, IReloj reloj, ValoradorPedidos valorador,
        FabricaCadenaValidacion cadenas, ServicioAuditoria auditoria)
    {
        _pedidos = pedidos;
        _clientes = clientes;
        _catalogo = catalogo;
        _unidad = unidad;
        _autorizador = autorizador;
        _reloj = reloj;
        _valorador = valorador;
        _cadenas = cadenas;
        _auditoria = auditoria;
    }

    //crea un editor en memoria con historial deshacer/rehacer (no persiste hasta guardar)
    public async Task<EditorBorrador> AbrirEditorAsync(int clienteId, int? pedidoId = null, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarPedidos);
        var cliente = await _clientes.ObtenerAsync(clienteId, ct) ?? throw new RecursoNoEncontradoException("Cliente", clienteId);
        Pedido? pedido = null;
        if (pedidoId is { } id)
        {
            pedido = await _pedidos.ObtenerAsync(id, ct) ?? throw new RecursoNoEncontradoException("Pedido", id);
            pedido.EstadoActual.ExigirEdicion();
        }
        return new EditorBorrador(cliente, pedido, _valorador, _reloj);
    }

    public async Task<Domain.Clientes.Cliente> ObtenerClienteAsync(int clienteId, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarPedidos);
        return await _clientes.ObtenerAsync(clienteId, ct) ?? throw new RecursoNoEncontradoException("Cliente", clienteId);
    }

    public async Task<IElementoCatalogo> ObtenerElementoAsync(string sku, CancellationToken ct = default) =>
        await _catalogo.ObtenerElementoAsync(sku, ct) ?? throw new RecursoNoEncontradoException("SKU", sku);

    //guarda el borrador: re-toma la instantanea de precios del catalogo y valora el pedido
    public async Task<PedidoGuardadoDto> GuardarBorradorAsync(BorradorDto dto, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.GestionarPedidos);
        if (dto.Lineas.Count == 0)
            throw new ExcepcionDominio("PEDIDO_SIN_LINEAS", "No se puede guardar un borrador sin líneas.");

        var resultado = await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var cliente = await _clientes.ObtenerAsync(dto.ClienteId, t)
                          ?? throw new RecursoNoEncontradoException("Cliente", dto.ClienteId);
            var lineas = new List<(IElementoCatalogo, int)>();
            foreach (var linea in dto.Lineas)
                lineas.Add((await ObtenerElementoAsync(linea.Sku, t), linea.Cantidad));

            Pedido pedido;
            if (dto.PedidoId is { } id)
            {
                pedido = await _pedidos.ObtenerAsync(id, t) ?? throw new RecursoNoEncontradoException("Pedido", id);
                pedido.CambiarCliente(cliente);
                pedido.CambiarEntrega(dto.Entrega);
                pedido.ReemplazarLineas(lineas);
            }
            else
            {
                var builder = new PedidoBuilder().ParaCliente(cliente).ConEntrega(dto.Entrega)
                    .RegistradoPor(usuario.NombreUsuario, _reloj.Ahora);
                foreach (var (elemento, cantidad) in lineas) builder.AgregarElemento(elemento, cantidad);
                pedido = builder.Construir();
                _pedidos.Agregar(pedido);
                await _unidad.GuardarCambiosAsync(t);          //obtiene id
                pedido.AsignarCodigo($"PED-{pedido.Id:0000}");
            }
            var valoracion = _valorador.Aplicar(pedido);
            return (pedido, valoracion);
        }, ct);

        return new PedidoGuardadoDto(resultado.pedido.Id, resultado.pedido.Codigo, resultado.pedido.Estado, resultado.valoracion);
    }

    //BORRADOR -> VALIDADO si la cadena (cliente -> lineas -> stock) lo aprueba
    public async Task<ResultadoValidacion> ValidarAsync(int pedidoId, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.GestionarPedidos);
        return await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var pedido = await _pedidos.ObtenerAsync(pedidoId, t) ?? throw new RecursoNoEncontradoException("Pedido", pedidoId);
            var cliente = await _clientes.ObtenerAsync(pedido.ClienteId, t);
            var productos = await _catalogo.ObtenerProductosAsync(pedido.RequerimientosStock().Select(r => r.Sku), t);
            var resultado = _cadenas.CrearParaValidacion().Validar(new ContextoValidacionPedido
            {
                Pedido = pedido, Cliente = cliente, Productos = productos, Usuario = usuario
            });
            if (resultado.EsValido)
            {
                _valorador.Aplicar(pedido);
                pedido.Validar(_reloj.Ahora, usuario.NombreUsuario);
            }
            return resultado;
        }, ct);
    }

    public async Task<ResultadoOperacion> CancelarAsync(int pedidoId, string motivo, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.GestionarPedidos);
        var codigo = await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var pedido = await _pedidos.ObtenerAsync(pedidoId, t) ?? throw new RecursoNoEncontradoException("Pedido", pedidoId);
            pedido.Cancelar(_reloj.Ahora, usuario.NombreUsuario, string.IsNullOrWhiteSpace(motivo) ? "Sin motivo" : motivo);
            return pedido.Codigo;
        }, ct);
        await _auditoria.RegistrarAsync(CategoriaAuditoria.NEGOCIO, "PEDIDO_CANCELADO", $"{codigo}: {motivo}", codigo, ct: ct);
        return ResultadoOperacion.Ok($"Pedido {codigo} CANCELADO.");
    }

    //prototype: crea un nuevo borrador a partir de otro pedido (plantilla de carrito)
    public async Task<PedidoGuardadoDto> ClonarComoBorradorAsync(int pedidoId, CancellationToken ct = default)
    {
        var usuario = _autorizador.Exigir(Permiso.GestionarPedidos);
        var resultado = await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var origen = await _pedidos.ObtenerAsync(pedidoId, t) ?? throw new RecursoNoEncontradoException("Pedido", pedidoId);
            var copia = origen.ClonarComoBorrador(_reloj.Ahora, usuario.NombreUsuario);
            _pedidos.Agregar(copia);
            await _unidad.GuardarCambiosAsync(t);
            copia.AsignarCodigo($"PED-{copia.Id:0000}");
            return (copia, _valorador.Aplicar(copia));
        }, ct);
        return new PedidoGuardadoDto(resultado.copia.Id, resultado.copia.Codigo, resultado.copia.Estado, resultado.Item2);
    }

    //intento explicito de modificar un pedido (lo rechaza el state si no es BORRADOR)
    public async Task<ResultadoOperacion> CambiarCantidadAsync(int pedidoId, string sku, int cantidad, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarPedidos);
        await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            var pedido = await _pedidos.ObtenerAsync(pedidoId, t) ?? throw new RecursoNoEncontradoException("Pedido", pedidoId);
            pedido.CambiarCantidad(sku, cantidad);
            _valorador.Aplicar(pedido);
            return true;
        }, ct);
        return ResultadoOperacion.Ok("Cantidad actualizada.");
    }
}
