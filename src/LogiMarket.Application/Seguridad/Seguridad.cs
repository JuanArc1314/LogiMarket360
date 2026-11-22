using LogiMarket.Application.Comun;
using LogiMarket.Domain.Seguridad;

namespace LogiMarket.Application.Seguridad;

public enum Permiso
{
    ConsultarInformacion,
    GestionarCatalogo,
    AjustarInventario,
    GestionarClientes,
    GestionarPedidos,
    CobrarPedidos,
    DespacharPedidos,
    EntregarPedidos,
    ProcesarDevoluciones,
    ConsultarAuditoria,
    AdministrarDemostracion
}

/// Matriz de permisos por operación. ADMIN: todo; VENTAS: clientes, pedidos, validación, cobro;
/// BODEGA: preparación, despacho, entrega; CONSULTA: solo lectura.
public static class MatrizPermisos
{
    private static readonly IReadOnlyDictionary<Permiso, RolUsuario[]> Matriz = new Dictionary<Permiso, RolUsuario[]>
    {
        [Permiso.ConsultarInformacion] = [RolUsuario.ADMIN, RolUsuario.VENTAS, RolUsuario.BODEGA, RolUsuario.CONSULTA],
        [Permiso.GestionarCatalogo] = [RolUsuario.ADMIN],
        [Permiso.AjustarInventario] = [RolUsuario.ADMIN, RolUsuario.BODEGA],
        [Permiso.GestionarClientes] = [RolUsuario.ADMIN, RolUsuario.VENTAS],
        [Permiso.GestionarPedidos] = [RolUsuario.ADMIN, RolUsuario.VENTAS],
        [Permiso.CobrarPedidos] = [RolUsuario.ADMIN, RolUsuario.VENTAS],
        [Permiso.DespacharPedidos] = [RolUsuario.ADMIN, RolUsuario.BODEGA],
        [Permiso.EntregarPedidos] = [RolUsuario.ADMIN, RolUsuario.BODEGA],
        [Permiso.ProcesarDevoluciones] = [RolUsuario.ADMIN, RolUsuario.VENTAS],
        [Permiso.ConsultarAuditoria] = [RolUsuario.ADMIN, RolUsuario.CONSULTA],
        [Permiso.AdministrarDemostracion] = [RolUsuario.ADMIN]
    };

    public static bool Permite(RolUsuario rol, Permiso permiso) =>
        Matriz.TryGetValue(permiso, out var roles) && roles.Contains(rol);

    public static IReadOnlyList<Permiso> PermisosDe(RolUsuario rol) =>
        Matriz.Where(p => p.Value.Contains(rol)).Select(p => p.Key).ToList();
}

public sealed record UsuarioSesion(int Id, string NombreUsuario, string NombreCompleto, RolUsuario Rol);

/// Sesión local actual (una por aplicación de escritorio).
public interface ISesionUsuario
{
    UsuarioSesion? Actual { get; }
    bool Autenticada { get; }
    string NombreUsuario { get; }
    void Iniciar(UsuarioSesion usuario);
    void Cerrar();
    event EventHandler? Cambio;
}

public sealed class SesionUsuario : ISesionUsuario
{
    private UsuarioSesion? _actual;

    public UsuarioSesion? Actual => Volatile.Read(ref _actual);
    public bool Autenticada => Actual is not null;
    public string NombreUsuario => Actual?.NombreUsuario ?? "anonimo";
    public event EventHandler? Cambio;

    public void Iniciar(UsuarioSesion usuario)
    {
        Volatile.Write(ref _actual, usuario ?? throw new ArgumentNullException(nameof(usuario)));
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    public void Cerrar()
    {
        Volatile.Write(ref _actual, null);
        Cambio?.Invoke(this, EventArgs.Empty);
    }
}

/// Autorización en la capa de aplicación (un botón deshabilitado NO es la política de seguridad).
public sealed class Autorizador
{
    private readonly ISesionUsuario _sesion;
    private readonly IBitacoraOperativa _bitacora;

    public Autorizador(ISesionUsuario sesion, IBitacoraOperativa bitacora)
    {
        _sesion = sesion;
        _bitacora = bitacora;
    }

    public bool Puede(Permiso permiso) =>
        _sesion.Actual is { } usuario && MatrizPermisos.Permite(usuario.Rol, permiso);

    /// Devuelve el usuario autorizado o lanza AccesoDenegadoException.
    public UsuarioSesion Exigir(Permiso permiso)
    {
        var usuario = _sesion.Actual;
        if (usuario is null)
        {
            _bitacora.Registrar(CategoriasBitacora.Seguridad, "DENEGADO", $"{permiso} sin sesión iniciada");
            throw new AccesoDenegadoException("Debe iniciar sesión para realizar esta operación.");
        }
        if (!MatrizPermisos.Permite(usuario.Rol, permiso))
        {
            _bitacora.Registrar(CategoriasBitacora.Seguridad, "DENEGADO", $"{permiso} denegado al rol {usuario.Rol}");
            throw new AccesoDenegadoException($"El rol {usuario.Rol} no tiene permiso para: {permiso}.");
        }
        return usuario;
    }
}

/// Puerto de hashing de contraseñas (implementado con PBKDF2 en Infrastructure).
public interface IHasherContrasenas
{
    string Calcular(string contrasena);
    bool Verificar(string contrasena, string hash);
}
