using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Catalogo;

//patron composite - compuesto
//un kit contiene productos u otros kits
//precio y peso se calculan recursivamente
//no se permite que un kit se contenga directa o indirectamente
public sealed class KitCompuesto : IElementoCatalogo
{
    //profundidad maxima de anidamiento admitida (defensa ante datos corruptos)
    public const int ProfundidadMaxima = 16;

    private readonly List<KitComponente> _componentes = new();

    private KitCompuesto() { } //EF Core

    public KitCompuesto(string sku, string nombre, string descripcion)
    {
        Sku = Guardia.Sku(sku);
        Nombre = Guardia.Texto(nombre, "El nombre");
        Descripcion = Guardia.TextoOpcional(descripcion);
        Activo = true;
    }

    public int Id { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string Descripcion { get; private set; } = string.Empty;
    public bool Activo { get; private set; }

    //vista de solo lectura de los componentes directos (para persistencia y presentacion)
    public IReadOnlyList<KitComponente> Componentes => _componentes.AsReadOnly();

    public bool EsCompuesto => true;

    public decimal PrecioVenta =>
        Dinero.Redondear(_componentes.Sum(c => c.Elemento.PrecioVenta * c.Cantidad));

    public decimal PesoKg => _componentes.Sum(c => c.Elemento.PesoKg * c.Cantidad);

    public void Renombrar(string nombre, string descripcion)
    {
        Nombre = Guardia.Texto(nombre, "El nombre");
        Descripcion = Guardia.TextoOpcional(descripcion);
    }

    //agrega un producto o kit
    //rechaza ciclos (KIT_CICLICO) y cantidades no positivas
    public void AgregarComponente(IElementoCatalogo elemento, int cantidad)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        Guardia.CantidadPositiva(cantidad);

        if (elemento.Contiene(this))
            throw new ExcepcionDominio("KIT_CICLICO",
                $"No se puede agregar {elemento.Sku} a {Sku}: se formaría una referencia circular.");

        var existente = _componentes.FirstOrDefault(c =>
            string.Equals(c.Elemento.Sku, elemento.Sku, StringComparison.OrdinalIgnoreCase));
        if (existente is not null)
            existente.Incrementar(cantidad);
        else
            _componentes.Add(new KitComponente(this, elemento, cantidad));
    }

    public void QuitarComponente(string sku)
    {
        var componente = _componentes.FirstOrDefault(c =>
            string.Equals(c.Elemento.Sku, sku, StringComparison.OrdinalIgnoreCase))
            ?? throw new ExcepcionDominio("COMPONENTE_INEXISTENTE", $"{sku} no forma parte de {Sku}.");
        _componentes.Remove(componente);
    }

    public void Desactivar() => Activo = false;
    public void Activar() => Activo = true;

    public bool Contiene(IElementoCatalogo elemento) => Contiene(elemento, 0);

    private bool Contiene(IElementoCatalogo elemento, int profundidad)
    {
        if (ReferenceEquals(elemento, this) || string.Equals(elemento.Sku, Sku, StringComparison.OrdinalIgnoreCase))
            return true;
        if (profundidad > ProfundidadMaxima)
            throw new ExcepcionDominio("KIT_PROFUNDIDAD", $"El kit {Sku} supera la profundidad máxima permitida.");

        foreach (var componente in _componentes)
        {
            var hijo = componente.Elemento;
            var contenido = hijo is KitCompuesto kit ? kit.Contiene(elemento, profundidad + 1) : hijo.Contiene(elemento);
            if (contenido) return true;
        }
        return false;
    }

    //acceso restringido para el iterador (no se entrega la lista interna)
    internal int CantidadComponentes => _componentes.Count;
    internal KitComponente ComponenteEn(int indice) => _componentes[indice];

    public IIteradorComponentes CrearIterador(int cantidad = 1) => new IteradorComponentesKit(this, cantidad);

    public void Aceptar(IVisitanteCatalogo visitante, int multiplicador = 1)
    {
        visitante.VisitarKit(this, multiplicador);
        foreach (var componente in _componentes)
            componente.Elemento.Aceptar(visitante, multiplicador * componente.Cantidad);
        visitante.SalirDeKit(this, multiplicador);
    }

    public override string ToString() => $"{Sku} - {Nombre}";
}
