using System.Text.Json;
using FacturasWeb.Models;

namespace FacturasWeb.Services;

public class SettingsStore
{
    private readonly string _archivo;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly object _lock = new();
    private Ecf3Settings? _cache;

    public SettingsStore(IConfiguration config, IWebHostEnvironment env)
    {
        var carpeta = config["Facturas:Carpeta"] ?? "/datax/facturas";
        var baseDir = Path.IsPathRooted(carpeta)
            ? Path.GetDirectoryName(carpeta.TrimEnd('/', '\\')) ?? carpeta
            : Path.Combine(env.ContentRootPath, Path.GetDirectoryName(carpeta.TrimStart('/', '\\').TrimEnd('/', '\\')) ?? "datax");

        Directory.CreateDirectory(baseDir);
        _archivo = Path.Combine(baseDir, "config.json");
    }

    public Ecf3Settings Get()
    {
        lock (_lock)
        {
            if (_cache != null) return Clone(_cache);
            if (File.Exists(_archivo))
            {
                try
                {
                    var json = File.ReadAllText(_archivo);
                    _cache = JsonSerializer.Deserialize<Ecf3Settings>(json, JsonOptions) ?? Defaults();
                }
                catch
                {
                    _cache = Defaults();
                }
            }
            else
            {
                _cache = Defaults();
                Guardar(_cache);
            }
            return Clone(_cache);
        }
    }

    public void Guardar(Ecf3Settings s)
    {
        lock (_lock)
        {
            var json = JsonSerializer.Serialize(s, JsonOptions);
            File.WriteAllText(_archivo, json);
            _cache = Clone(s);
        }
    }

    private static Ecf3Settings Defaults() => new()
    {
        ApiUrl = "https://test-demoxdemo24.ecf3.com/apix/api.php",
        Token = "b1e40a893dcf37130b00c27c6b66587435ab520dd1371c48277e1d90add2d70c",
        TipoEcfPorDefecto = "31"
    };

    private static Ecf3Settings Clone(Ecf3Settings s) => new()
    {
        ApiUrl = s.ApiUrl,
        Token = s.Token,
        TipoEcfPorDefecto = s.TipoEcfPorDefecto,
        TipoPagoPorDefecto = s.TipoPagoPorDefecto,
        FormaPagoPorDefecto = s.FormaPagoPorDefecto
    };
}
