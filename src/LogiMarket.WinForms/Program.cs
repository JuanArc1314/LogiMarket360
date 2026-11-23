using LogiMarket.Infrastructure.Persistencia;
using LogiMarket.WinForms.Comun;
using LogiMarket.WinForms.Composicion;
using LogiMarket.WinForms.Formularios;
using Microsoft.Extensions.DependencyInjection;

namespace LogiMarket.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var configuracion = ConfiguracionApp.Cargar();
        using var proveedor = CompositionRoot.Construir(configuracion);

        try
        {
            // Crea la base desde cero si no existe: migraciones EF Core + seed idempotente.
            using var ambito = proveedor.CreateScope();
            ambito.ServiceProvider.GetRequiredService<InicializadorBaseDatos>().InicializarAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo preparar la base de datos:\n{ex.Message}", "LogiMarket 360",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var ui = proveedor.GetRequiredService<ContextoUi>();
        while (true)
        {
            using var acceso = new LoginForm(ui);
            if (acceso.ShowDialog() != DialogResult.OK) break;

            using var principal = new MainForm(ui, configuracion);
            System.Windows.Forms.Application.Run(principal);
            if (!principal.CerroSesion) break; // si solo cerró sesión, vuelve a la pantalla de acceso
        }
    }
}
