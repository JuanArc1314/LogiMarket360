using System.Security.Cryptography;
using LogiMarket.Application.Seguridad;

namespace LogiMarket.Infrastructure.Seguridad;

//hash de contraseñas con PBKDF2-SHA256 + sal aleatoria
//nunca se guarda texto claro
internal sealed class HasherPbkdf2 : IHasherContrasenas
{
    private const string Prefijo = "PBKDF2-SHA256";
    private const int TamanoSal = 16;
    private const int TamanoHash = 32;
    private readonly int _iteraciones;

    public HasherPbkdf2(OpcionesInfraestructura opciones) => _iteraciones = opciones.IteracionesHash;

    public string Calcular(string contrasena)
    {
        ArgumentException.ThrowIfNullOrEmpty(contrasena);
        var sal = RandomNumberGenerator.GetBytes(TamanoSal);
        var hash = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, _iteraciones, HashAlgorithmName.SHA256, TamanoHash);
        return $"{Prefijo}${_iteraciones}${Convert.ToBase64String(sal)}${Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string contrasena, string hashAlmacenado)
    {
        var partes = hashAlmacenado.Split('$');
        if (partes.Length != 4 || partes[0] != Prefijo || !int.TryParse(partes[1], out var iteraciones)) return false;
        try
        {
            var sal = Convert.FromBase64String(partes[2]);
            var esperado = Convert.FromBase64String(partes[3]);
            var calculado = Rfc2898DeriveBytes.Pbkdf2(contrasena, sal, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
            return CryptographicOperations.FixedTimeEquals(calculado, esperado);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
