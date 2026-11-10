using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

public partial class Transaction
{
    [Key]
    [Column("TransactionID")]
    public int TransactionId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? AssistanceCode { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? RefNo { get; set; }

    [Column("SocialWorkerID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? SocialWorkerId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Date { get; set; }

    [Column("PatientPersonID")]
    public int? PatientPersonId { get; set; }

    [Column("RepresentativePersonID")]
    public int? RepresentativePersonId { get; set; }

    [Unicode(false)]
    public string? Representative { get; set; }

    [Column("FundSourceID")]
    public int? FundSourceId { get; set; }

    [Column("CreditorID")]
    public int? CreditorId { get; set; }

    [Column("ModeID")]
    public short? ModeId { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? Amount { get; set; }

    [Unicode(false)]
    public string? SocialWorkerAssessment { get; set; }

    [Unicode(false)]
    public string? Remarks { get; set; }

    public bool? IsReleased { get; set; }

    public bool? Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [ForeignKey("CreditorId")]
    [InverseProperty("Transactions")]
    public virtual Creditor? Creditor { get; set; }

    [ForeignKey("FundSourceId")]
    [InverseProperty("Transactions")]
    public virtual FundSource? FundSource { get; set; }

    [ForeignKey("ModeId")]
    [InverseProperty("Transactions")]
    public virtual ModeOfAssistance? Mode { get; set; }

    [ForeignKey("PatientPersonId")]
    [InverseProperty("TransactionPatientPeople")]
    public virtual Person? PatientPerson { get; set; }

    [ForeignKey("RepresentativePersonId")]
    [InverseProperty("TransactionRepresentativePeople")]
    public virtual Person? RepresentativePerson { get; set; }

    [InverseProperty("Transaction")]
    public virtual ICollection<TransactionService> TransactionServices { get; set; } = new List<TransactionService>();
}
