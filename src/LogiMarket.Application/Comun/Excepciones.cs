namespace LogiMarket.Application.Comun;

/// Error controlado de la capa de aplicación (código estable + mensaje legible).
public class ExcepcionAplicacion : Exception
{
    public ExcepcionAplicacion(string codigo, string mensaje, Exception? interna = null) : base(mensaje, interna)
    {
        Codigo = codigo;
    }

    public string Codigo { get; }
}

/// La operación no está permitida para el rol de la sesión (autorización en Application).
public sealed class AccesoDenegadoException : ExcepcionAplicacion
{
    public AccesoDenegadoException(string mensaje) : base("ACCESO_DENEGADO", mensaje) { }
}

public sealed class RecursoNoEncontradoException : ExcepcionAplicacion
{
    public RecursoNoEncontradoException(string recurso, object clave)
        : base("NO_ENCONTRADO", $"{recurso} '{clave}' no existe.") { }
}

/// Otra operación modificó los mismos datos (concurrencia optimista).
public sealed class ConflictoConcurrenciaException : ExcepcionAplicacion
{
    public ConflictoConcurrenciaException(string mensaje, Exception? interna = null)
        : base("CONFLICTO_CONCURRENCIA", mensaje, interna) { }
}

/// Se violó una restricción de unicidad (SKU, referencia, idempotencia, devolución única...).
public sealed class ViolacionUnicidadException : ExcepcionAplicacion
{
    public ViolacionUnicidadException(string mensaje, Exception? interna = null)
        : base("DUPLICADO", mensaje, interna) { }
}

/// Resultado uniforme de operaciones invocadas desde la interfaz o el invocador de comandos.
public sealed record ResultadoOperacion(bool Exito, string Mensaje, string? Codigo = null, object? Datos = null)
{
    public static ResultadoOperacion Ok(string mensaje, object? datos = null) => new(true, mensaje, "OK", datos);
    public static ResultadoOperacion Fallo(string codigo, string mensaje) => new(false, mensaje, codigo);
    public override string ToString() => Exito ? $"OK: {Mensaje}" : $"{Codigo}: {Mensaje}";
}
