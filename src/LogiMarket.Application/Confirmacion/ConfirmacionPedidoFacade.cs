using LogiMarket.Application.Comun;
using LogiMarket.Application.Eventos;
using LogiMarket.Application.Inventario;
using LogiMarket.Application.Metricas;
using LogiMarket.Application.Pagos;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Pedidos.Validacion;
using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Confirmacion;

public sealed record ResultadoConfirmacion(
    bool Exito, bool EsRepeticion, int PedidoId, string CodigoPedido, EstadoPedido? Estado, decimal Total,
    string? ReferenciaPago, string? CodigoRechazo, string Mensaje, IReadOnlyList<string> EslabonesEvaluados)
{
    internal static ResultadoConfirmacion Rechazo(int pedidoId, string codigo, string mensaje,
        IReadOnlyList<string>? eslabones = null, string codigoPedido = "", EstadoPedido? estado = null) =>
        new(false, false, pedidoId, codigoPedido, estado, 0m, null, codigo, mensaje, eslabones ?? []);
}

//colaboradores de persistencia agrupados (evita un constructor con demasiados parametros)
public sealed record RepositoriosConfirmacion(
    IRepositorioPedidos Pedidos, IRepositorioClientes Clientes, IRepositorioCatalogo Catalogo,
    IRepositorioPagos Pagos, IRepositorioOutbox Outbox);

//patron facade
//una unica operacion de confirmacion que coordina autorizacion, validacion (chain)
//valoracion (strategy + abstract factory), cobro simulado (proxy -> decorator -> adapter)
//stock, persistencia y outbox en una transaccion
//no contiene SQL ni reglas de descuento: delega
public sealed class ConfirmacionPedidoFacade
{
    private readonly IUnidadDeTrabajo _unidad;
    private readonly RepositoriosConfirmacion _repos;
    private readonly FabricaCadenaValidacion _cadenas;
    private readonly ValoradorPedidos _valorador;
    private readonly IPasarelaPago _pasarela;
    private readonly IPasarelaReembolso _reembolsos;
    private readonly ServicioInventario _inventario;
    private readonly SerializadorEventos _serializador;
    private readonly ISesionUsuario _sesion;
    private readonly IReloj _reloj;
    private readonly ServicioAuditoria _auditoria;
    private readonly MetricasSesion _metricas;
    private readonly IBitacoraOperativa _bitacora;

    public ConfirmacionPedidoFacade(IUnidadDeTrabajo unidad, RepositoriosConfirmacion repos, FabricaCadenaValidacion cadenas,
        ValoradorPedidos valorador, IPasarelaPago pasarela, IPasarelaReembolso reembolsos, ServicioInventario inventario,
        SerializadorEventos serializador, ISesionUsuario sesion, IReloj reloj, ServicioAuditoria auditoria,
        MetricasSesion metricas, IBitacoraOperativa bitacora)
    {
        _unidad = unidad;
        _repos = repos;
        _cadenas = cadenas;
        _valorador = valorador;
        _pasarela = pasarela;
        _reembolsos = reembolsos;
        _inventario = inventario;
        _serializador = serializador;
        _sesion = sesion;
        _reloj = reloj;
        _auditoria = auditoria;
        _metricas = metricas;
        _bitacora = bitacora;
    }

    public async Task<ResultadoConfirmacion> ConfirmarAsync(int pedidoId, string claveIdempotencia, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claveIdempotencia))
            throw new ArgumentException("La confirmación requiere una clave de idempotencia.", nameof(claveIdempotencia));

        ResultadoCobro? cobroAprobado = null;
        decimal montoCobrado = 0m;
        try
        {
            return await _unidad.EjecutarEnTransaccionAsync(async t =>
            {
                //1) idempotencia: misma clave -> se devuelve el resultado original sin volver a cobrar
                var previo = await _repos.Pagos.ObtenerPorClaveAsync(claveIdempotencia, t);
                if (previo is not null)
                    return await ResultadoRepetidoAsync(previo, pedidoId, t);

                var pedido = await _repos.Pedidos.ObtenerAsync(pedidoId, t)
                             ?? throw new RecursoNoEncontradoException("Pedido", pedidoId);
                var cliente = await _repos.Clientes.ObtenerAsync(pedido.ClienteId, t);
                var productos = await _repos.Catalogo.ObtenerProductosAsync(pedido.RequerimientosStock().Select(r => r.Sku), t);

                //2) chain of responsibility: cliente -> lineas -> stock -> autorizacion/condiciones
                var validacion = _cadenas.CrearParaConfirmacion().Validar(new ContextoValidacionPedido
                {
                    Pedido = pedido, Cliente = cliente, Productos = productos, Usuario = _sesion.Actual
                });
                if (!validacion.EsValido)
                    return ResultadoConfirmacion.Rechazo(pedidoId, validacion.Rechazo!.Codigo, validacion.Rechazo.ToString(),
                        validacion.EslabonesEvaluados, pedido.Codigo, pedido.Estado);

                //3) valoracion y transicion (state) dentro del dominio
                var ahora = _reloj.Ahora;
                var usuario = _sesion.NombreUsuario;
                _valorador.Aplicar(pedido);
                if (pedido.Estado == EstadoPedido.BORRADOR) pedido.Validar(ahora, usuario);

                //4) cobro simulado protegido: proxy -> decorator -> adapter
                var cobro = await _pasarela.CobrarAsync(pedido.Total, claveIdempotencia, t);
                if (!cobro.Aprobado)
                    throw new CobroRechazadoException(cobro.Motivo ?? "Cobro rechazado por el proveedor.");
                cobroAprobado = cobro;
                montoCobrado = pedido.Total;

                //5) stock, pedido, pago y outbox en la misma transaccion
                _inventario.DescontarPorVenta(pedido, productos, ahora, usuario);
                pedido.RegistrarPago(claveIdempotencia, ahora, usuario);
                _repos.Pagos.Agregar(new Pago(pedido.Id, cobro.Referencia!, claveIdempotencia, pedido.Total,
                    cobro.Proveedor, ahora, usuario));
                _repos.Outbox.Agregar(_serializador.CrearRegistro(new PedidoPagado(Guid.NewGuid(), ahora, pedido.Id,
                    pedido.Codigo, pedido.ClienteNombre, cliente!.Email, cliente.Telefono, pedido.Total, cobro.Referencia!)));

                return new ResultadoConfirmacion(true, false, pedido.Id, pedido.Codigo, pedido.Estado, pedido.Total,
                    cobro.Referencia, null, $"Pedido {pedido.Codigo} PAGADO por {Dinero.Formatear(pedido.Total)}.",
                    validacion.EslabonesEvaluados);
            }, ct);
        }
        catch (CobroRechazadoException ex)
        {
            _metricas.RegistrarCobroRechazado();
            await _auditoria.RegistrarAsync(CategoriaAuditoria.NEGOCIO, "COBRO_RECHAZADO",
                $"Pedido {pedidoId}: {ex.Message}", claveIdempotencia, ct: ct);
            return ResultadoConfirmacion.Rechazo(pedidoId, "COBRO_RECHAZADO", ex.Message);
        }
        catch (AccesoDenegadoException ex)
        {
            await _auditoria.RegistrarAsync(CategoriaAuditoria.SEGURIDAD, "COBRO_DENEGADO",
                $"Pedido {pedidoId}: {ex.Message}", claveIdempotencia, ct: ct);
            return ResultadoConfirmacion.Rechazo(pedidoId, "NO_AUTORIZADO", ex.Message);
        }
        catch (Exception ex) when (ex is ViolacionUnicidadException or ConflictoConcurrenciaException or ExcepcionDominio)
        {
            //la transaccion ya se revirtio
            //si hubo cobro aprobado, se compensa (anulacion simulada)
            if (cobroAprobado is not null)
                await CompensarAsync(cobroAprobado, montoCobrado, claveIdempotencia, ct);

            //carrera con la misma clave: el otro intento ya confirmo -> se devuelve ese resultado
            if (ex is ViolacionUnicidadException)
            {
                var original = await _repos.Pagos.ObtenerPorClaveAsync(claveIdempotencia, ct);
                if (original is not null) return await ResultadoRepetidoAsync(original, pedidoId, ct);
            }
            var codigo = ex switch
            {
                ExcepcionDominio d => d.Codigo,
                ExcepcionAplicacion a => a.Codigo,
                _ => "ERROR"
            };
            return ResultadoConfirmacion.Rechazo(pedidoId, codigo, ex.Message);
        }
        catch (Exception ex) when (cobroAprobado is not null)
        {
            //error imprevisto despues de un cobro aprobado: la transaccion ya se revirtio
            //se anula el cobro para que el cliente no quede cobrado sin un pedido pagado
            await CompensarAsync(cobroAprobado, montoCobrado, claveIdempotencia, CancellationToken.None);
            await _auditoria.RegistrarAsync(CategoriaAuditoria.NEGOCIO, "COBRO_COMPENSADO",
                $"Pedido {pedidoId}: {ex.Message}", claveIdempotencia, ct: CancellationToken.None);
            return ResultadoConfirmacion.Rechazo(pedidoId, "ERROR_CONFIRMACION",
                "No se pudo completar la confirmación. El cobro aprobado se anuló y el pedido no cambió.");
        }
    }

    private async Task<ResultadoConfirmacion> ResultadoRepetidoAsync(Pago pago, int pedidoId, CancellationToken ct)
    {
        if (pago.PedidoId != pedidoId)
            return ResultadoConfirmacion.Rechazo(pedidoId, "CLAVE_REUTILIZADA",
                "La clave de idempotencia ya se usó para otro pedido.");
        var pedido = await _repos.Pedidos.ObtenerAsync(pedidoId, ct);
        return new ResultadoConfirmacion(true, true, pedidoId, pedido?.Codigo ?? "", pedido?.Estado, pago.Monto,
            pago.Referencia, null, $"Confirmación repetida: se devuelve el resultado original (ref. {pago.Referencia}). No se cobró de nuevo.", []);
    }

    private async Task CompensarAsync(ResultadoCobro cobro, decimal monto, string clave, CancellationToken ct)
    {
        var reverso = await _reembolsos.ReembolsarAsync(cobro.Referencia!, monto, clave + "-ANULACION", ct);
        _bitacora.Registrar(CategoriasBitacora.Pago, "COMPENSACION",
            $"Anulación de {cobro.Referencia}: {(reverso.Aprobado ? "OK " + reverso.Referencia : reverso.Motivo)}");
    }

    private sealed class CobroRechazadoException(string mensaje) : Exception(mensaje);
}
