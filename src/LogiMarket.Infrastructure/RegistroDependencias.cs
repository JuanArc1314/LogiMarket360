using LogiMarket.Application.Catalogo;
using LogiMarket.Application.Clientes;
using LogiMarket.Application.Comandos;
using LogiMarket.Application.Comprobantes;
using LogiMarket.Application.Comun;
using LogiMarket.Application.Confirmacion;
using LogiMarket.Application.Consultas;
using LogiMarket.Application.Demostracion;
using LogiMarket.Application.Devoluciones;
using LogiMarket.Application.Eventos;
using LogiMarket.Application.Inventario;
using LogiMarket.Application.Logistica;
using LogiMarket.Application.Metricas;
using LogiMarket.Application.Notificaciones;
using LogiMarket.Application.Pagos;
using LogiMarket.Application.Pedidos;
using LogiMarket.Application.Pedidos.Validacion;
using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Logistica;
using LogiMarket.Domain.Pedidos;
using LogiMarket.Domain.Promociones;
using LogiMarket.Infrastructure.Notificaciones;
using LogiMarket.Infrastructure.Pagos;
using LogiMarket.Infrastructure.Persistencia;
using LogiMarket.Infrastructure.Salidas;
using LogiMarket.Infrastructure.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LogiMarket.Infrastructure;

/// Registro de dependencias de Application + Infrastructure. Lo invoca el composition root
/// (WinForms/Program) y las pruebas de integración. Todas las dependencias entran por constructor (DIP).
public static class RegistroDependencias
{
    public static IServiceCollection AgregarLogiMarket(this IServiceCollection services, OpcionesInfraestructura opciones)
    {
        // ---------- Singletons (estado de la sesión de escritorio) ----------
        services.AddSingleton(opciones);
        services.AddSingleton<ConfiguracionDemostracion>();
        services.AddSingleton<IReloj, RelojSistema>();
        services.AddSingleton<ISesionUsuario, SesionUsuario>();
        services.AddSingleton<IBitacoraOperativa, BitacoraOperativaArchivo>();
        services.AddSingleton<MetricasSesion>();                  // PATRÓN SINGLETON (única instancia en DI)
        services.AddSingleton<InvocadorComandos>();
        services.AddSingleton<IHasherContrasenas, HasherPbkdf2>();
        services.AddSingleton<Autorizador>();
        services.TryAddSingleton<IVisorDocumentos, VisorDocumentosMemoria>();

        // Dominio puro: Strategy y Abstract Factory registrados como colecciones (OCP).
        services.AddSingleton<IPoliticaDescuento, Mayorista15Politica>();
        services.AddSingleton<IPoliticaDescuento, Vip10Politica>();
        services.AddSingleton<IPoliticaDescuento, SinDescuentoPolitica>();
        services.AddSingleton(sp => new SelectorPoliticaDescuento(sp.GetServices<IPoliticaDescuento>()));
        services.AddSingleton<CalculadoraPedido>();
        services.AddSingleton<IFabricaEntrega, FabricaEntregaEstandar>();
        services.AddSingleton<IFabricaEntrega, FabricaEntregaExpres>();
        services.AddSingleton<SelectorFabricaEntrega>();
        services.AddSingleton<ValoradorPedidos>();
        services.AddSingleton<FabricaCadenaValidacion>();
        services.AddSingleton<SerializadorEventos>();

        // ---------- Pagos: Proxy → Decorator → (Conmutador) → Adapter | Alterno ----------
        services.AddSingleton(sp =>
        {
            var configuracion = sp.GetRequiredService<ConfiguracionDemostracion>();
            return new PasarelaLegadaSimulada(() => configuracion.SimularRechazoCobro ? "05" : null);
        });
        services.AddSingleton<PasarelaLegadaAdapter>();
        services.AddSingleton<PasarelaAlternaSimulada>();
        services.AddSingleton<PasarelaConmutable>();
        services.AddSingleton<IPasarelaPago>(sp =>
        {
            var bitacora = sp.GetRequiredService<IBitacoraOperativa>();
            IPasarelaPago real = sp.GetRequiredService<PasarelaConmutable>();
            IPasarelaPago auditada = new AuditoriaPagoDecorator(real, bitacora);
            return new PagoAutorizadoProxy(auditada, sp.GetRequiredService<ISesionUsuario>(), bitacora);
        });
        services.AddSingleton<IPasarelaReembolso, ServicioReembolsoSimulado>();

        // ---------- Persistencia (scoped: un DbContext por operación) ----------
        services.AddDbContext<LogiMarketDbContext>(o => o.UseSqlite(opciones.CadenaConexion));
        services.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajoEf>();
        services.AddScoped<IRepositorioCatalogo, RepositorioCatalogoEf>();
        services.AddScoped<IRepositorioClientes, RepositorioClientesEf>();
        services.AddScoped<IRepositorioPedidos, RepositorioPedidosEf>();
        services.AddScoped<IRepositorioPagos, RepositorioPagosEf>();
        services.AddScoped<IRepositorioDespachos, RepositorioDespachosEf>();
        services.AddScoped<IRepositorioDevoluciones, RepositorioDevolucionesEf>();
        services.AddScoped<IRepositorioMovimientos, RepositorioMovimientosEf>();
        services.AddScoped<IRepositorioAuditoria, RepositorioAuditoriaEf>();
        services.AddScoped<IRepositorioOutbox, RepositorioOutboxEf>();
        services.AddScoped<IRepositorioUsuarios, RepositorioUsuariosEf>();
        services.AddScoped<IRepositorioNotificaciones, RepositorioNotificacionesEf>();
        services.AddScoped<IConsultasOperativas, ConsultasOperativasEf>();
        services.AddScoped<SemillaDatos>();
        services.AddScoped<InicializadorBaseDatos>();
        services.AddScoped<IRestablecedorDatos>(sp => sp.GetRequiredService<InicializadorBaseDatos>());

        // ---------- Notificaciones (Factory Method) y eventos (Observer + Outbox) ----------
        services.AddScoped<EntornoNotificacion>();
        services.AddScoped<CreadorNotificador, CreadorNotificadorEmail>();
        services.AddScoped<CreadorNotificador, CreadorNotificadorSms>();
        services.AddScoped<SelectorCreadorNotificador>();
        services.AddScoped<IObservadorEventos, NotificacionObserver>();
        services.AddScoped<IObservadorEventos, MetricasObserver>();
        services.AddScoped<IObservadorEventos, AuditoriaObserver>();
        services.AddScoped<IPublicadorEventos, PublicadorEventos>();
        services.AddScoped<ProcesadorOutbox>();

        // ---------- Casos de uso ----------
        services.AddScoped<ServicioAuditoria>();
        services.AddScoped<ServicioInventario>();
        services.AddScoped<ServicioAutenticacion>();
        services.AddScoped<ServicioCatalogo>();
        services.AddScoped<ServicioClientes>();
        services.AddScoped<ServicioPedidos>();
        services.AddScoped<RepositoriosConfirmacion>();
        services.AddScoped<ConfirmacionPedidoFacade>();
        services.AddScoped<ServicioLogistica>();
        services.AddScoped<ColaboradoresDevolucion>();
        services.AddScoped<ProcesoDevolucion, DevolucionPorDefecto>();
        services.AddScoped<ProcesoDevolucion, DevolucionPorArrepentimiento>();
        services.AddScoped<ServicioDevoluciones>();
        services.AddScoped<FabricaComandos>();
        services.AddScoped<ServicioReportes>();
        services.AddScoped<ISalidaDocumento, SalidaVistaPrevia>();
        services.AddScoped<ISalidaDocumento, SalidaArchivoTxt>();
        services.AddScoped<ServicioComprobantes>();
        services.AddScoped<ServicioDemostracion>();

        return services;
    }
}
