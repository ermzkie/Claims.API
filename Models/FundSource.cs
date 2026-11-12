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

    public short? Year { get; set; }

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [ForeignKey("SourceId")]
    [InverseProperty("FundSources")]
    public virtual SourceOfAssistance? Source { get; set; }

    [InverseProperty("FundSource")]
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
