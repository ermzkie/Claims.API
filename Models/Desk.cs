using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("Desk")]
public partial class Desk
{
    [Key]
    [Column("DeskID")]
    public int DeskId { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string DeskName { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }
}
