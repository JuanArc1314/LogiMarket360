using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Seguridad;

public enum RolUsuario
{
    ADMIN,
    VENTAS,
    BODEGA,
    CONSULTA
}

/// Usuario local. La contraseña solo se conserva como hash (nunca en texto claro).
public sealed class Usuario
{
    private Usuario() { } // EF Core

    public Usuario(string nombreUsuario, string nombreCompleto, RolUsuario rol, string hashContrasena)
    {
        NombreUsuario = Guardia.Texto(nombreUsuario, "El usuario", 40).ToLowerInvariant();
        NombreCompleto = Guardia.Texto(nombreCompleto, "El nombre completo");
        Rol = rol;
        CambiarHash(hashContrasena);
        Activo = true;
    }

    public int Id { get; private set; }
    public string NombreUsuario { get; private set; } = string.Empty;
    public string NombreCompleto { get; private set; } = string.Empty;
    public RolUsuario Rol { get; private set; }
    public string HashContrasena { get; private set; } = string.Empty;
    public bool Activo { get; private set; }

    public void CambiarHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || hash.Length < 20)
            throw new ExcepcionDominio("HASH_INVALIDO", "Se requiere un hash de contraseña válido.");
        HashContrasena = hash;
    }

    public void Desactivar() => Activo = false;
}
