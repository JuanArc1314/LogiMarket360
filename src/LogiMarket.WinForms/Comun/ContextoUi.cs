using LogiMarket.Application.Comandos;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Metricas;
using LogiMarket.Application.Seguridad;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Comun;

/// Puente entre formularios y casos de uso: cada operación se ejecuta en un ámbito DI propio
/// (un DbContext por operación), igual que en las pruebas de integración.
public sealed class ContextoUi
{
    private readonly IServiceProvider _raiz;

    public ContextoUi(IServiceProvider raiz) => _raiz = raiz;

    public ISesionUsuario Sesion => _raiz.GetRequiredService<ISesionUsuario>();
    public Autorizador Autorizador => _raiz.GetRequiredService<Autorizador>();
    public IMediadorVistas Mediador => _raiz.GetRequiredService<IMediadorVistas>();
    public MetricasSesion Metricas => _raiz.GetRequiredService<MetricasSesion>();
    public InvocadorComandos Invocador => _raiz.GetRequiredService<InvocadorComandos>();

    public T Singleton<T>() where T : notnull => _raiz.GetRequiredService<T>();

    public bool Puede(Permiso permiso) => Autorizador.Puede(permiso);

    public async Task<T> EjecutarAsync<T>(Func<IServiceProvider, Task<T>> operacion)
    {
        await using var ambito = _raiz.CreateAsyncScope();
        return await operacion(ambito.ServiceProvider);
    }

    public async Task EjecutarAsync(Func<IServiceProvider, Task> operacion)
    {
        await using var ambito = _raiz.CreateAsyncScope();
        await operacion(ambito.ServiceProvider);
    }

    /// Ejecuta un comando (Command) a través del invocador, en un ámbito propio.
    public Task<Application.Comun.ResultadoOperacion> EjecutarComandoAsync(Func<FabricaComandos, IComandoOperacion> crear) =>
        EjecutarAsync(sp => Invocador.EjecutarAsync(crear(sp.GetRequiredService<FabricaComandos>())));
}
