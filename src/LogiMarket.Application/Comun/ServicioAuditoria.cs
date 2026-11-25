using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Application.Comun;

//registra auditoria persistente fuera de las transacciones de negocio (por ejemplo intentos
//rechazados, inicios de sesion)
//los eventos de negocio exitosos se auditan via outbox -> observer
public sealed class ServicioAuditoria
{
    private readonly IRepositorioAuditoria _repositorio;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly ISesionUsuario _sesion;
    private readonly IReloj _reloj;

    public ServicioAuditoria(IRepositorioAuditoria repositorio, IUnidadDeTrabajo unidad, ISesionUsuario sesion, IReloj reloj)
    {
        _repositorio = repositorio;
        _unidad = unidad;
        _sesion = sesion;
        _reloj = reloj;
    }

    public async Task RegistrarAsync(CategoriaAuditoria categoria, string accion, string detalle,
        string? referencia = null, string? usuario = null, CancellationToken ct = default)
    {
        _repositorio.Agregar(new Auditoria(_reloj.Ahora, usuario ?? _sesion.NombreUsuario, categoria, accion, detalle, referencia));
        await _unidad.GuardarCambiosAsync(ct);
    }
}
