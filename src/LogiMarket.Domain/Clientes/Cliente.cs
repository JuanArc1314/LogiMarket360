using System.Net.Mail;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Domain.Clientes;

public enum CategoriaCliente
{
    NORMAL,
    VIP
}

//cliente de LogiMarket
//solo los clientes activos pueden confirmar pedidos
public sealed class Cliente
{
    private Cliente() { } //EF Core

    public Cliente(string identificacion, string nombre, string email, string telefono, CategoriaCliente categoria)
    {
        Identificacion = Guardia.Texto(identificacion, "La identificación", 20);
        Activo = true;
        Actualizar(nombre, email, telefono, categoria);
    }

    public int Id { get; private set; }
    public string Identificacion { get; private set; } = string.Empty;
    public string Nombre { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string Telefono { get; private set; } = string.Empty;
    public CategoriaCliente Categoria { get; private set; }
    public bool Activo { get; private set; }

    public void Actualizar(string nombre, string email, string telefono, CategoriaCliente categoria)
    {
        Nombre = Guardia.Texto(nombre, "El nombre");
        Email = ValidarEmail(email);
        Telefono = Guardia.TextoOpcional(telefono, 20);
        if (!Enum.IsDefined(categoria))
            throw new ExcepcionDominio("CATEGORIA_INVALIDA", "Categoría de cliente no válida.");
        Categoria = categoria;
    }

    public void Desactivar() => Activo = false;
    public void Activar() => Activo = true;

    private static string ValidarEmail(string? email)
    {
        var valor = Guardia.TextoOpcional(email, 120);
        if (valor.Length == 0) return valor;
        if (!MailAddress.TryCreate(valor, out _))
            throw new ExcepcionDominio("EMAIL_INVALIDO", "El correo electrónico no tiene un formato válido.");
        return valor;
    }

    public override string ToString() => $"{Nombre} ({Categoria})";
}
