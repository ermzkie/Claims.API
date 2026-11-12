using System.ComponentModel.DataAnnotations;

namespace Claims.API.DTOs.Creditor;

public class CreateCreditorDto
{
  [Required(ErrorMessage = "Creditor name is required.")]
  public string CreditorName { get; set; } = string.Empty;
}