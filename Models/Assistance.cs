using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Keyless]
[Table("Assistance")]
public partial class Assistance
{
    [Column("AssistanceID")]
    public int AssistanceId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? AssistanceType { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Date { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? AssistanceCode { get; set; }

    public int? Fund { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Remarks { get; set; }

    [Column("isActive")]
    public bool? IsActive { get; set; }
}
