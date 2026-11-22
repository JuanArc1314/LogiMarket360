using System.Globalization;

namespace LogiMarket.Domain.Comun;

// Regla única de redondeo monetario (DRY): decimal, 2 decimales, MidpointRounding.AwayFromZero.
public static class Dinero
{
    private static readonly CultureInfo Formato = CultureInfo.InvariantCulture;

    public static decimal Redondear(decimal valor) =>
        Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public static string Formatear(decimal valor) =>
        "USD " + Redondear(valor).ToString("0.00", Formato);

    public static void ExigirPositivo(decimal valor, string campo)
    {
        if (valor <= 0m)
            throw new ExcepcionDominio("MONTO_INVALIDO", $"{campo} debe ser un valor monetario positivo.");
    }
}
