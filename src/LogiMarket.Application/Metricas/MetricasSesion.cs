using LogiMarket.Domain.Eventos;

namespace LogiMarket.Application.Metricas;

public sealed record InstantaneaMetricas(
    Guid InstanciaId, DateTime Inicio, int PedidosPagados, decimal MontoCobrado, int CobrosRechazados,
    int Despachos, int Devoluciones, decimal MontoReembolsado, int ComandosEjecutados, int ComandosFallidos,
    int EventosContabilizados);

/// PATRÓN SINGLETON — registrado como ÚNICA instancia en el contenedor DI (AddSingleton) y compartido
/// por varias pantallas. Contadores de la sesión con sincronización segura (lock). La base de datos
/// sigue siendo la fuente de verdad; estos valores son indicadores de la sesión en curso.
public sealed class MetricasSesion
{
    private readonly object _candado = new();
    private readonly HashSet<Guid> _eventosContabilizados = new();
    private int _pedidosPagados, _cobrosRechazados, _despachos, _devoluciones, _comandos, _comandosFallidos;
    private decimal _montoCobrado, _montoReembolsado;

    public Guid InstanciaId { get; } = Guid.NewGuid();
    public DateTime Inicio { get; } = DateTime.Now;

    public event EventHandler? Cambiaron;

    /// Contabiliza un evento una sola vez (idempotente ante reprocesos del Outbox).
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
