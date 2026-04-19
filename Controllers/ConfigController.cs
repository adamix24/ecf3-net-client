using FacturasWeb.Models;
using FacturasWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacturasWeb.Controllers;

public class ConfigController : Controller
{
    private readonly SettingsStore _settings;
    private readonly Ecf3ApiClient _api;

    public ConfigController(SettingsStore settings, Ecf3ApiClient api)
    {
        _settings = settings;
        _api = api;
    }

    public IActionResult Index() => View(_settings.Get());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Index(Ecf3Settings model)
    {
        if (!ModelState.IsValid) return View(model);
        _settings.Guardar(model);
        TempData["Mensaje"] = "Configuración guardada.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Probar(CancellationToken ct)
    {
        try
        {
            var p = await _api.PingAsync(ct);
            TempData["Mensaje"] = $"Conexión OK — {p.Mensaje} (v{p.Version})";
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error: {ex.Message} (HTTP {ex.StatusCode})";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Secuencias(CancellationToken ct)
    {
        try
        {
            var lista = await _api.ListarSecuenciasAsync(ct);
            return View(lista);
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error: {ex.Message}";
            return View(new List<SecuenciaNcf>());
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SecuenciaActualizar(SecuenciaActualizar model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.Serie)) model.Serie = "E";
        if (string.IsNullOrWhiteSpace(model.FechaAutorizacion))
            model.FechaAutorizacion = DateTime.Today.ToString("yyyy-MM-dd");
        if (string.IsNullOrWhiteSpace(model.FechaVencimiento))
            model.FechaVencimiento = DateTime.Today.AddYears(2).ToString("yyyy-MM-dd");

        try
        {
            await _api.ActualizarSecuenciaAsync(model, ct);
            TempData["Mensaje"] = $"Secuencia {model.TipoEcf} actualizada (desde {model.DesdeNum} hasta {model.HastaNum}).";
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error: {ex.Message}";
        }
        return RedirectToAction(nameof(Secuencias));
    }
}
