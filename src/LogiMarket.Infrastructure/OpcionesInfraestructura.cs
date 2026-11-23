namespace LogiMarket.Infrastructure;

/// Rutas de ejecución (base SQLite, comprobantes, notificaciones simuladas y bitácora).
public sealed class OpcionesInfraestructura
{
    public OpcionesInfraestructura(string directorioDatos, string nombreBaseDatos = "logimarket.db")
    {
        DirectorioDatos = Path.GetFullPath(directorioDatos);
        RutaBaseDatos = Path.Combine(DirectorioDatos, nombreBaseDatos);
    }

    public string DirectorioDatos { get; }
    public string RutaBaseDatos { get; }
    public string CadenaConexion => $"Data Source={RutaBaseDatos};Default Timeout=30";
    public string DirectorioComprobantes => Path.Combine(DirectorioDatos, "comprobantes");
    public string DirectorioNotificaciones => Path.Combine(DirectorioDatos, "notificaciones");
    public string RutaBitacora => Path.Combine(DirectorioDatos, "logs", "bitacora.log");

    /// Iteraciones PBKDF2 (configurable para pruebas rápidas; producción usa el valor por defecto).
    public int IteracionesHash { get; init; } = 100_000;
}
