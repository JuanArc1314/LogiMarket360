using LogiMarket.Application.Demostracion;

namespace LogiMarket.Application.Comun;

/// Puerto de tiempo (DIP): permite probar plazos de devolución sin depender del reloj real.
public interface IReloj
{
    DateTime Ahora { get; }
}

/// Reloj del sistema con desfase de días OPCIONAL configurable solo desde la pantalla de demostración
/// (para reproducir "día 20" o "día 10" en la sustentación). Con desfase 0 es la hora real.
public sealed class RelojSistema : IReloj
{
    private readonly ConfiguracionDemostracion _configuracion;

    public RelojSistema(ConfiguracionDemostracion configuracion) => _configuracion = configuracion;

    public DateTime Ahora => DateTime.Now.AddDays(_configuracion.DesfaseDias);
}
