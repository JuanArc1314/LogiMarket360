using System.Text;
using LogiMarket.Application.Comun;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Notificaciones;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Infrastructure.Notificaciones;

//dependencias comunes de los notificadores simulados
public sealed record EntornoNotificacion(
    IRepositorioNotificaciones Repositorio, IReloj Reloj, ConfiguracionDemostracion Configuracion,
    OpcionesInfraestructura Opciones, IBitacoraOperativa Bitacora);

//base de notificador simulado: idempotente por (evento, canal) y con fallo inducible
public abstract class NotificadorSimuladoBase : INotificador
{
    private readonly EntornoNotificacion _entorno;

    protected NotificadorSimuladoBase(EntornoNotificacion entorno) => _entorno = entorno;

    public abstract string Canal { get; }

    public async Task EnviarAsync(MensajeNotificacion mensaje, CancellationToken ct = default)
    {
        if (_entorno.Configuracion.SimularFalloNotificador)
            throw new InvalidOperationException($"Servicio {Canal} simulado no disponible.");
        if (await _entorno.Repositorio.ExisteAsync(mensaje.EventoId, Canal, ct)) return; //sin duplicados

        var cuerpo = Formatear(mensaje);
        var carpeta = Path.Combine(_entorno.Opciones.DirectorioNotificaciones, Canal.ToLowerInvariant());
        Directory.CreateDirectory(carpeta);
        await File.WriteAllTextAsync(Path.Combine(carpeta, $"{mensaje.EventoId:N}.{Extension}"), cuerpo, Encoding.UTF8, ct);

        _entorno.Repositorio.Agregar(new Notificacion(mensaje.EventoId, Canal, mensaje.Destino, mensaje.Asunto, cuerpo, _entorno.Reloj.Ahora));
        _entorno.Bitacora.Registrar(CategoriasBitacora.Outbox, "NOTIFICACION", $"{GetType().Name} → {mensaje.Destino}: {mensaje.Asunto}");
    }

    protected abstract string Extension { get; }
    protected abstract string Formatear(MensajeNotificacion mensaje);
}

public sealed class NotificadorEmailSimulado(EntornoNotificacion entorno) : NotificadorSimuladoBase(entorno)
{
    public override string Canal => CanalesNotificacion.Email;
    protected override string Extension => "eml";

    protected override string Formatear(MensajeNotificacion m) =>
        $"To: {m.Destino}\r\nFrom: notificaciones@logimarket.example\r\nSubject: {m.Asunto}\r\n\r\n" +
        $"Hola {m.Destinatario},\r\n\r\n{m.Cuerpo}\r\n\r\n-- LogiMarket 360 (correo simulado)";
}

public sealed class NotificadorSmsSimulado(EntornoNotificacion entorno) : NotificadorSimuladoBase(entorno)
{
    public const int LongitudMaxima = 160;
    public override string Canal => CanalesNotificacion.Sms;
    protected override string Extension => "sms.txt";

    protected override string Formatear(MensajeNotificacion m)
    {
        var texto = $"LogiMarket: {m.Cuerpo}";
        return texto.Length <= LongitudMaxima ? texto : texto[..(LongitudMaxima - 1)] + "…";
    }
}

//patron factory method - creador concreto EMAIL
public sealed class CreadorNotificadorEmail(EntornoNotificacion entorno) : CreadorNotificador
{
    public override string Canal => CanalesNotificacion.Email;
    protected override INotificador CrearNotificador() => new NotificadorEmailSimulado(entorno);
    protected override string ResolverDestino(IEventoNegocio e) =>
        string.IsNullOrWhiteSpace(e.ClienteEmail) ? "sin-correo@example.com" : e.ClienteEmail;
}

//patron factory method - creador concreto SMS
public sealed class CreadorNotificadorSms(EntornoNotificacion entorno) : CreadorNotificador
{
    public override string Canal => CanalesNotificacion.Sms;
    protected override INotificador CrearNotificador() => new NotificadorSmsSimulado(entorno);
    protected override string ResolverDestino(IEventoNegocio e) =>
        string.IsNullOrWhiteSpace(e.ClienteTelefono) ? "0000000000" : e.ClienteTelefono;
}
