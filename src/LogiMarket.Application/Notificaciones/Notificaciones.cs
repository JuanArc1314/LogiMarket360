using LogiMarket.Application.Demostracion;
using LogiMarket.Domain.Eventos;

namespace LogiMarket.Application.Notificaciones;

public sealed record MensajeNotificacion(Guid EventoId, string Destinatario, string Destino, string Asunto, string Cuerpo);

//producto del factory method: todos los notificadores comparten este contrato
public interface INotificador
{
    string Canal { get; }
    Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct = default);
}

//patron factory method - creador abstracto
//la operacion NotificarAsync es comun
//cada creador concreto (EMAIL, SMS) decide que notificador crear en CrearNotificador
public abstract class CreadorNotificador
{
    public abstract string Canal { get; }

    //metodo de fabrica
    protected abstract INotificador CrearNotificador();

    protected abstract string ResolverDestino(IEventoNegocio evento);

    //crea el notificador, arma el mensaje desde el evento y lo envia
    //devuelve el notificador usado
    public async Task<INotificador> NotificarAsync(IEventoNegocio evento, CancellationToken ct = default)
    {
        var notificador = CrearNotificador();
        var mensaje = new MensajeNotificacion(evento.Id, evento.ClienteNombre, ResolverDestino(evento),
            $"LogiMarket 360 · {evento.Tipo} · {evento.CodigoPedido}", evento.Describir());
        await notificador.EnviarAsync(mensaje, ct);
        return notificador;
    }
}

//selecciona el creador segun la configuracion (fuera de los formularios)
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
