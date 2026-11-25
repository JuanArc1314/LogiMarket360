using LogiMarket.Application.Comun;
using LogiMarket.Application.Confirmacion;
using LogiMarket.Application.Devoluciones;
using LogiMarket.Application.Logistica;
using LogiMarket.Application.Metricas;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Application.Comandos;

//patron command - comando
//encapsula una solicitud con su receptor y su entrada
public interface IComandoOperacion
{
    string Nombre { get; }
    string Entrada { get; }
    Task<ResultadoOperacion> EjecutarAsync(CancellationToken ct = default);
}

public sealed class ConfirmarPedidoCommand(ConfirmacionPedidoFacade receptor, int pedidoId, string clave) : IComandoOperacion
{
    public string Nombre => "ConfirmarPedido";
    public string Entrada => $"pedido={pedidoId}; clave={clave}";

    public async Task<ResultadoOperacion> EjecutarAsync(CancellationToken ct = default)
    {
        var r = await receptor.ConfirmarAsync(pedidoId, clave, ct);
        return r.Exito ? ResultadoOperacion.Ok(r.Mensaje, r) : new ResultadoOperacion(false, r.Mensaje, r.CodigoRechazo, r);
    }
}

public sealed class CancelarPedidoCommand(ServicioPedidos receptor, int pedidoId, string motivo) : IComandoOperacion
{
    public string Nombre => "CancelarPedido";
    public string Entrada => $"pedido={pedidoId}; motivo={motivo}";
    public Task<ResultadoOperacion> EjecutarAsync(CancellationToken ct = default) => receptor.CancelarAsync(pedidoId, motivo, ct);
}

public sealed class DespacharPedidoCommand(ServicioLogistica receptor, int pedidoId) : IComandoOperacion
{
    public string Nombre => "DespacharPedido";
    public string Entrada => $"pedido={pedidoId}";
    public Task<ResultadoOperacion> EjecutarAsync(CancellationToken ct = default) => receptor.DespacharAsync(pedidoId, ct);
}

public sealed class EntregarPedidoCommand(ServicioLogistica receptor, int pedidoId) : IComandoOperacion
{
    public string Nombre => "EntregarPedido";
    public string Entrada => $"pedido={pedidoId}";
    public Task<ResultadoOperacion> EjecutarAsync(CancellationToken ct = default) => receptor.EntregarAsync(pedidoId, ct);
}

public sealed class ProcesarDevolucionCommand(ServicioDevoluciones receptor, SolicitudDevolucion solicitud) : IComandoOperacion
{
    public string Nombre => "ProcesarDevolucion";
    public string Entrada => $"pedido={solicitud.PedidoId}; motivo={solicitud.Motivo}; sinUso={solicitud.ProductoSinUso}";

    public async Task<ResultadoOperacion> EjecutarAsync(CancellationToken ct = default)
    {
        var r = await receptor.ProcesarAsync(solicitud, ct);
        return r.Aceptada ? ResultadoOperacion.Ok(r.Mensaje, r) : new ResultadoOperacion(false, r.Mensaje, r.Codigo, r);
    }
}

//crea comandos con sus receptores (la UI no conoce los casos de uso concretos)
public sealed class FabricaComandos(
    ConfirmacionPedidoFacade confirmacion, ServicioPedidos pedidos, ServicioLogistica logistica, ServicioDevoluciones devoluciones)
{
    public IComandoOperacion Confirmar(int pedidoId, string clave) => new ConfirmarPedidoCommand(confirmacion, pedidoId, clave);
    public IComandoOperacion Cancelar(int pedidoId, string motivo) => new CancelarPedidoCommand(pedidos, pedidoId, motivo);
    public IComandoOperacion Despachar(int pedidoId) => new DespacharPedidoCommand(logistica, pedidoId);
    public IComandoOperacion Entregar(int pedidoId) => new EntregarPedidoCommand(logistica, pedidoId);
    public IComandoOperacion Devolver(SolicitudDevolucion solicitud) => new ProcesarDevolucionCommand(devoluciones, solicitud);
}

public sealed record RegistroComando(DateTime Fecha, string Usuario, string Comando, string Entrada, bool Exito, string Resultado);

//patron command - invocador
//ejecuta cualquier comando y registra comando, entrada y resultado
//la misma accion se dispara desde un boton o desde una prueba sin duplicar logica
public sealed class InvocadorComandos
{
    private readonly List<RegistroComando> _historial = new();
    private readonly object _candado = new();
    private readonly ISesionUsuario _sesion;
    private readonly IReloj _reloj;
    private readonly IBitacoraOperativa _bitacora;
    private readonly MetricasSesion _metricas;

    public InvocadorComandos(ISesionUsuario sesion, IReloj reloj, IBitacoraOperativa bitacora, MetricasSesion metricas)
    {
        _sesion = sesion;
        _reloj = reloj;
        _bitacora = bitacora;
        _metricas = metricas;
    }

    public IReadOnlyList<RegistroComando> Historial
    {
        get { lock (_candado) return _historial.ToList(); }
    }

    public async Task<ResultadoOperacion> EjecutarAsync(IComandoOperacion comando, CancellationToken ct = default)
    {
        ResultadoOperacion resultado;
        try
        {
            resultado = await comando.EjecutarAsync(ct);
        }
        catch (ExcepcionDominio ex)
        {
            resultado = ResultadoOperacion.Fallo(ex.Codigo, ex.Message);
        }
        catch (ExcepcionAplicacion ex)
        {
            resultado = ResultadoOperacion.Fallo(ex.Codigo, ex.Message);
        }

        var registro = new RegistroComando(_reloj.Ahora, _sesion.NombreUsuario, comando.Nombre, comando.Entrada,
            resultado.Exito, resultado.ToString());
        lock (_candado) _historial.Add(registro);
        _bitacora.Registrar(CategoriasBitacora.Comando, resultado.Exito ? "OK" : "FALLO",
            $"{comando.Nombre}({comando.Entrada}) → {resultado}");
        _metricas.RegistrarComando(resultado.Exito);
        return resultado;
    }
}
