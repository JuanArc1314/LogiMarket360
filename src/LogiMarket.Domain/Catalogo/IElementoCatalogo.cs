namespace LogiMarket.Domain.Catalogo;

public interface IElementoCatalogo
{
    string Sku { get; }
    string Nombre { get; }

    decimal PrecioVenta { get; }

    decimal PesoKg { get; }

    bool EsCompuesto { get; }

    bool Contiene(IElementoCatalogo elemento);

    IIteradorComponentes CrearIterador(int cantidad = 1);

    void Aceptar(IVisitanteCatalogo visitante, int multiplicador = 1);
}
