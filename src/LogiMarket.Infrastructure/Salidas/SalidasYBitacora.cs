using System.Text;
using LogiMarket.Application.Comprobantes;
using LogiMarket.Application.Comun;
using LogiMarket.Application.Seguridad;

namespace LogiMarket.Infrastructure.Salidas;

//patron bridge - implementador concreto: escribe el comprobante en un archivo TXT real
public sealed class SalidaArchivoTxt : ISalidaDocumento
{
    private readonly OpcionesInfraestructura _opciones;

    public SalidaArchivoTxt(OpcionesInfraestructura opciones) => _opciones = opciones;

    public string Medio => MediosSalida.ArchivoTxt;

    public async Task<string> TransmitirAsync(DocumentoRenderizado documento, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_opciones.DirectorioComprobantes);
        var ruta = Path.Combine(_opciones.DirectorioComprobantes, $"{documento.NombreArchivo}_{DateTime.Now:yyyyMMdd_HHmmssfff}.txt");
        await File.WriteAllTextAsync(ruta, documento.Contenido, Encoding.UTF8, ct);
        return ruta;
    }
}

//bitacora operativa en memoria + archivo de texto (independiente de las transacciones SQLite
//por lo que conserva los LOG de cobros fallidos y denegados)
public sealed class BitacoraOperativaArchivo : IBitacoraOperativa
{
    private const int MaximoEnMemoria = 5000;
    private readonly List<EntradaBitacora> _entradas = new();
    private readonly object _candado = new();
    private readonly OpcionesInfraestructura _opciones;
    private readonly ISesionUsuario _sesion;
    private readonly IReloj _reloj;

    public BitacoraOperativaArchivo(OpcionesInfraestructura opciones, ISesionUsuario sesion, IReloj reloj)
    {
        _opciones = opciones;
        _sesion = sesion;
        _reloj = reloj;
    }

    public void Registrar(string categoria, string etapa, string mensaje, string? correlacion = null)
    {
        var entrada = new EntradaBitacora(_reloj.Ahora, categoria, etapa, mensaje, correlacion, _sesion.NombreUsuario);
        lock (_candado)
        {
            _entradas.Add(entrada);
            if (_entradas.Count > MaximoEnMemoria) _entradas.RemoveRange(0, _entradas.Count - MaximoEnMemoria);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_opciones.RutaBitacora)!);
                File.AppendAllText(_opciones.RutaBitacora, entrada + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException)
            {
                //la bitacora en archivo es complementaria: un fallo de e/s no interrumpe la operacion
            }
        }
    }

    public IReadOnlyList<EntradaBitacora> Consultar(string? categoria = null, string? correlacion = null)
    {
        lock (_candado)
        {
            return _entradas
                .Where(e => categoria is null || e.Categoria == categoria)
                .Where(e => correlacion is null || e.Correlacion == correlacion)
                .ToList();
        }
    }
}
