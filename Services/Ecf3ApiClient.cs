using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FacturasWeb.Models;

namespace FacturasWeb.Services;

public class Ecf3ApiClient
{
    private readonly HttpClient _http;
    private readonly SettingsStore _settings;

    public List<ApiLogEntry> Log { get; } = new();

    public Ecf3ApiClient(HttpClient http, SettingsStore settings)
    {
        _http = http;
        _settings = settings;
    }

    private static readonly JsonSerializerOptions JsonOut = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly JsonSerializerOptions JsonIn = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private string BuildUrl(string ruta, IDictionary<string, string>? query = null)
    {
        var s = _settings.Get();
        var baseUrl = s.ApiUrl.TrimEnd();
        var sep = baseUrl.Contains('?') ? "&" : "?";
        var url = $"{baseUrl}{sep}ruta={Uri.EscapeDataString(ruta)}";
        if (query != null)
            foreach (var kv in query)
                url += $"&{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}";
        return url;
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string ruta, object? body = null, IDictionary<string, string>? query = null)
    {
        var req = new HttpRequestMessage(method, BuildUrl(ruta, query));
        var token = _settings.Get().Token;
        if (!string.IsNullOrWhiteSpace(token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body != null)
        {
            // El API ECF3 espera el body como form-urlencoded con un campo datax={json}
            // (la ruta application/json tiene bugs en algunos endpoints como secuencias.actualizar)
            var json = JsonSerializer.Serialize(body, JsonOut);
            var form = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("datax", json)
            });
            req.Content = form;
        }
        return req;
    }

    private async Task<JsonElement> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        var entry = new ApiLogEntry
        {
            Timestamp = DateTime.Now,
            Method = req.Method.Method,
            Url = req.RequestUri?.ToString() ?? "",
            Token = _settings.Get().Token
        };
        if (req.Content != null)
        {
            var raw = await req.Content.ReadAsStringAsync(ct);
            entry.RequestBody = raw;
            if (raw.StartsWith("datax=", StringComparison.Ordinal))
            {
                try { entry.RequestPayload = Uri.UnescapeDataString(raw.Substring(6)); }
                catch { entry.RequestPayload = raw; }
            }
        }

        var sw = Stopwatch.StartNew();
        HttpResponseMessage? resp = null;
        string? texto = null;
        try
        {
            resp = await _http.SendAsync(req, ct);
            texto = await resp.Content.ReadAsStringAsync(ct);
            entry.StatusCode = (int)resp.StatusCode;
            entry.ResponseBody = texto;
        }
        catch (Exception ex)
        {
            entry.Error = ex.Message;
            entry.DurationMs = sw.ElapsedMilliseconds;
            Log.Add(entry);
            resp?.Dispose();
            throw;
        }
        entry.DurationMs = sw.ElapsedMilliseconds;
        Log.Add(entry);

        try
        {
            if (string.IsNullOrWhiteSpace(texto))
                throw new Ecf3ApiException((int)resp.StatusCode, "Respuesta vacía del API");
            JsonElement json;
            try { json = JsonDocument.Parse(texto).RootElement.Clone(); }
            catch { throw new Ecf3ApiException((int)resp.StatusCode, $"Respuesta no-JSON: {Truncate(texto, 300)}"); }

            if (!resp.IsSuccessStatusCode)
            {
                var msg = TryGetString(json, "error") ?? TryGetString(json, "mensaje") ?? $"HTTP {(int)resp.StatusCode}";
                throw new Ecf3ApiException((int)resp.StatusCode, msg);
            }
            if (json.ValueKind == JsonValueKind.Object
                && json.TryGetProperty("ok", out var okProp)
                && okProp.ValueKind == JsonValueKind.False)
            {
                var msg = TryGetString(json, "error") ?? TryGetString(json, "mensaje") ?? "Error del API";
                throw new Ecf3ApiException((int)resp.StatusCode, msg);
            }
            return json;
        }
        finally
        {
            resp?.Dispose();
        }
    }

    public async Task<PingResultado> PingAsync(CancellationToken ct = default)
    {
        var json = await SendAsync(BuildRequest(HttpMethod.Get, "ping"), ct);
        return new PingResultado
        {
            Ok = TryGetBool(json, "ok") ?? true,
            Mensaje = TryGetString(json, "mensaje"),
            Version = TryGetString(json, "version")
        };
    }

    public async Task<RncResultado> ConsultarRncAsync(string rnc, CancellationToken ct = default)
    {
        var json = await SendAsync(BuildRequest(HttpMethod.Post, "consultas.rnc", new { rnc }), ct);
        var dato = json;
        if (json.TryGetProperty("data", out var d)) dato = d;
        else if (json.TryGetProperty("rnc_info", out var r)) dato = r;

        return new RncResultado
        {
            Rnc = TryGetString(dato, "rnc") ?? rnc,
            RazonSocial = TryGetString(dato, "razon_social") ?? TryGetString(dato, "nombre") ?? TryGetString(dato, "razonsocial"),
            Estado = TryGetString(dato, "estado"),
            Actividad = TryGetString(dato, "actividad_economica") ?? TryGetString(dato, "actividad")
        };
    }

    public async Task<EmitirResultado> EmitirAsync(EmitirRequest request, CancellationToken ct = default)
    {
        var json = await SendAsync(BuildRequest(HttpMethod.Post, "documentos.emitir", request), ct);
        return new EmitirResultado
        {
            Ok = TryGetBool(json, "ok") ?? true,
            DocumentoId = TryGetInt(json, "documento_id"),
            Ncf = TryGetString(json, "ncf"),
            TipoEcf = TryGetString(json, "tipo_ecf"),
            Total = TryGetDecimal(json, "total"),
            EstadoDgii = TryGetString(json, "estado_dgii"),
            Uid = TryGetString(json, "uid"),
            TrackId = TryGetString(json, "track_id"),
            MensajeDgii = TryGetString(json, "mensaje_dgii"),
            PdfUrl = TryGetString(json, "pdf_url"),
            QrUrl = TryGetString(json, "qr_url"),
            Duplicado = TryGetBool(json, "duplicado") ?? false,
            Ambiente = TryGetString(json, "ambiente"),
            Aviso = TryGetString(json, "aviso")
        };
    }

    public async Task<JsonElement> ValidarNcfAsync(string rnc, string ncf, CancellationToken ct = default)
    {
        return await SendAsync(BuildRequest(HttpMethod.Post, "consultas.ncf", new { rnc, ncf }), ct);
    }

    public async Task<EmitirResultado> EmitirNotaCreditoAsync(NotaCreditoRequest request, CancellationToken ct = default)
    {
        var json = await SendAsync(BuildRequest(HttpMethod.Post, "documentos.nota_credito", request), ct);
        return new EmitirResultado
        {
            Ok = TryGetBool(json, "ok") ?? true,
            DocumentoId = TryGetInt(json, "documento_id"),
            Ncf = TryGetString(json, "ncf"),
            TipoEcf = TryGetString(json, "tipo_ecf"),
            Total = TryGetDecimal(json, "total"),
            EstadoDgii = TryGetString(json, "estado_dgii"),
            Uid = TryGetString(json, "uid"),
            TrackId = TryGetString(json, "track_id"),
            MensajeDgii = TryGetString(json, "mensaje_dgii"),
            PdfUrl = TryGetString(json, "pdf_url"),
            QrUrl = TryGetString(json, "qr_url"),
            Duplicado = TryGetBool(json, "duplicado") ?? false,
            Ambiente = TryGetString(json, "ambiente"),
            Aviso = TryGetString(json, "aviso")
        };
    }

    public async Task<GastosListado> ListarGastosAsync(GastoFiltros filtros, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(filtros.RncEmisor)) query["rnc_emisor"] = filtros.RncEmisor;
        if (!string.IsNullOrWhiteSpace(filtros.TipoEcf)) query["tipo_ecf"] = filtros.TipoEcf;
        if (!string.IsNullOrWhiteSpace(filtros.Desde)) query["desde"] = filtros.Desde;
        if (!string.IsNullOrWhiteSpace(filtros.Hasta)) query["hasta"] = filtros.Hasta;
        if (filtros.Limit.HasValue) query["limit"] = filtros.Limit.Value.ToString();
        if (filtros.Offset.HasValue) query["offset"] = filtros.Offset.Value.ToString();

        var json = await SendAsync(BuildRequest(HttpMethod.Get, "gastos.listar", null, query), ct);
        var r = new GastosListado
        {
            Total = TryGetInt(json, "total") ?? 0,
            Limit = TryGetInt(json, "limit") ?? 50,
            Offset = TryGetInt(json, "offset") ?? 0
        };
        if (json.TryGetProperty("gastos", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var it in arr.EnumerateArray())
                r.Gastos.Add(ParseGasto(it));
        return r;
    }

    public async Task<Gasto?> ConsultarGastoAsync(int id, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string> { ["id"] = id.ToString() };
        var json = await SendAsync(BuildRequest(HttpMethod.Get, "gastos.consultar", null, query), ct);
        var root = json;
        if (json.TryGetProperty("gasto", out var g)) root = g;
        else if (json.TryGetProperty("data", out var d)) root = d;
        return ParseGasto(root);
    }

    public async Task<GastoRegistrarResultado> RegistrarGastoAsync(GastoRegistrarRequest req, CancellationToken ct = default)
    {
        var json = await SendAsync(BuildRequest(HttpMethod.Post, "gastos.registrar", req), ct);
        return new GastoRegistrarResultado
        {
            Ok = TryGetBool(json, "ok") ?? true,
            GastoId = TryGetInt(json, "gasto_id"),
            Duplicado = TryGetBool(json, "duplicado") ?? false,
            Mensaje = TryGetString(json, "mensaje")
        };
    }

    public async Task EliminarGastoAsync(int id, CancellationToken ct = default)
    {
        await SendAsync(BuildRequest(HttpMethod.Post, "gastos.eliminar", new { id }), ct);
    }

    private static Gasto ParseGasto(JsonElement e) => new()
    {
        Id = TryGetInt(e, "id") ?? 0,
        RncEmisor = TryGetString(e, "rnc_emisor"),
        RazonSocialEmisor = TryGetString(e, "razon_social_emisor"),
        Ncf = TryGetString(e, "ncf"),
        TipoEcf = TryGetString(e, "tipo_ecf"),
        FechaEmision = TryGetString(e, "fecha_emision"),
        Total = TryGetDecimal(e, "total") ?? 0m,
        Estado = TryGetString(e, "estado"),
        Notas = TryGetString(e, "notas"),
        CreadoEn = TryGetString(e, "creado_en")
    };

    public async Task<List<SecuenciaNcf>> ListarSecuenciasAsync(CancellationToken ct = default)
    {
        var json = await SendAsync(BuildRequest(HttpMethod.Get, "secuencias.listar"), ct);
        var result = new List<SecuenciaNcf>();
        if (json.TryGetProperty("secuencias", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var it in arr.EnumerateArray())
                result.Add(ParseSecuencia(it));
        }
        return result;
    }

    public async Task ActualizarSecuenciaAsync(SecuenciaActualizar body, CancellationToken ct = default)
    {
        await SendAsync(BuildRequest(HttpMethod.Post, "secuencias.actualizar", body), ct);
    }

    private static SecuenciaNcf ParseSecuencia(JsonElement e) => new()
    {
        TipoEcf = TryGetString(e, "tipo_ecf") ?? "",
        Nombre = TryGetString(e, "nombre"),
        Serie = TryGetString(e, "serie"),
        Desde = TryGetInt(e, "desde"),
        Hasta = TryGetInt(e, "hasta"),
        Actual = TryGetInt(e, "actual"),
        Disponibles = TryGetInt(e, "disponibles"),
        Estado = TryGetString(e, "estado"),
        SiguienteNcf = TryGetString(e, "siguiente_ncf"),
        FechaAutorizacion = TryGetString(e, "fecha_autorizacion"),
        FechaVencimiento = TryGetString(e, "fecha_vencimiento")
    };

    private static string? TryGetString(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() : null;

    private static int? TryGetInt(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var i)) return i;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out var s)) return s;
        return null;
    }

    private static decimal? TryGetDecimal(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return null;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDecimal(out var d)) return d;
        if (p.ValueKind == JsonValueKind.String && decimal.TryParse(p.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }

    private static bool? TryGetBool(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max) + "…";
}

public class Ecf3ApiException : Exception
{
    public int StatusCode { get; }
    public Ecf3ApiException(int status, string message) : base(message) { StatusCode = status; }
}

public class ApiLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = "";
    public string Url { get; set; } = "";
    public string? Token { get; set; }
    public string? RequestBody { get; set; }
    public string? RequestPayload { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public long DurationMs { get; set; }
    public string? Error { get; set; }
}

public class PingResultado
{
    public bool Ok { get; set; }
    public string? Mensaje { get; set; }
    public string? Version { get; set; }
}

public class RncResultado
{
    public string Rnc { get; set; } = "";
    public string? RazonSocial { get; set; }
    public string? Estado { get; set; }
    public string? Actividad { get; set; }
    public bool EsValido => !string.IsNullOrWhiteSpace(RazonSocial);
}

public class EmitirRequest
{
    public string Uid { get; set; } = "";
    [JsonPropertyName("tipo_ecf")] public string TipoEcf { get; set; } = "31";
    public string? Ncf { get; set; }
    [JsonPropertyName("rnc_receptor")] public string? RncReceptor { get; set; }
    [JsonPropertyName("razon_social_receptor")] public string? RazonSocialReceptor { get; set; }
    [JsonPropertyName("direccion_receptor")] public string? DireccionReceptor { get; set; }
    [JsonPropertyName("fecha_emision")] public string? FechaEmision { get; set; }
    [JsonPropertyName("tipo_pago")] public int TipoPago { get; set; } = 1;
    [JsonPropertyName("forma_pago")] public int FormaPago { get; set; } = 1;
    public List<EmitirItem> Items { get; set; } = new();
}

public class EmitirItem
{
    public string Descripcion { get; set; } = "";
    public decimal Cantidad { get; set; }
    [JsonPropertyName("precio_unitario")] public decimal PrecioUnitario { get; set; }
    [JsonPropertyName("tasa_itbis")] public int TasaItbis { get; set; } = 18;
    public decimal Descuento { get; set; }
}

public class EmitirResultado
{
    public bool Ok { get; set; }
    public int? DocumentoId { get; set; }
    public string? Ncf { get; set; }
    public string? TipoEcf { get; set; }
    public decimal? Total { get; set; }
    public string? EstadoDgii { get; set; }
    public string? Uid { get; set; }
    public string? TrackId { get; set; }
    public string? MensajeDgii { get; set; }
    public string? PdfUrl { get; set; }
    public string? QrUrl { get; set; }
    public bool Duplicado { get; set; }
    public string? Ambiente { get; set; }
    public string? Aviso { get; set; }
}

public class SecuenciaNcf
{
    public string TipoEcf { get; set; } = "";
    public string? Nombre { get; set; }
    public string? Serie { get; set; }
    public int? Desde { get; set; }
    public int? Hasta { get; set; }
    public int? Actual { get; set; }
    public int? Disponibles { get; set; }
    public string? Estado { get; set; }
    public string? SiguienteNcf { get; set; }
    public string? FechaAutorizacion { get; set; }
    public string? FechaVencimiento { get; set; }
}

public class Gasto
{
    public int Id { get; set; }
    public string? RncEmisor { get; set; }
    public string? RazonSocialEmisor { get; set; }
    public string? Ncf { get; set; }
    public string? TipoEcf { get; set; }
    public string? FechaEmision { get; set; }
    public decimal Total { get; set; }
    public string? Estado { get; set; }
    public string? Notas { get; set; }
    public string? CreadoEn { get; set; }
}

public class GastoFiltros
{
    public string? RncEmisor { get; set; }
    public string? TipoEcf { get; set; }
    public string? Desde { get; set; }
    public string? Hasta { get; set; }
    public int? Limit { get; set; }
    public int? Offset { get; set; }
}

public class GastosListado
{
    public List<Gasto> Gastos { get; set; } = new();
    public int Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}

public class GastoRegistrarRequest
{
    [JsonPropertyName("rnc_emisor")] public string RncEmisor { get; set; } = "";
    [JsonPropertyName("razon_social_emisor")] public string? RazonSocialEmisor { get; set; }
    public string? Ncf { get; set; }
    [JsonPropertyName("tipo_ecf")] public string? TipoEcf { get; set; }
    [JsonPropertyName("fecha_emision")] public string? FechaEmision { get; set; }
    public decimal Total { get; set; }
    public string? Notas { get; set; }
}

public class GastoRegistrarResultado
{
    public bool Ok { get; set; }
    public int? GastoId { get; set; }
    public bool Duplicado { get; set; }
    public string? Mensaje { get; set; }
}

public class NotaCreditoRequest
{
    public string Uid { get; set; } = "";
    [JsonPropertyName("ncf_referencia")] public string NcfReferencia { get; set; } = "";
    [JsonPropertyName("codigo_modificacion")] public string CodigoModificacion { get; set; } = "05";
    [JsonPropertyName("rnc_receptor")] public string? RncReceptor { get; set; }
    [JsonPropertyName("razon_social_receptor")] public string? RazonSocialReceptor { get; set; }
    public List<EmitirItem>? Items { get; set; }
}

public class SecuenciaActualizar
{
    [JsonPropertyName("tipo_ecf")] public string TipoEcf { get; set; } = "";
    [JsonPropertyName("desde_num")] public int DesdeNum { get; set; }
    [JsonPropertyName("hasta_num")] public int HastaNum { get; set; }
    public string Serie { get; set; } = "E";
    [JsonPropertyName("fecha_autorizacion")] public string? FechaAutorizacion { get; set; }
    [JsonPropertyName("fecha_vencimiento")] public string? FechaVencimiento { get; set; }
}
