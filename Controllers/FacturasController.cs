using System.Text.Json;
using FacturasWeb.Models;
using FacturasWeb.Services;
using Microsoft.AspNetCore.Mvc;

namespace FacturasWeb.Controllers;

public class FacturasController : Controller
{
    private readonly FacturaStore _store;
    private readonly SettingsStore _settings;
    private readonly Ecf3ApiClient _api;

    public FacturasController(FacturaStore store, SettingsStore settings, Ecf3ApiClient api)
    {
        _store = store;
        _settings = settings;
        _api = api;
    }

    public IActionResult Index()
    {
        var lista = _store.ListarTodas()
            .OrderByDescending(f => f.Fecha)
            .ThenBy(f => f.Numero)
            .ToList();
        ViewBag.Carpeta = _store.Carpeta;
        return View(lista);
    }

    public IActionResult Create()
    {
        var s = _settings.Get();
        var nueva = new Factura
        {
            Numero = SugerirNumero(),
            Lineas = new List<LineaFactura> { new() },
            TipoEcf = s.TipoEcfPorDefecto,
            TipoPago = s.TipoPagoPorDefecto,
            FormaPago = s.FormaPagoPorDefecto
        };
        ViewBag.EsNueva = true;
        return View("Edit", nueva);
    }

    public IActionResult Edit(string id)
    {
        var factura = _store.Obtener(id);
        if (factura == null) return NotFound();
        if (factura.EstaFirmada) return RedirectToAction(nameof(Details), new { id });
        if (factura.Lineas.Count == 0) factura.Lineas.Add(new LineaFactura());
        ViewBag.EsNueva = false;
        return View(factura);
    }

    public IActionResult Details(string id)
    {
        var factura = _store.Obtener(id);
        if (factura == null) return NotFound();
        return View(factura);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Save(Factura factura)
    {
        var existente = _store.Obtener(factura.Id);
        if (existente != null && existente.EstaFirmada)
        {
            TempData["Error"] = "No se puede modificar una factura ya firmada.";
            return RedirectToAction(nameof(Details), new { id = factura.Id });
        }
        if (existente != null)
        {
            factura.RncValidado = existente.RncValidado && existente.Rnc == factura.Rnc;
            factura.RazonSocialValidada = factura.RncValidado ? existente.RazonSocialValidada : null;
        }

        factura.Lineas = factura.Lineas?
            .Where(l => !string.IsNullOrWhiteSpace(l.Descripcion) || l.Precio > 0 || l.Cantidad > 0)
            .ToList() ?? new List<LineaFactura>();

        if (factura.Lineas.Count == 0)
            ModelState.AddModelError("Lineas", "Agregue al menos una línea");

        if (!ModelState.IsValid)
        {
            if (factura.Lineas.Count == 0) factura.Lineas.Add(new LineaFactura());
            ViewBag.EsNueva = existente == null;
            return View("Edit", factura);
        }

        _store.Guardar(factura);
        TempData["Mensaje"] = $"Factura {factura.Numero} guardada.";
        return RedirectToAction(nameof(Details), new { id = factura.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(string id)
    {
        var factura = _store.Obtener(id);
        if (factura == null) return RedirectToAction(nameof(Index));
        if (factura.EstaFirmada)
        {
            TempData["Error"] = "No se puede eliminar una factura firmada.";
            return RedirectToAction(nameof(Index));
        }
        _store.Eliminar(id);
        TempData["Mensaje"] = "Factura eliminada.";
        return RedirectToAction(nameof(Index));
    }

    public IActionResult Print(string id)
    {
        var factura = _store.Obtener(id);
        if (factura == null) return NotFound();
        return View(factura);
    }

    [HttpPost]
    public async Task<IActionResult> ValidarRnc(string rnc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rnc))
            return Json(new { ok = false, mensaje = "RNC vacío" });
        try
        {
            var r = await _api.ConsultarRncAsync(rnc.Trim(), ct);
            if (!r.EsValido)
                return Json(new { ok = false, mensaje = "RNC no encontrado en padrón DGII" });
            return Json(new { ok = true, rnc = r.Rnc, razonSocial = r.RazonSocial, estado = r.Estado });
        }
        catch (Ecf3ApiException ex)
        {
            return Json(new { ok = false, mensaje = ex.Message, statusCode = ex.StatusCode });
        }
        catch (Exception ex)
        {
            return Json(new { ok = false, mensaje = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Firmar(string id, CancellationToken ct)
    {
        var factura = _store.Obtener(id);
        if (factura == null) return NotFound();

        if (factura.EstaFirmada)
        {
            TempData["Error"] = "La factura ya está firmada.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var tiposQueExigenRnc = new[] { "31", "33", "34", "41", "44", "45" };
        if (tiposQueExigenRnc.Contains(factura.TipoEcf))
        {
            if (string.IsNullOrWhiteSpace(factura.Rnc))
            {
                TempData["Error"] = $"El tipo e-CF {factura.TipoEcf} requiere RNC del receptor.";
                return RedirectToAction(nameof(Details), new { id });
            }
            try
            {
                var r = await _api.ConsultarRncAsync(factura.Rnc, ct);
                if (!r.EsValido)
                {
                    TempData["Error"] = $"RNC {factura.Rnc} no válido en DGII.";
                    return RedirectToAction(nameof(Details), new { id });
                }
                factura.RncValidado = true;
                factura.RazonSocialValidada = r.RazonSocial;
                if (string.IsNullOrWhiteSpace(factura.Cliente) && !string.IsNullOrWhiteSpace(r.RazonSocial))
                    factura.Cliente = r.RazonSocial!;
            }
            catch (Ecf3ApiException ex)
            {
                TempData["Error"] = $"Error validando RNC: {ex.Message}";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        var req = new EmitirRequest
        {
            Uid = factura.Id,
            TipoEcf = factura.TipoEcf,
            RncReceptor = factura.Rnc,
            RazonSocialReceptor = factura.Cliente,
            DireccionReceptor = factura.Direccion,
            FechaEmision = factura.Fecha.ToString("dd-MM-yyyy"),
            TipoPago = factura.TipoPago,
            FormaPago = factura.FormaPago,
            Items = factura.Lineas.Select(l => new EmitirItem
            {
                Descripcion = l.Descripcion,
                Cantidad = l.Cantidad,
                PrecioUnitario = l.Precio,
                TasaItbis = l.TasaItbis
            }).ToList()
        };

        try
        {
            var r = await _api.EmitirAsync(req, ct);
            factura.DocumentoId = r.DocumentoId;
            factura.Ncf = r.Ncf;
            factura.TrackId = r.TrackId;
            factura.EstadoDgii = r.EstadoDgii;
            factura.MensajeDgii = r.MensajeDgii;
            factura.PdfUrl = r.PdfUrl;
            factura.QrUrl = r.QrUrl;
            factura.Ambiente = r.Ambiente;
            factura.FechaFirma = DateTime.Now;
            _store.Guardar(factura);
            TempData["Mensaje"] = r.Duplicado
                ? $"Factura ya había sido firmada (NCF {r.Ncf})."
                : $"Factura firmada. NCF: {r.Ncf} · Estado: {r.EstadoDgii}";
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error al firmar: {ex.Message}";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error al firmar: {ex.Message}";
        }

        TempData["FirmarLog"] = JsonSerializer.Serialize(_api.Log);
        return RedirectToAction(nameof(Details), new { id });
    }

    public IActionResult NotaCredito(string id)
    {
        var original = _store.Obtener(id);
        if (original == null) return NotFound();
        if (!original.EstaFirmada)
        {
            TempData["Error"] = "Solo se puede emitir nota de crédito de facturas firmadas.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (original.TipoEcf == "34")
        {
            TempData["Error"] = "No se puede emitir nota de crédito de otra nota de crédito.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var nota = new Factura
        {
            Numero = SugerirNumero(),
            Fecha = DateTime.Today,
            Cliente = original.Cliente,
            Rnc = original.Rnc,
            Direccion = original.Direccion,
            Notas = $"Nota de crédito al NCF {original.Ncf}",
            TipoEcf = "34",
            TipoPago = original.TipoPago,
            FormaPago = original.FormaPago,
            NcfReferencia = original.Ncf,
            CodigoModificacion = "05",
            RncValidado = original.RncValidado,
            RazonSocialValidada = original.RazonSocialValidada,
            Lineas = original.Lineas.Select(l => new LineaFactura
            {
                Descripcion = l.Descripcion,
                Cantidad = l.Cantidad,
                Precio = l.Precio,
                TasaItbis = l.TasaItbis
            }).ToList()
        };
        ViewBag.OriginalId = original.Id;
        ViewBag.OriginalNcf = original.Ncf;
        ViewBag.OriginalNumero = original.Numero;
        return View(nota);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NotaCreditoEmitir(Factura factura, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(factura.NcfReferencia))
        {
            TempData["Error"] = "Falta NCF de referencia.";
            return RedirectToAction(nameof(Index));
        }
        var codigosValidos = new[] { "01", "02", "03", "04", "05" };
        if (!codigosValidos.Contains(factura.CodigoModificacion))
        {
            TempData["Error"] = "Código de modificación inválido.";
            return RedirectToAction(nameof(Index));
        }

        factura.TipoEcf = "34";
        factura.Id = string.IsNullOrEmpty(factura.Id) ? Guid.NewGuid().ToString("N") : factura.Id;
        factura.Lineas = factura.Lineas?
            .Where(l => !string.IsNullOrWhiteSpace(l.Descripcion) || l.Precio > 0 || l.Cantidad > 0)
            .ToList() ?? new List<LineaFactura>();

        var req = new NotaCreditoRequest
        {
            Uid = factura.Id,
            NcfReferencia = factura.NcfReferencia,
            CodigoModificacion = factura.CodigoModificacion!,
            RncReceptor = factura.Rnc,
            RazonSocialReceptor = factura.Cliente,
            Items = factura.CodigoModificacion == "05" || factura.Lineas.Count == 0
                ? null
                : factura.Lineas.Select(l => new EmitirItem
                {
                    Descripcion = l.Descripcion,
                    Cantidad = l.Cantidad,
                    PrecioUnitario = l.Precio,
                    TasaItbis = l.TasaItbis
                }).ToList()
        };

        try
        {
            var r = await _api.EmitirNotaCreditoAsync(req, ct);
            factura.DocumentoId = r.DocumentoId;
            factura.Ncf = r.Ncf;
            factura.TrackId = r.TrackId;
            factura.EstadoDgii = r.EstadoDgii;
            factura.MensajeDgii = r.MensajeDgii;
            factura.PdfUrl = r.PdfUrl;
            factura.QrUrl = r.QrUrl;
            factura.Ambiente = r.Ambiente;
            factura.FechaFirma = DateTime.Now;
            _store.Guardar(factura);
            TempData["Mensaje"] = r.Duplicado
                ? $"Nota de crédito ya emitida (NCF {r.Ncf})."
                : $"Nota de crédito emitida. NCF: {r.Ncf} · Estado: {r.EstadoDgii}";
            TempData["FirmarLog"] = JsonSerializer.Serialize(_api.Log);
            return RedirectToAction(nameof(Details), new { id = factura.Id });
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error al emitir nota de crédito: {ex.Message}";
            TempData["FirmarLog"] = JsonSerializer.Serialize(_api.Log);
            ViewBag.OriginalNcf = factura.NcfReferencia;
            return View("NotaCredito", factura);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Validar(string id, CancellationToken ct)
    {
        var factura = _store.Obtener(id);
        if (factura == null) return NotFound();
        if (!factura.EstaFirmada)
        {
            TempData["Error"] = "Primero debe firmar la factura.";
            return RedirectToAction(nameof(Details), new { id });
        }
        try
        {
            var rncEmisor = factura.Rnc ?? "";
            var json = await _api.ValidarNcfAsync(rncEmisor, factura.Ncf!, ct);
            TempData["Mensaje"] = "Validación ejecutada: " + JsonSerializer.Serialize(json);
        }
        catch (Ecf3ApiException ex)
        {
            TempData["Error"] = $"Error validando NCF: {ex.Message}";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View("Error");

    private string SugerirNumero()
    {
        var existentes = _store.ListarTodas()
            .Select(f => f.Numero)
            .Where(n => int.TryParse(n, out _))
            .Select(int.Parse)
            .DefaultIfEmpty(0)
            .Max();
        return (existentes + 1).ToString("D6");
    }
}
