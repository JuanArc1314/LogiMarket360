using LogiMarket.Application.Comun;
using LogiMarket.Application.Puertos;
using LogiMarket.Domain.Operaciones;

namespace LogiMarket.Application.Seguridad;

//acceso local: verifica el hash, abre/cierra la sesion y audita los intentos
public sealed class ServicioAutenticacion
{
    private readonly IRepositorioUsuarios _usuarios;
    private readonly IHasherContrasenas _hasher;
    private readonly ISesionUsuario _sesion;
    private readonly ServicioAuditoria _auditoria;

    public ServicioAutenticacion(IRepositorioUsuarios usuarios, IHasherContrasenas hasher, ISesionUsuario sesion,
        ServicioAuditoria auditoria)
    {
        _usuarios = usuarios;
        _hasher = hasher;
        _sesion = sesion;
        _auditoria = auditoria;
    }

    public async Task<ResultadoOperacion> IniciarSesionAsync(string nombreUsuario, string contrasena, CancellationToken ct = default)
    {
        var nombre = (nombreUsuario ?? string.Empty).Trim().ToLowerInvariant();
        var usuario = nombre.Length == 0 ? null : await _usuarios.ObtenerPorNombreAsync(nombre, ct);
        if (usuario is null || !usuario.Activo || !_hasher.Verificar(contrasena ?? string.Empty, usuario.HashContrasena))
        {
            await _auditoria.RegistrarAsync(CategoriaAuditoria.SEGURIDAD, "LOGIN_FALLIDO", $"Usuario '{nombre}'", usuario: nombre, ct: ct);
            return ResultadoOperacion.Fallo("CREDENCIALES_INVALIDAS", "Usuario o contraseña incorrectos.");
        }

        _sesion.Iniciar(new UsuarioSesion(usuario.Id, usuario.NombreUsuario, usuario.NombreCompleto, usuario.Rol));
        await _auditoria.RegistrarAsync(CategoriaAuditoria.SEGURIDAD, "LOGIN", $"{usuario.NombreUsuario} ({usuario.Rol})", ct: ct);
        return ResultadoOperacion.Ok($"Bienvenido, {usuario.NombreCompleto}.");
    }

    public async Task CerrarSesionAsync(CancellationToken ct = default)
    {
        if (!_sesion.Autenticada) return;
        await _auditoria.RegistrarAsync(CategoriaAuditoria.SEGURIDAD, "LOGOUT", _sesion.NombreUsuario, ct: ct);
        _sesion.Cerrar();
    }
}
