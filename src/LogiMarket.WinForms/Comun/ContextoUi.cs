using LogiMarket.Application.Comandos;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Application.Metricas;
using LogiMarket.Application.Seguridad;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Comun;

//puente entre formularios y casos de uso: cada operacion se ejecuta en un ambito DI propio (un
//DbContext por operacion), igual que en las pruebas de integracion
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

    //ejecuta un comando (command) a traves del invocador, en un ambito propio
    public Task<Application.Comun.ResultadoOperacion> EjecutarComandoAsync(Func<FabricaComandos, IComandoOperacion> crear) =>
        EjecutarAsync(sp => Invocador.EjecutarAsync(crear(sp.GetRequiredService<FabricaComandos>())));
}
