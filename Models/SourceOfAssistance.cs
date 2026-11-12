using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("SourceOfAssistance")]
public partial class SourceOfAssistance
{
    [Key]
    [Column("SourceID")]
    public short SourceId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? SourceDesc { get; set; }

    [InverseProperty("Source")]
    public virtual ICollection<FundSource> FundSources { get; set; } = new List<FundSource>();
}
