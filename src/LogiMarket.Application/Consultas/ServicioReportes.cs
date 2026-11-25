using LogiMarket.Application.Consultas.Filtros;
using LogiMarket.Application.Seguridad;

namespace LogiMarket.Application.Consultas;

public sealed record ResultadoFiltro(string ArbolInterpretado, IReadOnlyList<PedidoResumenDto> Pedidos);

//reportes de pedidos con filtro interpretado (sin SQL concatenado)
public sealed class ServicioReportes
{
    private readonly IConsultasOperativas _consultas;
    private readonly Autorizador _autorizador;

    public ServicioReportes(IConsultasOperativas consultas, Autorizador autorizador)
    {
        _consultas = consultas;
        _autorizador = autorizador;
    }

    public async Task<ResultadoFiltro> FiltrarPedidosAsync(string filtro, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.ConsultarInformacion);
        var expresion = new ParserFiltroPedidos().Analizar(filtro); //lanza ErrorSintaxisFiltro si es invalido
        var pedidos = await _consultas.ListarPedidosAsync(ct);
        return new ResultadoFiltro(expresion.ToString() ?? filtro, pedidos.Where(expresion.Interpretar).ToList());
    }
}
