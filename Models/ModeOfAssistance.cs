using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("ModeOfAssistance")]
public partial class ModeOfAssistance
{
    [Key]
    [Column("ModeID")]
    public short ModeId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? ModeDesc { get; set; }

    [InverseProperty("Mode")]
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
