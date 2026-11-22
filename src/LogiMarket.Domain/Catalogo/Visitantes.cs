namespace LogiMarket.Domain.Catalogo;

/// PATRÓN VISITOR — Visitante. Nuevas operaciones sobre el catálogo (desglose, peso logístico)
public interface IVisitanteCatalogo
{
    void VisitarProducto(ProductoSimple producto, int multiplicador);
    void VisitarKit(KitCompuesto kit, int multiplicador);
    void SalirDeKit(KitCompuesto kit, int multiplicador);
}

/// Línea del desglose físico.
public sealed record LineaDesglose(string Sku, string Nombre, int Cantidad, decimal PesoUnitarioKg)
{
    public decimal PesoTotalKg => PesoUnitarioKg * Cantidad;
}

/// Visitante concreto: desglose físico por SKU final y traza de la visita anidada.
public sealed class DesgloseFisicoVisitor : IVisitanteCatalogo
{
    private readonly Dictionary<string, LineaDesglose> _lineas = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _orden = new();
    private readonly List<string> _traza = new();
    private int _nivel;

    public IReadOnlyList<LineaDesglose> Lineas => _orden.Select(s => _lineas[s]).ToList();

    /// Recorrido observable, p. ej. "KIT_TELETRABAJO > KIT_OFICINA > P01".
    public IReadOnlyList<string> Traza => _traza;

    public void VisitarProducto(ProductoSimple producto, int multiplicador)
    {
        _traza.Add($"{new string(' ', _nivel * 2)}{producto.Sku} x{multiplicador}");
        if (_lineas.TryGetValue(producto.Sku, out var previa))
            _lineas[producto.Sku] = previa with { Cantidad = previa.Cantidad + multiplicador };
        else
        {
            _lineas[producto.Sku] = new LineaDesglose(producto.Sku, producto.Nombre, multiplicador, producto.PesoKg);
            _orden.Add(producto.Sku);
        }
    }

    public void VisitarKit(KitCompuesto kit, int multiplicador)
    {
        _traza.Add($"{new string(' ', _nivel * 2)}[{kit.Sku}] x{multiplicador}");
        _nivel++;
    }

    public void SalirDeKit(KitCompuesto kit, int multiplicador) => _nivel--;
}

/// Visitante concreto: peso logístico total y peso por kit visitado.
public sealed class PesoLogisticoVisitor : IVisitanteCatalogo
{
    private readonly Stack<(string Sku, decimal Acumulado)> _kitsAbiertos = new();
    private readonly Dictionary<string, decimal> _pesoPorKit = new(StringComparer.OrdinalIgnoreCase);

    public decimal PesoTotalKg { get; private set; }
    public IReadOnlyDictionary<string, decimal> PesoPorKit => _pesoPorKit;

    public void VisitarProducto(ProductoSimple producto, int multiplicador) =>
        PesoTotalKg += producto.PesoKg * multiplicador;

    public void VisitarKit(KitCompuesto kit, int multiplicador) =>
        _kitsAbiertos.Push((kit.Sku, PesoTotalKg));

    public void SalirDeKit(KitCompuesto kit, int multiplicador)
    {
        var (sku, inicio) = _kitsAbiertos.Pop();
        _pesoPorKit[sku] = PesoTotalKg - inicio;
    }
}
