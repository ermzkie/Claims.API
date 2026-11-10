using System.ComponentModel.DataAnnotations;

namespace Claims.API.DTOs.NatureOfService;

public class CreateNatureOfServiceDto
{
  [Required(ErrorMessage = "Nature description is required.")]
  public string NatureDesc { get; set; } = string.Empty;
}