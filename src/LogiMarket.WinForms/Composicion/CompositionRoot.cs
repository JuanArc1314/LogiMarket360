using System.Text.Json;
using LogiMarket.Application.Comprobantes;
using LogiMarket.Application.Coordinacion;
using LogiMarket.Infrastructure;
using LogiMarket.WinForms.Comun;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms.Composicion;

public sealed record ConfiguracionApp(string DirectorioDatos, string NombreBaseDatos, int ProcesarOutboxCadaSegundos)
{
    public static ConfiguracionApp Cargar()
    {
        var ruta = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var config = File.Exists(ruta)
            ? JsonSerializer.Deserialize<ConfiguracionApp>(File.ReadAllText(ruta), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            : null;
        return config ?? new ConfiguracionApp("datos", "logimarket.db", 15);
    }

    public string DirectorioAbsoluto => Path.IsPathRooted(DirectorioDatos)
        ? DirectorioDatos
        : Path.Combine(AppContext.BaseDirectory, DirectorioDatos);
}

//unico lugar donde WinForms conoce infrastructure: solo para registrar dependencias
//los formularios reciben casos de uso de application a traves de ContextoUi
public static class CompositionRoot
{
    public static ServiceProvider Construir(ConfiguracionApp configuracion)
    {
        var servicios = new ServiceCollection();

        //implementaciones propias de la presentacion (se registran antes para prevalecer)
        servicios.AddSingleton<IVisorDocumentos, VisorDocumentosWinForms>();
        servicios.AddSingleton<FormularioPrincipalMediator>();
        servicios.AddSingleton<IMediadorVistas>(sp => sp.GetRequiredService<FormularioPrincipalMediator>());
        servicios.AddSingleton<ContextoUi>();

        servicios.AgregarLogiMarket(new OpcionesInfraestructura(configuracion.DirectorioAbsoluto, configuracion.NombreBaseDatos));
        servicios.AddSingleton(configuracion);

        return servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
