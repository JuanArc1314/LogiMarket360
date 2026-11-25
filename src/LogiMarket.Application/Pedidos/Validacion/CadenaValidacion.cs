using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Pedidos.Validacion;

public sealed record RechazoValidacion(string Eslabon, string Codigo, string Mensaje)
{
    public override string ToString() => $"[{Eslabon}] {Codigo}: {Mensaje}";
}

//resultado estructurado: causa del rechazo y orden de eslabones evaluados (observable)
public sealed record ResultadoValidacion(RechazoValidacion? Rechazo, IReadOnlyList<string> EslabonesEvaluados)
{
    public bool EsValido => Rechazo is null;
}

public sealed class ContextoValidacionPedido
{
    public required Pedido Pedido { get; init; }
    public Cliente? Cliente { get; init; }
    public IReadOnlyDictionary<string, ProductoSimple> Productos { get; init; } =
        new Dictionary<string, ProductoSimple>();
    public UsuarioSesion? Usuario { get; init; }
}

//patron chain of responsibility - manejador abstracto
//cada eslabon evalua su regla
//si falla devuelve un rechazo estructurado y detiene la cadena, si no delega en el siguiente
//no reemplaza las invariantes del dominio (que se vuelven a verificar en las entidades)
public abstract class ValidadorPedido
{
    private ValidadorPedido? _siguiente;

    public abstract string Nombre { get; }

    //enlaza y devuelve el siguiente para encadenar con fluidez
    public ValidadorPedido EnlazarCon(ValidadorPedido siguiente)
    {
        _siguiente = siguiente;
        return siguiente;
    }

    public ResultadoValidacion Validar(ContextoValidacionPedido contexto)
    {
        var traza = new List<string>();
        return Procesar(contexto, traza);
    }

    private ResultadoValidacion Procesar(ContextoValidacionPedido contexto, List<string> traza)
    {
        traza.Add(Nombre);
        var rechazo = Evaluar(contexto);
        if (rechazo is not null) return new ResultadoValidacion(rechazo, traza);
        return _siguiente is null ? new ResultadoValidacion(null, traza) : _siguiente.Procesar(contexto, traza);
    }

    //devuelve null si la regla se cumple
    protected abstract RechazoValidacion? Evaluar(ContextoValidacionPedido contexto);

    protected RechazoValidacion Rechazar(string codigo, string mensaje) => new(Nombre, codigo, mensaje);
}

public sealed class ClienteActivoValidador : ValidadorPedido
{
    public override string Nombre => "1-ClienteActivo";

    protected override RechazoValidacion? Evaluar(ContextoValidacionPedido c)
    {
        if (c.Cliente is null || c.Cliente.Id != c.Pedido.ClienteId)
            return Rechazar("CLIENTE_INEXISTENTE", "El pedido no tiene un cliente registrado válido.");
        if (!c.Cliente.Activo)
            return Rechazar("CLIENTE_INACTIVO", $"El cliente {c.Cliente.Nombre} está inactivo.");
        return null;
    }
}

public sealed class LineasCantidadesValidador : ValidadorPedido
{
    public const int MaximoPorLinea = 999;
    public override string Nombre => "2-LineasCantidades";

    protected override RechazoValidacion? Evaluar(ContextoValidacionPedido c)
    {
        if (c.Pedido.Lineas.Count == 0)
            return Rechazar("PEDIDO_SIN_LINEAS", "El pedido no tiene líneas.");
        var invalida = c.Pedido.Lineas.FirstOrDefault(l => l.Cantidad is <= 0 or > MaximoPorLinea);
        return invalida is null
            ? null
            : Rechazar("CANTIDAD_INVALIDA", $"La cantidad de {invalida.Sku} debe estar entre 1 y {MaximoPorLinea}.");
    }
}

public sealed class StockDisponibleValidador : ValidadorPedido
{
    public override string Nombre => "3-ReglasStock";

    protected override RechazoValidacion? Evaluar(ContextoValidacionPedido c)
    {
        var faltantes = new List<string>();
        foreach (var (sku, requerido) in c.Pedido.RequerimientosStock())
        {
            if (!c.Productos.TryGetValue(sku, out var producto))
                return Rechazar("PRODUCTO_INEXISTENTE", $"El producto {sku} no existe en el catálogo.");
            if (!producto.Activo)
                return Rechazar("PRODUCTO_INACTIVO", $"El producto {sku} está inactivo.");
            if (producto.Stock < requerido)
                faltantes.Add($"{sku} (requiere {requerido}, hay {producto.Stock})");
        }
        return faltantes.Count == 0
            ? null
            : Rechazar("STOCK_INSUFICIENTE", "Stock insuficiente: " + string.Join("; ", faltantes));
    }
}

public sealed class CondicionesConfirmacionValidador : ValidadorPedido
{
    public override string Nombre => "4-AutorizacionCondiciones";

    protected override RechazoValidacion? Evaluar(ContextoValidacionPedido c)
    {
        if (c.Usuario is null || !MatrizPermisos.Permite(c.Usuario.Rol, Permiso.CobrarPedidos))
            return Rechazar("NO_AUTORIZADO", $"El rol {c.Usuario?.Rol.ToString() ?? "anónimo"} no puede confirmar cobros.");
        if (c.Pedido.Estado is not (EstadoPedido.BORRADOR or EstadoPedido.VALIDADO))
            return Rechazar("ESTADO_NO_CONFIRMABLE", $"Un pedido {c.Pedido.Estado} no puede confirmarse.");
        return null;
    }
}

//ensambla las cadenas en el orden exigido: cliente -> lineas -> stock ->
//autorizacion/condiciones
public sealed class FabricaCadenaValidacion
{
    public ValidadorPedido CrearParaValidacion()
    {
        var inicio = new ClienteActivoValidador();
        inicio.EnlazarCon(new LineasCantidadesValidador())
              .EnlazarCon(new StockDisponibleValidador());
        return inicio;
    }

    public ValidadorPedido CrearParaConfirmacion()
    {
        var inicio = new ClienteActivoValidador();
        inicio.EnlazarCon(new LineasCantidadesValidador())
              .EnlazarCon(new StockDisponibleValidador())
              .EnlazarCon(new CondicionesConfirmacionValidador());
        return inicio;
    }
}
