namespace LogiMarket.Application.Comun;

//error controlado de la capa de aplicacion (codigo estable + mensaje legible)
public class ExcepcionAplicacion : Exception
{
    public ExcepcionAplicacion(string codigo, string mensaje, Exception? interna = null) : base(mensaje, interna)
    {
        Codigo = codigo;
    }

    public string Codigo { get; }
}

//la operacion no esta permitida para el rol de la sesion (autorizacion en application)
public sealed class AccesoDenegadoException : ExcepcionAplicacion
{
    public AccesoDenegadoException(string mensaje) : base("ACCESO_DENEGADO", mensaje) { }
}

public sealed class RecursoNoEncontradoException : ExcepcionAplicacion
{
    public RecursoNoEncontradoException(string recurso, object clave)
        : base("NO_ENCONTRADO", $"{recurso} '{clave}' no existe.") { }
}

//otra operacion modifico los mismos datos (concurrencia optimista)
public sealed class ConflictoConcurrenciaException : ExcepcionAplicacion
{
    public ConflictoConcurrenciaException(string mensaje, Exception? interna = null)
        : base("CONFLICTO_CONCURRENCIA", mensaje, interna) { }
}

//se violo una restriccion de unicidad (SKU, referencia, idempotencia, devolucion unica...)
public sealed class ViolacionUnicidadException : ExcepcionAplicacion
{
    public ViolacionUnicidadException(string mensaje, Exception? interna = null)
        : base("DUPLICADO", mensaje, interna) { }
}

//resultado uniforme de operaciones invocadas desde la interfaz o el invocador de comandos
public sealed record ResultadoOperacion(bool Exito, string Mensaje, string? Codigo = null, object? Datos = null)
{
    public static ResultadoOperacion Ok(string mensaje, object? datos = null) => new(true, mensaje, "OK", datos);
    public static ResultadoOperacion Fallo(string codigo, string mensaje) => new(false, mensaje, codigo);
    public override string ToString() => Exito ? $"OK: {Mensaje}" : $"{Codigo}: {Mensaje}";
}
