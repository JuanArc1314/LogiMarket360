namespace LogiMarket.Application.Demostracion;

public static class ProveedoresPago
{
    public const string Legado = "LEGADO";
    public const string Alterno = "ALTERNO";
}

public static class CanalesNotificacion
{
    public const string Email = "EMAIL";
    public const string Sms = "SMS";
}

/// Opciones de la pantalla de demostración (singleton). No alteran reglas de negocio ni resultados:
/// solo eligen el colaborador simulado activo o le indican que falle, para observar el flujo real.
public sealed class ConfiguracionDemostracion
{
    private readonly object _candado = new();
    private string _proveedorPago = ProveedoresPago.Legado;
    private string _canal = CanalesNotificacion.Email;
    private bool _rechazarCobro;
    private bool _fallarNotificador;
    private int _desfaseDias;

    public string ProveedorPago { get { lock (_candado) return _proveedorPago; } }
    public string CanalNotificacion { get { lock (_candado) return _canal; } }
    public bool SimularRechazoCobro { get { lock (_candado) return _rechazarCobro; } }
    public bool SimularFalloNotificador { get { lock (_candado) return _fallarNotificador; } }
    public int DesfaseDias { get { lock (_candado) return _desfaseDias; } }

    public event EventHandler? Cambio;

    public void UsarProveedorPago(string proveedor)
    {
        if (proveedor is not (ProveedoresPago.Legado or ProveedoresPago.Alterno))
            throw new ArgumentException($"Proveedor de pago desconocido: {proveedor}", nameof(proveedor));
        lock (_candado) _proveedorPago = proveedor;
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    public void UsarCanalNotificacion(string canal)
    {
        if (canal is not (CanalesNotificacion.Email or CanalesNotificacion.Sms))
            throw new ArgumentException($"Canal desconocido: {canal}", nameof(canal));
        lock (_candado) _canal = canal;
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    public void ConfigurarRechazoCobro(bool activo) { lock (_candado) _rechazarCobro = activo; Cambio?.Invoke(this, EventArgs.Empty); }
    public void ConfigurarFalloNotificador(bool activo) { lock (_candado) _fallarNotificador = activo; Cambio?.Invoke(this, EventArgs.Empty); }

    public void ConfigurarDesfaseDias(int dias)
    {
        if (dias is < 0 or > 365) throw new ArgumentOutOfRangeException(nameof(dias), "Desfase permitido: 0 a 365 días.");
        lock (_candado) _desfaseDias = dias;
        Cambio?.Invoke(this, EventArgs.Empty);
    }

    public string Describir() =>
        $"Proveedor={ProveedorPago}; Canal={CanalNotificacion}; RechazoCobro={SimularRechazoCobro}; " +
        $"FalloNotificador={SimularFalloNotificador}; DesfaseDias={DesfaseDias}";
}
