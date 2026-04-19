using System.ComponentModel.DataAnnotations;

namespace FacturasWeb.Models;

public class Factura
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required(ErrorMessage = "El número de factura es obligatorio")]
    [Display(Name = "Número")]
    public string Numero { get; set; } = string.Empty;

    [Required(ErrorMessage = "La fecha es obligatoria")]
    [DataType(DataType.Date)]
    [Display(Name = "Fecha")]
    public DateTime Fecha { get; set; } = DateTime.Today;

    [Required(ErrorMessage = "El cliente es obligatorio")]
    [Display(Name = "Cliente")]
    public string Cliente { get; set; } = string.Empty;

    [Display(Name = "RNC / Identificación")]
    public string? Rnc { get; set; }

    [Display(Name = "Dirección")]
    public string? Direccion { get; set; }

    [Display(Name = "Notas")]
    public string? Notas { get; set; }

    public List<LineaFactura> Lineas { get; set; } = new();

    [Display(Name = "Tipo e-CF")]
    public string TipoEcf { get; set; } = "31";

    [Display(Name = "Tipo de pago")]
    public int TipoPago { get; set; } = 1;

    [Display(Name = "Forma de pago")]
    public int FormaPago { get; set; } = 1;

    public bool RncValidado { get; set; }
    public string? RazonSocialValidada { get; set; }

    public int? DocumentoId { get; set; }
    public string? Ncf { get; set; }
    public string? NcfReferencia { get; set; }
    public string? CodigoModificacion { get; set; }
    public string? TrackId { get; set; }
    public string? EstadoDgii { get; set; }
    public string? MensajeDgii { get; set; }
    public string? PdfUrl { get; set; }
    public string? QrUrl { get; set; }
    public string? Ambiente { get; set; }
    public DateTime? FechaFirma { get; set; }

    public decimal Subtotal => Lineas?.Sum(l => l.Subtotal) ?? 0m;
    public decimal Itbis => Lineas?.Sum(l => l.Itbis) ?? 0m;
    public decimal Total => Subtotal + Itbis;

    public bool EstaFirmada => !string.IsNullOrEmpty(Ncf);
}

public class LineaFactura
{
    [Required(ErrorMessage = "La descripción es obligatoria")]
    [Display(Name = "Descripción")]
    public string Descripcion { get; set; } = string.Empty;

    [Display(Name = "Cantidad")]
    [Range(0.0001, double.MaxValue, ErrorMessage = "La cantidad debe ser mayor que 0")]
    public decimal Cantidad { get; set; } = 1m;

    [Display(Name = "Precio")]
    [Range(0, double.MaxValue, ErrorMessage = "El precio no puede ser negativo")]
    public decimal Precio { get; set; }

    [Display(Name = "ITBIS %")]
    public int TasaItbis { get; set; } = 18;

    public decimal Subtotal => Math.Round(Cantidad * Precio, 2);
    public decimal Itbis => Math.Round(Subtotal * TasaItbis / 100m, 2);
}
