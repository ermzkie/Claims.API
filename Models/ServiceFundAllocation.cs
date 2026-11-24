using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Index("CreditorId", Name = "IX_ServiceFundAllocations_CreditorID")]
[Index("TranServiceId", Name = "IX_ServiceFundAllocations_TranServiceID")]
public partial class ServiceFundAllocation
{
    [Key]
    [Column("ServiceFundAllocationID")]
    public int ServiceFundAllocationId { get; set; }

    [Column("TranServiceID")]
    public int TranServiceId { get; set; }

    [Column("FundSourceID")]
    public int? FundSourceId { get; set; }

    [Column("CreditorID")]
    public int? CreditorId { get; set; }

    [Column("ModeID")]
    public short? ModeId { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? Amount { get; set; }

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [ForeignKey("CreditorId")]
    [InverseProperty("ServiceFundAllocations")]
    public virtual Creditor? Creditor { get; set; }

    [ForeignKey("FundSourceId")]
    [InverseProperty("ServiceFundAllocations")]
    public virtual FundSource? FundSource { get; set; }

    [ForeignKey("ModeId")]
    [InverseProperty("ServiceFundAllocations")]
    public virtual ModeOfAssistance? Mode { get; set; }

    [ForeignKey("TranServiceId")]
    [InverseProperty("ServiceFundAllocations")]
    public virtual TransactionService TranService { get; set; } = null!;

    [InverseProperty("ServiceFundAllocation")]
    public virtual ICollection<TransactionMedicine> TransactionMedicines { get; set; } = new List<TransactionMedicine>();
}
