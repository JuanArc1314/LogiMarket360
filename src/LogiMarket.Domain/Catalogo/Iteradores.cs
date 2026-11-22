using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Catalogo;

public readonly record struct ComponenteFisico(ProductoSimple Producto, int Cantidad)
{
    public string Sku => Producto.Sku;
}

/// PATRÓN ITERATOR — Iterador propio. Recorre los componentes físicos de un elemento
/// sin exponer la colección interna del kit.
public interface IIteradorComponentes
{
    bool MoverSiguiente();
    ComponenteFisico Actual { get; }
    void Reiniciar();
}

/// Iterador trivial de una hoja: devuelve el propio producto una vez.
internal sealed class IteradorProductoSimple : IIteradorComponentes
{
    private readonly ProductoSimple _producto;
    private readonly int _cantidad;
    private bool _entregado;
    private bool _posicionado;

    public IteradorProductoSimple(ProductoSimple producto, int cantidad)
    {
        _producto = producto;
        _cantidad = Guardia.CantidadPositiva(cantidad);
    }

    public ComponenteFisico Actual => _posicionado
        ? new ComponenteFisico(_producto, _cantidad)
        : throw new InvalidOperationException("Llame a MoverSiguiente antes de leer Actual.");

    public bool MoverSiguiente()
    {
        if (_entregado) { _posicionado = false; return false; }
        _entregado = true;
        _posicionado = true;
        return true;
    }

    public void Reiniciar() { _entregado = false; _posicionado = false; }
}

/// Iterador de kits anidados con pila explícita (recorrido en profundidad, determinista por orden de inserción).
/// Propaga cantidades multiplicando por la cantidad de cada nivel y termina siempre: la profundidad está acotada.
internal sealed class IteradorComponentesKit : IIteradorComponentes
{
    private sealed class Marco
    {
        public required KitCompuesto Kit { get; init; }
        public required int Multiplicador { get; init; }
        public int Indice { get; set; }
    }

    private readonly KitCompuesto _raiz;
    private readonly int _cantidad;
    private readonly Stack<Marco> _pila = new();
    private ComponenteFisico? _actual;

    public IteradorComponentesKit(KitCompuesto raiz, int cantidad)
    {
        _raiz = raiz;
        _cantidad = Guardia.CantidadPositiva(cantidad);
        Reiniciar();
    }

    public ComponenteFisico Actual =>
        _actual ?? throw new InvalidOperationException("Llame a MoverSiguiente antes de leer Actual.");

    public bool MoverSiguiente()
    {
        while (_pila.Count > 0)
        {
            var marco = _pila.Peek();
            if (marco.Indice >= marco.Kit.CantidadComponentes)
            {
                _pila.Pop();
                continue;
            }

            var componente = marco.Kit.ComponenteEn(marco.Indice++);
            var multiplicador = marco.Multiplicador * componente.Cantidad;

            switch (componente.Elemento)
            {
                case ProductoSimple producto:
                    _actual = new ComponenteFisico(producto, multiplicador);
                    return true;
                case KitCompuesto subKit:
                    if (_pila.Count >= KitCompuesto.ProfundidadMaxima)
                        throw new ExcepcionDominio("KIT_PROFUNDIDAD",
                            $"El recorrido de {_raiz.Sku} supera la profundidad máxima.");
                    _pila.Push(new Marco { Kit = subKit, Multiplicador = multiplicador });
                    break;
            }
        }

        _actual = null;
        return false;
    }

    public void Reiniciar()
    {
        _pila.Clear();
        _actual = null;
        _pila.Push(new Marco { Kit = _raiz, Multiplicador = _cantidad });
    }
}

/// Utilidad de expansión: usa el iterador y agrega cantidades por SKU final (orden de aparición).
public static class ExpansionCatalogo
{
    public static IReadOnlyList<ComponenteFisico> ExpandirAgregado(IElementoCatalogo elemento, int cantidad = 1)
    {
        var acumulado = new Dictionary<string, ComponenteFisico>(StringComparer.OrdinalIgnoreCase);
        var orden = new List<string>();
        var iterador = elemento.CrearIterador(cantidad);
        while (iterador.MoverSiguiente())
        {
            var actual = iterador.Actual;
            if (acumulado.TryGetValue(actual.Sku, out var previo))
                acumulado[actual.Sku] = previo with { Cantidad = previo.Cantidad + actual.Cantidad };
            else
            {
                acumulado[actual.Sku] = actual;
                orden.Add(actual.Sku);
            }
        }
        return orden.Select(sku => acumulado[sku]).ToList();
    }
}
