namespace LogiMarket.Application.Comun;

public static class CategoriasBitacora
{
    public const string Pago = "PAGO";
    public const string Seguridad = "SEGURIDAD";
    public const string Outbox = "OUTBOX";
    public const string Comando = "COMANDO";
}

public sealed record EntradaBitacora(
    DateTime Fecha, string Categoria, string Etapa, string Mensaje, string? Correlacion, string Usuario)
{
    public override string ToString() =>
        $"{Fecha:HH:mm:ss.fff} [{Categoria}/{Etapa}] {(Correlacion is null ? "" : $"#{Correlacion} ")}{Mensaje} ({Usuario})";
}

//bitacora operativa (LOG) independiente de la transaccion de negocio: los registros de un cobro
//fallido o denegado se conservan aunque la transaccion se revierta
public interface IBitacoraOperativa
{
    void Registrar(string categoria, string etapa, string mensaje, string? correlacion = null);
    IReadOnlyList<EntradaBitacora> Consultar(string? categoria = null, string? correlacion = null);
}

//identificador de correlacion que fluye por las llamadas asincronas (decorator -> adapter)
public static class ContextoCorrelacion
{
    private static readonly AsyncLocal<string?> Valor = new();

    public static string? Actual => Valor.Value;

    public static IDisposable Establecer(string correlacion)
    {
        var anterior = Valor.Value;
        Valor.Value = correlacion;
        return new Restaurador(() => Valor.Value = anterior);
    }

    private sealed class Restaurador(Action accion) : IDisposable
    {
        public void Dispose() => accion();
    }
}
