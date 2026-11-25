using LogiMarket.Domain.Eventos;

namespace LogiMarket.Application.Metricas;

public sealed record InstantaneaMetricas(
    Guid InstanciaId, DateTime Inicio, int PedidosPagados, decimal MontoCobrado, int CobrosRechazados,
    int Despachos, int Devoluciones, decimal MontoReembolsado, int ComandosEjecutados, int ComandosFallidos,
    int EventosContabilizados);

//patron singleton - registrado como unica instancia en el contenedor DI (AddSingleton) y
//compartido por varias pantallas
//contadores de la sesion con sincronizacion segura (lock)
//la base de datos sigue siendo la fuente de verdad
//estos valores son indicadores de la sesion en curso
public sealed class MetricasSesion
{
    private readonly object _candado = new();
    private readonly HashSet<Guid> _eventosContabilizados = new();
    private int _pedidosPagados, _cobrosRechazados, _despachos, _devoluciones, _comandos, _comandosFallidos;
    private decimal _montoCobrado, _montoReembolsado;

    public Guid InstanciaId { get; } = Guid.NewGuid();
    public DateTime Inicio { get; } = DateTime.Now;

    public event EventHandler? Cambiaron;

    //contabiliza un evento una sola vez (idempotente ante reprocesos del outbox)
    public bool RegistrarEvento(IEventoNegocio evento)
    {
        lock (_candado)
        {
            if (!_eventosContabilizados.Add(evento.Id)) return false;
            switch (evento)
            {
                case PedidoPagado pagado:
                    _pedidosPagados++;
                    _montoCobrado += pagado.Total;
                    break;
                case DespachoRegistrado:
                    _despachos++;
                    break;
                case DevolucionAceptada devolucion:
                    _devoluciones++;
                    _montoReembolsado += devolucion.MontoReembolso;
                    break;
            }
        }
        Cambiaron?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RegistrarCobroRechazado()
    {
        Interlocked.Increment(ref _cobrosRechazados);
        Cambiaron?.Invoke(this, EventArgs.Empty);
    }

    public void RegistrarComando(bool exito)
    {
        Interlocked.Increment(ref _comandos);
        if (!exito) Interlocked.Increment(ref _comandosFallidos);
        Cambiaron?.Invoke(this, EventArgs.Empty);
    }

    public InstantaneaMetricas Instantanea()
    {
        lock (_candado)
        {
            return new InstantaneaMetricas(InstanciaId, Inicio, _pedidosPagados, _montoCobrado,
                Volatile.Read(ref _cobrosRechazados), _despachos, _devoluciones, _montoReembolsado,
                Volatile.Read(ref _comandos), Volatile.Read(ref _comandosFallidos), _eventosContabilizados.Count);
        }
    }
}
