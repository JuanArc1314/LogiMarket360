using LogiMarket.Application.Demostracion;
using LogiMarket.Domain.Eventos;

namespace LogiMarket.Application.Notificaciones;

public sealed record MensajeNotificacion(Guid EventoId, string Destinatario, string Destino, string Asunto, string Cuerpo);

/// <summary>Producto del Factory Method: todos los notificadores comparten este contrato.</summary>
public interface INotificador
{
    string Canal { get; }
    Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct = default);
}

/// <summary>
/// PATRÓN FACTORY METHOD — Creador abstracto. La operación <see cref="NotificarAsync"/> es común;
/// cada creador concreto (EMAIL, SMS) decide QUÉ notificador crear en <see cref="CrearNotificador"/>.
/// </summary>
public abstract class CreadorNotificador
{
    public abstract string Canal { get; }

    /// <summary>MÉTODO DE FÁBRICA.</summary>
    protected abstract INotificador CrearNotificador();

    protected abstract string ResolverDestino(IEventoNegocio evento);

    /// <summary>Crea el notificador, arma el mensaje desde el evento y lo envía. Devuelve el notificador usado.</summary>
    public async Task<INotificador> NotificarAsync(IEventoNegocio evento, CancellationToken ct = default)
    {
        var notificador = CrearNotificador();
        var mensaje = new MensajeNotificacion(evento.Id, evento.ClienteNombre, ResolverDestino(evento),
            $"LogiMarket 360 · {evento.Tipo} · {evento.CodigoPedido}", evento.Describir());
        await notificador.EnviarAsync(mensaje, ct);
        return notificador;
    }
}

/// <summary>Selecciona el creador según la configuración (fuera de los formularios).</summary>
public sealed class SelectorCreadorNotificador
{
    private readonly IReadOnlyDictionary<string, CreadorNotificador> _creadores;
    private readonly ConfiguracionDemostracion _configuracion;

    public SelectorCreadorNotificador(IEnumerable<CreadorNotificador> creadores, ConfiguracionDemostracion configuracion)
    {
        _creadores = creadores.ToDictionary(c => c.Canal, StringComparer.OrdinalIgnoreCase);
        _configuracion = configuracion;
    }

    public IReadOnlyCollection<string> CanalesDisponibles => _creadores.Keys.ToList();

    public CreadorNotificador Actual =>
        _creadores.TryGetValue(_configuracion.CanalNotificacion, out var creador)
            ? creador
            : throw new InvalidOperationException($"Canal no registrado: {_configuracion.CanalNotificacion}");
}
