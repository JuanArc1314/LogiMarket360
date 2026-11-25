using LogiMarket.Application.Demostracion;

namespace LogiMarket.Application.Comun;

//puerto de tiempo (DIP): permite probar plazos de devolucion sin depender del reloj real
public interface IReloj
{
    DateTime Ahora { get; }
}

//reloj del sistema con desfase de dias opcional configurable solo desde la pantalla de
//demostracion (para reproducir "día 20" o "día 10" en la sustentacion)
//con desfase 0 es la hora real
public sealed class RelojSistema : IReloj
{
    private readonly ConfiguracionDemostracion _configuracion;

    public RelojSistema(ConfiguracionDemostracion configuracion) => _configuracion = configuracion;

    public DateTime Ahora => DateTime.Now.AddDays(_configuracion.DesfaseDias);
}
