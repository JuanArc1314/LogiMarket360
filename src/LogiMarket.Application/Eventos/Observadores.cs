using LogiMarket.Application.Comun;
using LogiMarket.Application.Metricas;
using LogiMarket.Application.Notificaciones;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Application.Eventos;

//patron observer - observador
public interface IObservadorEventos
{
    string Nombre { get; }
    Task ManejarAsync(IEventoNegocio evento, CancellationToken ct = default);
}

public sealed record ResultadoPublicacion(IReadOnlyList<string> Notificados, IReadOnlyList<string> Fallos)
{
    public bool Completo => Fallos.Count == 0;
}

//patron observer - sujeto/publicador
public interface IPublicadorEventos
{
    IReadOnlyList<string> Suscriptores { get; }
    void Suscribir(IObservadorEventos observador);
    void Desuscribir(IObservadorEventos observador);
    Task<ResultadoPublicacion> PublicarAsync(IEventoNegocio evento, CancellationToken ct = default);
}

public sealed class PublicadorEventos : IPublicadorEventos
{
    private readonly List<IObservadorEventos> _observadores = new();

    public PublicadorEventos(IEnumerable<IObservadorEventos> observadoresIniciales)
    {
        foreach (var observador in observadoresIniciales) Suscribir(observador);
    }

    public IReadOnlyList<string> Suscriptores => _observadores.Select(o => o.Nombre).ToList();

    public void Suscribir(IObservadorEventos observador)
    {
        if (!_observadores.Contains(observador)) _observadores.Add(observador);
    }

    public void Desuscribir(IObservadorEventos observador) => _observadores.Remove(observador);

    //notifica a todos
    //un fallo no impide notificar a los demas, pero se informa
    public async Task<ResultadoPublicacion> PublicarAsync(IEventoNegocio evento, CancellationToken ct = default)
    {
        var notificados = new List<string>();
        var fallos = new List<string>();
        foreach (var observador in _observadores.ToList())
        {
            try
            {
                await observador.ManejarAsync(evento, ct);
                notificados.Add(observador.Nombre);
            }
            catch (Exception ex)
            {
                fallos.Add($"{observador.Nombre}: {ex.Message}");
            }
        }
        return new ResultadoPublicacion(notificados, fallos);
    }
}

//observador de notificacion: usa el creador del canal actual (factory method)
public sealed class NotificacionObserver : IObservadorEventos
{
    private readonly SelectorCreadorNotificador _selector;

    public NotificacionObserver(SelectorCreadorNotificador selector) => _selector = selector;

    public string Nombre => "Notificacion";

    public Task ManejarAsync(IEventoNegocio evento, CancellationToken ct = default) =>
        _selector.Actual.NotificarAsync(evento, ct);
}

//observador de metricas de sesion (singleton)
public sealed class MetricasObserver : IObservadorEventos
{
    private readonly MetricasSesion _metricas;

    public MetricasObserver(MetricasSesion metricas) => _metricas = metricas;

    public string Nombre => "Metricas";

    public Task ManejarAsync(IEventoNegocio evento, CancellationToken ct = default)
    {
        _metricas.RegistrarEvento(evento);
        return Task.CompletedTask;
    }
}

//observador de auditoria persistente (idempotente por identificador de evento)
public sealed class AuditoriaObserver : IObservadorEventos
{
    private readonly IRepositorioAuditoria _auditoria;
    private readonly IReloj _reloj;

    public AuditoriaObserver(IRepositorioAuditoria auditoria, IReloj reloj)
    {
        _auditoria = auditoria;
        _reloj = reloj;
    }

    public string Nombre => "Auditoria";

    public async Task ManejarAsync(IEventoNegocio evento, CancellationToken ct = default)
    {
        var accion = $"EVENTO_{evento.Tipo}";
        var referencia = evento.Id.ToString();
        if (await _auditoria.ExisteAsync(referencia, accion, ct)) return;
        _auditoria.Agregar(new Auditoria(_reloj.Ahora, "outbox", CategoriaAuditoria.NEGOCIO, accion, evento.Describir(), referencia));
    }
}
