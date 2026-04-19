using System.Text.Json;
using FacturasWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacturasWeb.Controllers;

public class GastosController : Controller
{
    private readonly Ecf3ApiClient _api;

    public GastosController(Ecf3ApiClient api)
    {
        _api = api;
    }

    public async Task<IActionResult> Index(
        string? rncEmisor, string? tipoEcf, string? desde, string? hasta,
        int? limit, int? offset, CancellationToken ct = default)
    {
        var filtros = new GastoFiltros
        {
            RncEmisor = rncEmisor,
            TipoEcf = tipoEcf,
            Desde = desde,
            Hasta = hasta,
            Limit = limit ?? 50,
            Offset = offset ?? 0
        };
        ViewBag.Filtros = filtros;
        try
        {
            var lista = await _api.ListarGastosAsync(filtros, ct);
            return View(lista);
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error consultando gastos: {ex.Message}";
            return View(new GastosListado());
        }
    }

    public IActionResult Create()
    {
        return View(new GastoRegistrarRequest
        {
            FechaEmision = DateTime.Today.ToString("yyyy-MM-dd"),
            TipoEcf = "32"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Registrar(GastoRegistrarRequest model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.RncEmisor))
            ModelState.AddModelError(nameof(model.RncEmisor), "RNC del proveedor es obligatorio");
        if (model.Total <= 0)
            ModelState.AddModelError(nameof(model.Total), "El total debe ser mayor que cero");

        if (!ModelState.IsValid) return View("Create", model);

        if (!string.IsNullOrEmpty(model.FechaEmision) && model.FechaEmision.Contains('-') && model.FechaEmision.Length == 10 && model.FechaEmision[4] == '-')
        {
            // ya está en yyyy-mm-dd; lo dejamos así porque el API acepta ambos
        }

        try
        {
            var r = await _api.RegistrarGastoAsync(model, ct);
            TempData["Mensaje"] = r.Duplicado
                ? $"Gasto ya existente (id {r.GastoId}): {r.Mensaje}"
                : $"Gasto registrado (id {r.GastoId}).";
            TempData["FirmarLog"] = JsonSerializer.Serialize(_api.Log);
            if (r.GastoId.HasValue)
                return RedirectToAction(nameof(Details), new { id = r.GastoId });
            return RedirectToAction(nameof(Index));
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error al registrar: {ex.Message}";
            TempData["FirmarLog"] = JsonSerializer.Serialize(_api.Log);
            return View("Create", model);
        }
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct = default)
    {
        try
        {
            var gasto = await _api.ConsultarGastoAsync(id, ct);
            if (gasto == null) return NotFound();
            return View(gasto);
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error: {ex.Message}";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        try
        {
            await _api.EliminarGastoAsync(id, ct);
            TempData["Mensaje"] = $"Gasto {id} eliminado.";
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error al eliminar: {ex.Message}";
        }
        return RedirectToAction(nameof(Index));
    }
}
