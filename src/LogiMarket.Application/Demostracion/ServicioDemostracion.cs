using LogiMarket.Application.Comun;
using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Application.Demostracion;

/// Operaciones de la pantalla de demostración (solo ADMIN): cambiar proveedor simulado, inducir fallo
/// de cobro o de notificador, desplazar días y restablecer la base controlada. Todo queda auditado.
public sealed class ServicioDemostracion
{
    private readonly ConfiguracionDemostracion _configuracion;
    private readonly Autorizador _autorizador;
    private readonly IRestablecedorDatos _restablecedor;
    private readonly ServicioAuditoria _auditoria;
    private readonly IBitacoraOperativa _bitacora;

    public ServicioDemostracion(ConfiguracionDemostracion configuracion, Autorizador autorizador,
        IRestablecedorDatos restablecedor, ServicioAuditoria auditoria, IBitacoraOperativa bitacora)
    {
        _configuracion = configuracion;
        _autorizador = autorizador;
        _restablecedor = restablecedor;
        _auditoria = auditoria;
        _bitacora = bitacora;
    }

    public ConfiguracionDemostracion Configuracion => _configuracion;

    public Task AplicarAsync(Action<ConfiguracionDemostracion> cambio, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.AdministrarDemostracion);
        cambio(_configuracion);
        return _auditoria.RegistrarAsync(CategoriaAuditoria.SISTEMA, "DEMO_CONFIG", _configuracion.Describir(), ct: ct);
    }

    public async Task RestablecerBaseControladaAsync(CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.AdministrarDemostracion);
        await _restablecedor.RestablecerAsync(ct);
        _bitacora.Registrar(CategoriasBitacora.Outbox, "RESET", "Base de pruebas restablecida (migraciones + seed).");
    }
}
