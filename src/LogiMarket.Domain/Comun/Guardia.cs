namespace LogiMarket.Domain.Comun;

/// >Validaciones de entrada reutilizables por las entidades (evita validaciones dispersas).
internal static class Guardia
{
    public static string Texto(string? valor, string campo, int max = 120)
    {
        var limpio = (valor ?? string.Empty).Trim();
        if (limpio.Length == 0)
            throw new ExcepcionDominio("CAMPO_REQUERIDO", $"{campo} es obligatorio.");
        if (limpio.Length > max)
            throw new ExcepcionDominio("CAMPO_DEMASIADO_LARGO", $"{campo} admite como máximo {max} caracteres.");
        return limpio;
    }

    public static string TextoOpcional(string? valor, int max = 250)
    {
        var limpio = (valor ?? string.Empty).Trim();
        return limpio.Length > max ? limpio[..max] : limpio;
    }

    public static string Sku(string? valor)
    {
        var sku = Texto(valor, "SKU", 30).ToUpperInvariant();
        if (sku.Any(char.IsWhiteSpace))
            throw new ExcepcionDominio("SKU_INVALIDO", "El SKU no puede contener espacios.");
        return sku;
    }

    public static int CantidadPositiva(int cantidad, string campo = "La cantidad")
    {
        if (cantidad <= 0)
            throw new ExcepcionDominio("CANTIDAD_INVALIDA", $"{campo} debe ser un entero mayor que cero.");
        return cantidad;
    }
}
