using LogiMarket.Application.Eventos;
using LogiMarket.Application.Inventario;
using LogiMarket.Application.Pagos;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Catalogo;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Comun;
using LogiMarket.Domain.Eventos;
using LogiMarket.Domain.Operaciones;
using LogiMarket.Domain.Pedidos;

namespace LogiMarket.Application.Devoluciones;

public sealed record SolicitudDevolucion(int PedidoId, MotivoDevolucion Motivo, bool ProductoSinUso, string Observacion = "");

public sealed record ResultadoDevolucion(
    bool Aceptada, string Codigo, string Mensaje, decimal Reembolso, string? ReferenciaReembolso,
    int DiasDesdeEntrega, IReadOnlyList<string> PasosEjecutados);

public sealed class ContextoDevolucion
{
    public required Pedido Pedido { get; init; }
    public required Cliente? Cliente { get; init; }
    public required Pago Pago { get; init; }
    public required SolicitudDevolucion Solicitud { get; init; }
    public required IReadOnlyDictionary<string, ProductoSimple> Productos { get; init; }
    public required DateTime Ahora { get; init; }
    public required string Usuario { get; init; }

    public int DiasDesdeEntrega => Pedido.FechaEntrega is { } entrega ? (Ahora.Date - entrega.Date).Days : -1;
}

/// Colaboradores comunes de todas las variantes de devolución.
public sealed record ColaboradoresDevolucion(
    IRepositorioDevoluciones Devoluciones, IRepositorioOutbox Outbox, IPasarelaReembolso Reembolsos,
    ServicioInventario Inventario, SerializadorEventos Serializador);

/// PATRÓN TEMPLATE METHOD. ProcesarAsync fija la secuencia invariante:
/// verificar elegibilidad → inspeccionar condición → calcular reembolso → registrar devolución/stock → generar evento.
/// Las variantes (Defecto, Arrepentimiento) solo personalizan los pasos permitidos.
public abstract class ProcesoDevolucion
{
    private readonly ColaboradoresDevolucion _c;

    protected ProcesoDevolucion(ColaboradoresDevolucion colaboradores) => _c = colaboradores;

    public abstract MotivoDevolucion Motivo { get; }

    /// Plazo máximo en días desde la entrega.
    protected abstract int DiasMaximos { get; }

    /// MÉTODO PLANTILLA: la estructura es la misma para toda variante.
    public async Task<ResultadoDevolucion> ProcesarAsync(ContextoDevolucion ctx, CancellationToken ct = default)
    {
        var pasos = new List<string>();

        pasos.Add("1-VerificarElegibilidad");
        if (VerificarElegibilidad(ctx) is { } noElegible) return Rechazo(noElegible, ctx, pasos);

        pasos.Add("2-InspeccionarCondicion");
        if (InspeccionarCondicion(ctx) is { } condicion) return Rechazo(condicion, ctx, pasos);

        pasos.Add("3-CalcularReembolso");
        var monto = CalcularReembolso(ctx);

        pasos.Add("4-RegistrarDevolucionYStock");
        var devolucion = await RegistrarDevolucionYStockAsync(ctx, monto, ct);

        pasos.Add("5-GenerarEvento");
        GenerarEvento(ctx, devolucion);

        return new ResultadoDevolucion(true, "ACEPTADA",
            $"Devolución {Motivo} aceptada: reembolso {Dinero.Formatear(monto)} (sin tarifa de envío).",
            monto, devolucion.ReferenciaReembolso, ctx.DiasDesdeEntrega, pasos);
    }

    // ---- Pasos con comportamiento común (pueden especializarse) ----

    protected virtual (string Codigo, string Mensaje)? VerificarElegibilidad(ContextoDevolucion ctx)
    {
        if (ctx.Pedido.Estado != EstadoPedido.ENTREGADO)
            return ("NO_ENTREGADO", $"Solo se devuelven pedidos ENTREGADOS (actual: {ctx.Pedido.Estado}).");
        if (ctx.Pedido.Devuelto)
            return ("YA_DEVUELTO", "El pedido ya tiene una devolución registrada.");
        if (ctx.DiasDesdeEntrega < 0)
            return ("FECHA_INVALIDA", "La fecha de la solicitud es anterior a la entrega.");
        if (ctx.DiasDesdeEntrega > DiasMaximos)
            return ("FUERA_DE_PLAZO", $"{Motivo} admite hasta {DiasMaximos} días; han pasado {ctx.DiasDesdeEntrega}.");
        return null;
    }

    protected abstract (string Codigo, string Mensaje)? InspeccionarCondicion(ContextoDevolucion ctx);

    /// Importe de productos después del descuento, sin la tarifa de envío. 
    protected virtual decimal CalcularReembolso(ContextoDevolucion ctx) => ctx.Pedido.ImporteProductosNeto;

    /// Gancho: si las unidades vuelven al stock vendible. }
    protected virtual bool ReintegraStock => true;

    // ---- Pasos invariantes ----

    private async Task<Devolucion> RegistrarDevolucionYStockAsync(ContextoDevolucion ctx, decimal monto, CancellationToken ct)
    {
        var reembolso = await _c.Reembolsos.ReembolsarAsync(ctx.Pago.Referencia, monto, $"DEV-{ctx.Pedido.Codigo}", ct);
        if (!reembolso.Aprobado)
            throw new ExcepcionDominio("REEMBOLSO_RECHAZADO", reembolso.Motivo ?? "El reembolso simulado fue rechazado.");

        ctx.Pedido.RegistrarDevolucion(ctx.Ahora, ctx.Usuario, Motivo.ToString());
        if (ReintegraStock)
            _c.Inventario.ReintegrarPorDevolucion(ctx.Pedido, ctx.Productos, ctx.Ahora, ctx.Usuario);

        var devolucion = new Devolucion(ctx.Pedido.Id, Motivo, ctx.DiasDesdeEntrega, monto, reembolso.Referencia!,
            ReintegraStock, ctx.Ahora, ctx.Usuario);
        _c.Devoluciones.Agregar(devolucion);
        return devolucion;
    }

    private void GenerarEvento(ContextoDevolucion ctx, Devolucion devolucion) =>
        _c.Outbox.Agregar(_c.Serializador.CrearRegistro(new DevolucionAceptada(Guid.NewGuid(), ctx.Ahora, ctx.Pedido.Id,
            ctx.Pedido.Codigo, ctx.Pedido.ClienteNombre, ctx.Cliente?.Email ?? "", ctx.Cliente?.Telefono ?? "",
            Motivo.ToString(), devolucion.MontoReembolso, devolucion.ReferenciaReembolso)));

    private ResultadoDevolucion Rechazo((string Codigo, string Mensaje) causa, ContextoDevolucion ctx, List<string> pasos) =>
        new(false, causa.Codigo, causa.Mensaje, 0m, null, ctx.DiasDesdeEntrega, pasos);
}

/// DEFECTO: hasta 30 días; se acepta con el producto defectuoso (no exige "sin uso"). 
public sealed class DevolucionPorDefecto(ColaboradoresDevolucion colaboradores) : ProcesoDevolucion(colaboradores)
{
    public override MotivoDevolucion Motivo => MotivoDevolucion.DEFECTO;
    protected override int DiasMaximos => 30;
    protected override (string Codigo, string Mensaje)? InspeccionarCondicion(ContextoDevolucion ctx) => null;
}

/// ARREPENTIMIENTO: hasta 7 días y siempre que el producto esté sin uso. 
public sealed class DevolucionPorArrepentimiento(ColaboradoresDevolucion colaboradores) : ProcesoDevolucion(colaboradores)
{
    public override MotivoDevolucion Motivo => MotivoDevolucion.ARREPENTIMIENTO;
    protected override int DiasMaximos => 7;

    protected override (string Codigo, string Mensaje)? InspeccionarCondicion(ContextoDevolucion ctx) =>
        ctx.Solicitud.ProductoSinUso ? null : ("PRODUCTO_USADO", "Por arrepentimiento solo se acepta producto sin uso.");
}
