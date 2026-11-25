using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Catalogo;

//patron composite - hoja
//producto fisico con SKU unico, precio, peso y existencias
public sealed class ProductoSimple : IElementoCatalogo
{
    private ProductoSimple() { } //EF Core

    public ProductoSimple(string sku, string nombre, string categoria, string descripcion,
        decimal precio, decimal pesoKg, int stockInicial)
    {
        Sku = Guardia.Sku(sku);
        if (stockInicial < 0)
            throw new ExcepcionDominio("STOCK_INVALIDO", "El stock inicial no puede ser negativo.");
        Stock = stockInicial;
        Activo = true;
        Actualizar(nombre, categoria, descripcion, precio, pesoKg);
    }

    public int Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string Categoria { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public decimal Precio { get; private set; }
    public decimal PesoKg { get; private set; }
    public int Stock { get; private set; }
    public bool Activo { get; private set; }

    //token de concurrencia optimista (evita sobreventa ante escrituras simultaneas)
    public int Version { get; private set; }

    public decimal PrecioVenta => Precio;
    public bool EsCompuesto => false;

    public void Actualizar(string nombre, string categoria, string descripcion, decimal precio, decimal pesoKg)
    {
        Dinero.ExigirPositivo(precio, "El precio");
        if (pesoKg < 0m)
            throw new ExcepcionDominio("PESO_INVALIDO", "El peso no puede ser negativo.");
        if (Dinero.Redondear(precio) != precio)
            throw new ExcepcionDominio("MONTO_INVALIDO", "El precio admite como máximo dos decimales.");

        Nombre = Guardia.Texto(nombre, "El nombre");
        Categoria = Guardia.Texto(categoria, "La categoría", 60);
        Descripcion = Guardia.TextoOpcional(descripcion);
        Precio = precio;
        PesoKg = pesoKg;
        Version++;
    }

    public void DescontarStock(int cantidad)
    {
        Guardia.CantidadPositiva(cantidad);
        if (Stock < cantidad)
            throw new ExcepcionDominio("STOCK_INSUFICIENTE",
                $"Stock insuficiente de {Sku}: requerido {cantidad}, disponible {Stock}.");
        Stock -= cantidad;
        Version++;
    }

    public void ReintegrarStock(int cantidad)
    {
        Guardia.CantidadPositiva(cantidad);
        Stock += cantidad;
        Version++;
    }

    //ajuste manual de inventario (conteo fisico)
    //devuelve la diferencia aplicada
    public int AjustarStock(int nuevoStock)
    {
        if (nuevoStock < 0)
            throw new ExcepcionDominio("STOCK_INVALIDO", "El stock no puede ser negativo.");
        var diferencia = nuevoStock - Stock;
        Stock = nuevoStock;
        Version++;
        return diferencia;
    }

    public void Desactivar() { Activo = false; Version++; }
    public void Activar() { Activo = true; Version++; }

    public bool Contiene(IElementoCatalogo elemento) =>
        ReferenceEquals(elemento, this) || string.Equals(elemento.Sku, Sku, StringComparison.OrdinalIgnoreCase);

    public IIteradorComponentes CrearIterador(int cantidad = 1) => new IteradorProductoSimple(this, cantidad);

    public void Aceptar(IVisitanteCatalogo visitante, int multiplicador = 1) =>
        visitante.VisitarProducto(this, multiplicador);

    public override string ToString() => $"{Sku} - {Nombre}";
}
