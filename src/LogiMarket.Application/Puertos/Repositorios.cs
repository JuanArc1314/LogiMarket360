using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Seguridad;

namespace LogiMarket.Application.Puertos;

//patron repository: interfaces del nucleo (ISP: pequeñas y por agregado)
//adaptadores EF Core en infrastructure

public interface IRepositorioCatalogo
{
    Task<ProductoSimple?> ObtenerProductoAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, ProductoSimple>> ObtenerProductosAsync(IEnumerable<string> skus, CancellationToken ct = default);
    Task<IReadOnlyList<ProductoSimple>> ListarProductosAsync(CancellationToken ct = default);
    Task<KitCompuesto?> ObtenerKitAsync(string sku, CancellationToken ct = default);
    Task<IReadOnlyList<KitCompuesto>> ListarKitsAsync(CancellationToken ct = default);
    Task<IElementoCatalogo?> ObtenerElementoAsync(string sku, CancellationToken ct = default);
    Task<bool> ExisteSkuAsync(string sku, CancellationToken ct = default);
    void AgregarProducto(ProductoSimple producto);
    void AgregarKit(KitCompuesto kit);
}

public interface IRepositorioClientes
{
    Task<Cliente?> ObtenerAsync(int id, CancellationToken ct = default);
    Task<bool> ExisteIdentificacionAsync(string identificacion, int? excluirId = null, CancellationToken ct = default);
    void Agregar(Cliente cliente);
}

public interface IRepositorioPedidos
{
    Task<Pedido?> ObtenerAsync(int id, CancellationToken ct = default);
    Task<Pedido?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default);
    void Agregar(Pedido pedido);
}

public interface IRepositorioPagos
{
    Task<Pago?> ObtenerPorClaveAsync(string claveIdempotencia, CancellationToken ct = default);
    Task<Pago?> ObtenerPorPedidoAsync(int pedidoId, CancellationToken ct = default);
    void Agregar(Pago pago);
}

public interface IRepositorioDespachos
{
    Task<Despacho?> ObtenerPorPedidoAsync(int pedidoId, CancellationToken ct = default);
    void Agregar(Despacho despacho);
}

public interface IRepositorioDevoluciones
{
    Task<bool> ExisteParaPedidoAsync(int pedidoId, CancellationToken ct = default);
    void Agregar(Devolucion devolucion);
}

public interface IRepositorioMovimientos
{
    void Agregar(MovimientoInventario movimiento);
}

public interface IRepositorioAuditoria
{
    Task<bool> ExisteAsync(string referencia, string accion, CancellationToken ct = default);
    void Agregar(Auditoria registro);
}

public interface IRepositorioOutbox
{
    Task<IReadOnlyList<Guid>> ObtenerIdsPendientesAsync(int maximo, CancellationToken ct = default);
    Task<EventoOutbox?> ObtenerAsync(Guid id, CancellationToken ct = default);
    void Agregar(EventoOutbox evento);
}

public interface IRepositorioUsuarios
{
    Task<Usuario?> ObtenerPorNombreAsync(string nombreUsuario, CancellationToken ct = default);
}

public interface IRepositorioNotificaciones
{
    Task<bool> ExisteAsync(Guid eventoId, string canal, CancellationToken ct = default);
    void Agregar(Notificacion notificacion);
}

//unit of work materializado por el DbContext y una transaccion real de base de datos
public interface IUnidadDeTrabajo
{
    //ejecuta la operacion dentro de una transaccion: si termina sin excepcion guarda y confirma
    //ante cualquier excepcion revierte y descarta los cambios en memoria
    Task<T> EjecutarEnTransaccionAsync<T>(Func<CancellationToken, Task<T>> operacion, CancellationToken ct = default);

    Task GuardarCambiosAsync(CancellationToken ct = default);

    void DescartarCambios();
}

//puerto para restablecer la base de pruebas controlada (solo demostracion/ADMIN)
public interface IRestablecedorDatos
{
    Task RestablecerAsync(CancellationToken ct = default);
}
