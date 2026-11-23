namespace LogiMarket.Application.Coordinacion;

/// Eventos que una vista comunica al mediador después de una operación.
public enum EventoVista
{
    SesionIniciada,
    CatalogoModificado,
    ClienteModificado,
    BorradorGuardado,
    PedidoConfirmado,
    PedidoCancelado,
    DespachoRegistrado,
    EntregaRegistrada,
    DevolucionProcesada,
    InventarioAjustado,
    OutboxProcesado,
    DatosRestablecidos
}

/// PATRÓN MEDIATOR — Colega: una vista que puede refrescarse cuando el mediador lo indique.
public interface IVistaColaboradora
{
    string NombreVista { get; }
    Task RefrescarAsync();
}

/// PATRÓN MEDIATOR — Mediador: las vistas no se llaman entre sí; solo notifican al mediador.
public interface IMediadorVistas
{
    void Registrar(IVistaColaboradora vista);
    Task NotificarAsync(IVistaColaboradora? emisor, EventoVista evento);
    IReadOnlyList<string> UltimasVistasRefrescadas { get; }
}

/// 
/// Mediador concreto con una tabla de rutas evento → vistas a refrescar. El formulario principal
/// (WinForms) lo especializa con sus nombres de vista; las pruebas lo usan con vistas dobles.
/// 
public class MediadorVistas : IMediadorVistas
{
    private readonly Dictionary<string, IVistaColaboradora> _vistas = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<EventoVista, string[]> _rutas;
    private List<string> _ultimas = new();

    public MediadorVistas(IReadOnlyDictionary<EventoVista, string[]> rutas) => _rutas = rutas;

    public IReadOnlyList<string> UltimasVistasRefrescadas => _ultimas;

    public event EventHandler<string>? Notificado;

    public void Registrar(IVistaColaboradora vista) => _vistas[vista.NombreVista] = vista;

    public async Task NotificarAsync(IVistaColaboradora? emisor, EventoVista evento)
    {
        var refrescadas = new List<string>();
        var fallidas = new List<string>();
        if (_rutas.TryGetValue(evento, out var destinos))
        {
            foreach (var nombre in destinos)
            {
                if (!_vistas.TryGetValue(nombre, out var vista) || ReferenceEquals(vista, emisor)) continue;
                try
                {
                    await vista.RefrescarAsync();
                    refrescadas.Add(nombre);
                }
                catch (Exception ex)
                {
                    fallidas.Add($"{nombre} ({ex.Message})"); // una vista con error no bloquea a las demás
                }
            }
        }
        _ultimas = refrescadas;
        Notificado?.Invoke(this, $"{evento} → {string.Join(", ", refrescadas)}" +
                                 (fallidas.Count > 0 ? $" | sin refrescar: {string.Join(", ", fallidas)}" : ""));
    }
}
