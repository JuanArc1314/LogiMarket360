namespace LogiMarket.Domain.Comun;

/// Violación de una invariante o regla de negocio del dominio.
public class ExcepcionDominio : Exception
{
    public ExcepcionDominio(string codigo, string mensaje) : base(mensaje)
    {
        Codigo = codigo;
    }

    public string Codigo { get; }
}
