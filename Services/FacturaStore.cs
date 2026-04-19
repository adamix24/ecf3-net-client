using System.Text.Json;
using FacturasWeb.Models;

namespace FacturasWeb.Services;

public class FacturaStore
{
    private readonly string _carpeta;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public FacturaStore(IConfiguration config, IWebHostEnvironment env)
    {
        var configurada = config["Facturas:Carpeta"] ?? "/datax/facturas";
        _carpeta = Path.IsPathRooted(configurada)
            ? configurada
            : Path.Combine(env.ContentRootPath, configurada.TrimStart('/', '\\'));

        Directory.CreateDirectory(_carpeta);
    }

    public string Carpeta => _carpeta;

    public IEnumerable<Factura> ListarTodas()
    {
        foreach (var archivo in Directory.EnumerateFiles(_carpeta, "*.json"))
        {
            Factura? f = null;
            try
            {
                var json = File.ReadAllText(archivo);
                f = JsonSerializer.Deserialize<Factura>(json, JsonOptions);
            }
            catch
            {
                // Ignoramos archivos corruptos en el listado
            }
            if (f != null) yield return f;
        }
    }

    public Factura? Obtener(string id)
    {
        var ruta = RutaArchivo(id);
        if (!File.Exists(ruta)) return null;
        var json = File.ReadAllText(ruta);
        return JsonSerializer.Deserialize<Factura>(json, JsonOptions);
    }

    public void Guardar(Factura factura)
    {
        if (string.IsNullOrWhiteSpace(factura.Id))
            factura.Id = Guid.NewGuid().ToString("N");

        var json = JsonSerializer.Serialize(factura, JsonOptions);
        File.WriteAllText(RutaArchivo(factura.Id), json);
    }

    public bool Eliminar(string id)
    {
        var ruta = RutaArchivo(id);
        if (!File.Exists(ruta)) return false;
        File.Delete(ruta);
        return true;
    }

    private string RutaArchivo(string id)
    {
        var limpio = string.Concat(id.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        if (string.IsNullOrEmpty(limpio)) throw new ArgumentException("Id de factura inválido", nameof(id));
        return Path.Combine(_carpeta, limpio + ".json");
    }
}
