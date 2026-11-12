using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("FundSource")]
public partial class FundSource
{
    [Key]
    [Column("FundSourceID")]
    public int FundSourceId { get; set; }

    [Column("AppropriationID")]
    public int? AppropriationId { get; set; }

    [Column("TrustFundID")]
    public int? TrustFundId { get; set; }

    [Column("SourceID")]
    public short? SourceId { get; set; }

    [StringLength(10)]
    public string? DateEn { get; set; }

    [StringLength(10)]
    public string? Stat { get; set; }

    public short? Year { get; set; }

    [ForeignKey("SourceId")]
    [InverseProperty("FundSources")]
    public virtual SourceOfAssistance? Source { get; set; }

    [InverseProperty("FundSource")]
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
