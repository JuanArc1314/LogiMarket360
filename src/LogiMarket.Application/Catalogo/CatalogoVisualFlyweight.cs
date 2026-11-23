using System.Collections.Concurrent;
using LogiMarket.Application.Consultas;

namespace LogiMarket.Application.Catalogo;


/// PATRÓN FLYWEIGHT — Estado intrínseco, inmutable y compartido de un SKU (nombre, categoría, descripción).
public sealed class MetadatosSku
{
    public MetadatosSku(string sku, string nombre, string categoria, string descripcion)
    {
        Sku = sku;
        Nombre = nombre;
        Categoria = categoria;
        Descripcion = descripcion;
    }

    public string Sku { get; }
    public string Nombre { get; }
    public string Categoria { get; }
    public string Descripcion { get; }
}

/// Fila de vista: referencia al flyweight + estado EXTRÍNSECO por fila (stock, cantidad, precio).
public sealed class FilaCatalogoVisual
{
    public FilaCatalogoVisual(int numero, MetadatosSku metadatos, int stock, int cantidad, decimal precioVenta)
    {
        Numero = numero;
        Metadatos = metadatos;
        Stock = stock;
        Cantidad = cantidad;
        PrecioVenta = precioVenta;
    }

    public int Numero { get; }
    public MetadatosSku Metadatos { get; }
    public string Sku => Metadatos.Sku;
    public string Nombre => Metadatos.Nombre;
    public string Categoria => Metadatos.Categoria;
    public int Stock { get; }
    public int Cantidad { get; }
    public decimal PrecioVenta { get; }
}

/// PATRÓN FLYWEIGHT — Fábrica: devuelve SIEMPRE la misma instancia por SKU.
public sealed class CatalogoVisualFlyweightFactory
{
    private readonly ConcurrentDictionary<string, MetadatosSku> _cache = new(StringComparer.OrdinalIgnoreCase);

    public int InstanciasCreadas => _cache.Count;

    public MetadatosSku Obtener(ProductoDto producto) =>
        _cache.GetOrAdd(producto.Sku, _ => new MetadatosSku(producto.Sku, producto.Nombre, producto.Categoria, producto.Descripcion));
}

public sealed record InformeFlyweight(
    int Filas, int ProductosBase, int InstanciasMetadatosCompartidas, int InstanciasMetadatosSinCompartir,
    long BytesConFlyweight, long BytesSinFlyweight, bool ReferenciasCompartidas, string Condiciones)
{
    public long AhorroBytes => BytesSinFlyweight - BytesConFlyweight;
}

/// Genera N filas de vista y compara bajo condiciones equivalentes (mismas filas, mismos valores
/// extrínsecos) la memoria asignada con metadatos compartidos frente a metadatos duplicados por fila.
public static class DemostracionFlyweight
{
    public static (IReadOnlyList<FilaCatalogoVisual> Filas, InformeFlyweight Informe) Generar(
        IReadOnlyList<ProductoDto> productos, int filas = 1000)
    {
        if (productos.Count == 0) throw new ArgumentException("Se requiere al menos un producto.", nameof(productos));
        if (filas < 1) throw new ArgumentOutOfRangeException(nameof(filas));

        // Calentamiento para no medir la compilación JIT en la primera pasada.
        ConstruirCompartidas(productos, Math.Min(10, filas), new CatalogoVisualFlyweightFactory());
        ConstruirDuplicadas(productos, Math.Min(10, filas));

        var fabrica = new CatalogoVisualFlyweightFactory();
        var antesCon = GC.GetAllocatedBytesForCurrentThread();
        var compartidas = ConstruirCompartidas(productos, filas, fabrica);
        var bytesCon = GC.GetAllocatedBytesForCurrentThread() - antesCon;

        var antesSin = GC.GetAllocatedBytesForCurrentThread();
        var duplicadas = ConstruirDuplicadas(productos, filas);
        var bytesSin = GC.GetAllocatedBytesForCurrentThread() - antesSin;

        var mismoSku = compartidas.Where(f => f.Sku == compartidas[0].Sku).Take(2).ToList();
        var compartidasOk = mismoSku.Count == 2 && ReferenceEquals(mismoSku[0].Metadatos, mismoSku[1].Metadatos);
        var instanciasSin = duplicadas.Select(f => f.Metadatos).Distinct(ReferenceEqualityComparer.Instance).Count();

        var informe = new InformeFlyweight(filas, productos.Count, fabrica.InstanciasCreadas, instanciasSin,
            bytesCon, bytesSin, compartidasOk,
            $"{filas} filas; {productos.Count} SKU base; mismo hilo; mismos valores extrínsecos; " +
            "medición con GC.GetAllocatedBytesForCurrentThread tras calentamiento.");
        return (compartidas, informe);
    }

    private static List<FilaCatalogoVisual> ConstruirCompartidas(IReadOnlyList<ProductoDto> productos, int filas,
        CatalogoVisualFlyweightFactory fabrica)
    {
        var lista = new List<FilaCatalogoVisual>(filas);
        for (var i = 0; i < filas; i++)
        {
            var p = productos[i % productos.Count];
            lista.Add(new FilaCatalogoVisual(i + 1, fabrica.Obtener(p), p.Stock, 1 + i % 5, p.Precio));
        }
        return lista;
    }

    private static List<FilaCatalogoVisual> ConstruirDuplicadas(IReadOnlyList<ProductoDto> productos, int filas)
    {
        var lista = new List<FilaCatalogoVisual>(filas);
        for (var i = 0; i < filas; i++)
        {
            var p = productos[i % productos.Count];
            // Sin Flyweight: cada fila crea sus propios metadatos (copias de texto equivalentes a cargar la vista).
            var meta = new MetadatosSku(new string(p.Sku.AsSpan()), new string(p.Nombre.AsSpan()),
                new string(p.Categoria.AsSpan()), new string(p.Descripcion.AsSpan()));
            lista.Add(new FilaCatalogoVisual(i + 1, meta, p.Stock, 1 + i % 5, p.Precio));
        }
        return lista;
    }
}
