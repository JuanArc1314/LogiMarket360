using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Pedidos.Estados;

namespace LogiMarket.Domain.Pedidos;

/// Agregado raíz Pedido. Conserva sus invariantes: las transiciones se delegan en objetos de
/// estado (PATRÓN STATE), la edición solo es posible en BORRADOR y no hay setters públicos.
/// Implementa PROTOTYPE Clonar y es el ORIGINADOR del PATRÓN MEMENTO.
public sealed class Pedido : IPrototipo<Pedido>
{
    private readonly List<PedidoDetalle> _lineas = new();
    private readonly List<PedidoHistorialEstado> _historial = new();

    private Pedido() { } // EF Core

    /// Solo el <see cref="PedidoBuilder"/> crea pedidos nuevos (garantiza cliente y líneas).
    internal Pedido(Cliente cliente, TipoEntrega entrega, DateTime fecha, string usuario)
    {
        AsignarCliente(cliente);
        Entrega = entrega;
        FechaCreacion = fecha;
        Estado = EstadoPedido.BORRADOR;
        PoliticaDescuento = "SIN_VALORAR";
        _historial.Add(new PedidoHistorialEstado(null, EstadoPedido.BORRADOR, fecha, usuario, "Pedido creado"));
    }

    public int Id { get; private set; }
    public string Codigo { get; private set; } = string.Empty;
    public int ClienteId { get; private set; }
    public string ClienteNombre { get; private set; } = string.Empty;
    public CategoriaCliente CategoriaCliente { get; private set; }
    public TipoEntrega Entrega { get; private set; }
    public EstadoPedido Estado { get; private set; }
    public DateTime FechaCreacion { get; private set; }

    public decimal Subtotal { get; private set; }
    public string PoliticaDescuento { get; private set; } = string.Empty;
    public decimal Descuento { get; private set; }
    public decimal TarifaEnvio { get; private set; }
    public int PlazoEntregaDias { get; private set; }
    public decimal Total { get; private set; }

    public string? ClaveIdempotencia { get; private set; }
    public DateTime? FechaPago { get; private set; }
    public DateTime? FechaDespacho { get; private set; }
    public DateTime? FechaEntrega { get; private set; }
    public bool Devuelto { get; private set; }
    public DateTime? FechaDevolucion { get; private set; }

    /// Token de concurrencia optimista.
    public int Version { get; private set; }

    public IReadOnlyList<PedidoDetalle> Lineas => _lineas.AsReadOnly();
    public IReadOnlyList<PedidoHistorialEstado> Historial => _historial.AsReadOnly();

    public int UnidadesFisicas => _lineas.Sum(l => l.UnidadesFisicas);
    public decimal PesoTotalKg => _lineas.Sum(l => l.PesoUnitarioKg * l.Cantidad);
    public decimal ImporteProductosNeto => Dinero.Redondear(Subtotal - Descuento);

    ///Estado actual como objeto (PATRÓN STATE).
    public EstadoPedidoBase EstadoActual => EstadosPedido.Para(Estado);

    // -------------------- Edición (solo BORRADOR) --------------------

    public void AgregarLinea(IElementoCatalogo elemento, int cantidad)
    {
        EstadoActual.ExigirEdicion();
        ArgumentNullException.ThrowIfNull(elemento);
        var existente = BuscarLinea(elemento.Sku);
        if (existente is not null)
            existente.Incrementar(cantidad);
        else
            _lineas.Add(PedidoDetalle.DesdeCatalogo(elemento, cantidad));
        RecalcularSubtotal();
    }

    public void CambiarCantidad(string sku, int cantidad)
    {
        EstadoActual.ExigirEdicion();
        var linea = BuscarLinea(sku) ?? throw LineaInexistente(sku);
        linea.CambiarCantidad(cantidad);
        RecalcularSubtotal();
    }

    public void QuitarLinea(string sku)
    {
        EstadoActual.ExigirEdicion();
        var linea = BuscarLinea(sku) ?? throw LineaInexistente(sku);
        _lineas.Remove(linea);
        RecalcularSubtotal();
    }

    public void CambiarEntrega(TipoEntrega entrega)
    {
        EstadoActual.ExigirEdicion();
        Entrega = entrega;
    }

    public void CambiarCliente(Cliente cliente)
    {
        EstadoActual.ExigirEdicion();
        AsignarCliente(cliente);
    }

    /// Reemplaza todas las líneas tomando precios actuales del catálogo (re-instantánea).
    public void ReemplazarLineas(IEnumerable<(IElementoCatalogo Elemento, int Cantidad)> lineas)
    {
        EstadoActual.ExigirEdicion();
        var nuevas = lineas.Select(l => PedidoDetalle.DesdeCatalogo(l.Elemento, l.Cantidad)).ToList();
        _lineas.Clear();
        foreach (var grupo in nuevas.GroupBy(l => l.Sku, StringComparer.OrdinalIgnoreCase))
        {
            var primera = grupo.First();
            foreach (var extra in grupo.Skip(1)) primera.Incrementar(extra.Cantidad);
            _lineas.Add(primera);
        }
        RecalcularSubtotal();
    }

    public void AsignarCodigo(string codigo)
    {
        if (!string.IsNullOrEmpty(Codigo))
            throw new ExcepcionDominio("CODIGO_ASIGNADO", "El pedido ya tiene código asignado.");
        Codigo = Guardia.Texto(codigo, "El código", 20);
    }

    /// Aplica el resultado de la calculadora (Strategy + Abstract Factory) verificando coherencia.
    public void AplicarValoracion(Valoracion valoracion)
    {
        if (Estado is not (EstadoPedido.BORRADOR or EstadoPedido.VALIDADO))
            throw new ExcepcionDominio("PEDIDO_NO_EDITABLE", $"No se puede revalorar un pedido {Estado}.");
        if (valoracion.Subtotal != CalcularSubtotal())
            throw new ExcepcionDominio("VALORACION_INCOHERENTE", "La valoración no corresponde a las líneas actuales.");
        if (valoracion.Familia != Entrega)
            throw new ExcepcionDominio("VALORACION_INCOHERENTE", "La tarifa no corresponde a la forma de entrega.");

        Subtotal = valoracion.Subtotal;
        PoliticaDescuento = valoracion.Politica;
        Descuento = valoracion.Descuento;
        TarifaEnvio = valoracion.Tarifa;
        PlazoEntregaDias = valoracion.PlazoDias;
        Total = valoracion.Total;
    }

    /// Cantidades por SKU final a descontar del inventario (instantánea de composición).
    public IReadOnlyList<(string Sku, int Cantidad)> RequerimientosStock() =>
        _lineas.SelectMany(l => l.Componentes.Select(c => (c.Sku, Cantidad: c.CantidadPorUnidad * l.Cantidad)))
            .GroupBy(x => x.Sku, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, g.Sum(x => x.Cantidad)))
            .ToList();

    public decimal CalcularSubtotal() => Dinero.Redondear(_lineas.Sum(l => l.Subtotal));

    // -------------------- Transiciones (delegadas al State) --------------------

    public void Validar(DateTime fecha, string usuario) => EstadoActual.Validar(this, fecha, usuario);
    public void RegistrarPago(string claveIdempotencia, DateTime fecha, string usuario) =>
        EstadoActual.Pagar(this, claveIdempotencia, fecha, usuario);
    public void Despachar(DateTime fecha, string usuario) => EstadoActual.Despachar(this, fecha, usuario);
    public void Entregar(DateTime fecha, string usuario) => EstadoActual.Entregar(this, fecha, usuario);
    public void Cancelar(DateTime fecha, string usuario, string motivo) => EstadoActual.Cancelar(this, fecha, usuario, motivo);
    public void RegistrarDevolucion(DateTime fecha, string usuario, string motivo) =>
        EstadoActual.Devolver(this, fecha, usuario, motivo);

    // Operaciones internas que solo los objetos de estado pueden invocar.
    internal void TransicionarA(EstadoPedido nuevo, DateTime fecha, string usuario, string nota)
    {
        if (nuevo == Estado)
            throw new ExcepcionDominio("ESTADO_REPETIDO", $"El pedido ya se encuentra en estado {Estado}.");
        _historial.Add(new PedidoHistorialEstado(Estado, nuevo, fecha, usuario, nota));
        Estado = nuevo;
        Version++;
    }

    internal void MarcarPagado(string clave, DateTime fecha)
    {
        ClaveIdempotencia = Guardia.Texto(clave, "La clave de idempotencia", 80);
        FechaPago = fecha;
    }

    internal void MarcarDespachado(DateTime fecha) => FechaDespacho = fecha;
    internal void MarcarEntregado(DateTime fecha) => FechaEntrega = fecha;

    internal void MarcarDevuelto(DateTime fecha, string usuario, string motivo)
    {
        Devuelto = true;
        FechaDevolucion = fecha;
        _historial.Add(new PedidoHistorialEstado(Estado, Estado, fecha, usuario, $"Devolución total aceptada ({motivo})"));
        Version++;
    }

    // -------------------- PROTOTYPE --------------------

    /// Copia profunda como nuevo BORRADOR: líneas y componentes con colecciones nuevas;
    /// no copia Id, código, estado, pago, clave de idempotencia ni historial.
    public Pedido Clonar() => ClonarComoBorrador(DateTime.Now, "sistema");

    public Pedido ClonarComoBorrador(DateTime fecha, string usuario)
    {
        var copia = new Pedido
        {
            ClienteId = ClienteId,
            ClienteNombre = ClienteNombre,
            CategoriaCliente = CategoriaCliente,
            Entrega = Entrega,
            FechaCreacion = fecha,
            Estado = EstadoPedido.BORRADOR,
            PoliticaDescuento = "SIN_VALORAR"
        };
        foreach (var linea in _lineas)
            copia._lineas.Add(linea.ClonarProfundo());
        copia.RecalcularSubtotal();
        copia._historial.Add(new PedidoHistorialEstado(null, EstadoPedido.BORRADOR, fecha, usuario,
            string.IsNullOrEmpty(Codigo) ? "Creado desde plantilla" : $"Clonado desde {Codigo}"));
        return copia;
    }

    // -------------------- MEMENTO (originador) --------------------

    public PedidoMemento CrearMemento() =>
        new(ClienteId, ClienteNombre, CategoriaCliente, Entrega, _lineas.Select(l => l.ClonarProfundo()).ToList());

    public void RestaurarMemento(PedidoMemento memento)
    {
        ArgumentNullException.ThrowIfNull(memento);
        if (Estado != EstadoPedido.BORRADOR)
            throw new ExcepcionDominio("MEMENTO_BLOQUEADO",
                "Solo se puede deshacer/rehacer un borrador; un pedido confirmado no se revierte.");
        ClienteId = memento.ClienteId;
        ClienteNombre = memento.ClienteNombre;
        CategoriaCliente = memento.CategoriaCliente;
        Entrega = memento.Entrega;
        _lineas.Clear();
        foreach (var linea in memento.Lineas)
            _lineas.Add(linea.ClonarProfundo()); // el memento permanece inmutable
        RecalcularSubtotal();
    }

    // -------------------- Auxiliares --------------------

    private void AsignarCliente(Cliente cliente)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ClienteId = cliente.Id;
        ClienteNombre = cliente.Nombre;
        CategoriaCliente = cliente.Categoria;
    }

    private void RecalcularSubtotal()
    {
        Subtotal = CalcularSubtotal();
        PoliticaDescuento = "SIN_VALORAR";
        Descuento = 0m;
        TarifaEnvio = 0m;
        PlazoEntregaDias = 0;
        Total = Subtotal;
    }

    private PedidoDetalle? BuscarLinea(string sku) =>
        _lineas.FirstOrDefault(l => string.Equals(l.Sku, sku, StringComparison.OrdinalIgnoreCase));

    private static ExcepcionDominio LineaInexistente(string sku) =>
        new("LINEA_INEXISTENTE", $"El pedido no contiene la línea {sku}.");
}
