using System.ComponentModel.DataAnnotations;

namespace FacturasWeb.Models;

public class Ecf3Settings
{
    [Required(ErrorMessage = "La URL del API es obligatoria")]
    [Display(Name = "URL del API")]
    [Url(ErrorMessage = "URL inválida")]
    public string ApiUrl { get; set; } = "https://test-demoxdemo24.ecf3.com/apix/api.php";

    [Required(ErrorMessage = "El token es obligatorio")]
    [Display(Name = "Token (API key)")]
    public string Token { get; set; } = "";

    [Display(Name = "Tipo e-CF por defecto")]
    public string TipoEcfPorDefecto { get; set; } = "31";

    [Display(Name = "Tipo de pago por defecto (1=Contado,2=Crédito,3=Gratuito)")]
    public int TipoPagoPorDefecto { get; set; } = 1;

    [Display(Name = "Forma de pago por defecto (1=Efectivo,2=Transf,3=Débito,4=Crédito)")]
    public int FormaPagoPorDefecto { get; set; } = 1;
}
