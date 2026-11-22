using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Catalogo;

public sealed class KitComponente
{
    private KitComponente() { } 

    internal KitComponente(KitCompuesto kit, IElementoCatalogo elemento, int cantidad)
    {
        Kit = kit;
        switch (elemento)
        {
            case ProductoSimple producto:
                Producto = producto;
                break;
            case KitCompuesto subKit:
                SubKit = subKit;
                break;
            default:
                throw new ExcepcionDominio("COMPONENTE_INVALIDO", "Tipo de elemento de catálogo no soportado.");
        }
        Cantidad = Guardia.CantidadPositiva(cantidad);
    }

    public int Id { get; private set; }
    public int KitId { get; private set; }
    public KitCompuesto Kit { get; private set; } = null!;
    public int? ProductoId { get; private set; }
    public ProductoSimple? Producto { get; private set; }
    public int? SubKitId { get; private set; }
    public KitCompuesto? SubKit { get; private set; }
    public int Cantidad { get; private set; }

    public IElementoCatalogo Elemento =>
        (IElementoCatalogo?)Producto ?? SubKit
        ?? throw new InvalidOperationException("El componente del kit no tiene su elemento cargado.");

    internal void Incrementar(int cantidad) => Cantidad += Guardia.CantidadPositiva(cantidad);
}
