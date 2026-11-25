using LogiMarket.Application.Comun;
using LogiMarket.Application.Puertos;
using LogiMarket.Application.Seguridad;
using LogiMarket.Domain.Clientes;
using LogiMarket.Domain.Comun;

namespace LogiMarket.Application.Clientes;

public sealed record ClienteEdicionDto(int? Id, string Identificacion, string Nombre, string Email, string Telefono,
    CategoriaCliente Categoria, bool Activo);

public sealed class ServicioClientes
{
    private readonly IRepositorioClientes _clientes;
    private readonly IUnidadDeTrabajo _unidad;
    private readonly Autorizador _autorizador;

    public ServicioClientes(IRepositorioClientes clientes, IUnidadDeTrabajo unidad, Autorizador autorizador)
    {
        _clientes = clientes;
        _unidad = unidad;
        _autorizador = autorizador;
    }

    public async Task<ResultadoOperacion> GuardarAsync(ClienteEdicionDto dto, CancellationToken ct = default)
    {
        _autorizador.Exigir(Permiso.GestionarClientes);
        var id = await _unidad.EjecutarEnTransaccionAsync(async t =>
        {
            if (await _clientes.ExisteIdentificacionAsync(dto.Identificacion.Trim(), dto.Id, t))
                throw new ExcepcionDominio("IDENTIFICACION_DUPLICADA", $"Ya existe un cliente con identificación {dto.Identificacion}.");

            Cliente cliente;
            if (dto.Id is { } existente)
            {
                cliente = await _clientes.ObtenerAsync(existente, t) ?? throw new RecursoNoEncontradoException("Cliente", existente);
                cliente.Actualizar(dto.Nombre, dto.Email, dto.Telefono, dto.Categoria);
            }
            else
            {
                cliente = new Cliente(dto.Identificacion, dto.Nombre, dto.Email, dto.Telefono, dto.Categoria);
                _clientes.Agregar(cliente);
            }
            if (dto.Activo) cliente.Activar(); else cliente.Desactivar();
            await _unidad.GuardarCambiosAsync(t);
            return cliente.Id;
        }, ct);
        return ResultadoOperacion.Ok($"Cliente guardado (Id {id}).", id);
    }
}
