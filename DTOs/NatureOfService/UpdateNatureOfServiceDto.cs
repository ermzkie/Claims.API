using System.ComponentModel.DataAnnotations;

namespace Claims.API.DTOs.NatureOfService;

public class UpdateNatureOfServiceDto
{
  [Required(ErrorMessage = "Nature description is required.")]
  public string NatureDesc { get; set; } = string.Empty;
  public bool Status { get; set; }
}