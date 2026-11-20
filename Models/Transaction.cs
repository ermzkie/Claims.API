using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Index("SocialWorkerUserId", Name = "IX_Transactions_SocialWorkerUserID")]
public partial class Transaction
{
    [Key]
    [Column("TransactionID")]
    public int TransactionId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? AssistanceCode { get; set; }

    [Column("LaneTypeID")]
    public short? LaneTypeId { get; set; }

    public short? QueueNo { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? RefNo { get; set; }

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

    [Column("SocialWorkerID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? SocialWorkerId { get; set; }

    [Column("SocialWorkerUserID")]
    public int? SocialWorkerUserId { get; set; }

    [Unicode(false)]
    public string? SocialWorkerAssessment { get; set; }

    [Unicode(false)]
    public string? Remarks { get; set; }

    [Column("ApprovalStatusID")]
    public short ApprovalStatusId { get; set; }

    public int? ApprovedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateApproved { get; set; }

    public bool IsReleased { get; set; }

    public int? ReleasedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateReleased { get; set; }

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [ForeignKey("ApprovalStatusId")]
    [InverseProperty("Transactions")]
    public virtual ApprovalStatus ApprovalStatus { get; set; } = null!;

    [ForeignKey("ApprovedBy")]
    [InverseProperty("TransactionApprovedByNavigations")]
    public virtual User? ApprovedByNavigation { get; set; }

    [ForeignKey("CreditorId")]
    [InverseProperty("Transactions")]
    public virtual Creditor? Creditor { get; set; }

    [ForeignKey("FundSourceId")]
    [InverseProperty("Transactions")]
    public virtual FundSource? FundSource { get; set; }

    [ForeignKey("LaneTypeId")]
    [InverseProperty("Transactions")]
    public virtual LaneType? LaneType { get; set; }

    [ForeignKey("ModeId")]
    [InverseProperty("Transactions")]
    public virtual ModeOfAssistance? Mode { get; set; }

    [ForeignKey("PatientPersonId")]
    [InverseProperty("TransactionPatientPeople")]
    public virtual Person? PatientPerson { get; set; }

    [ForeignKey("ReleasedBy")]
    [InverseProperty("TransactionReleasedByNavigations")]
    public virtual User? ReleasedByNavigation { get; set; }

    [ForeignKey("RepresentativePersonId")]
    [InverseProperty("TransactionRepresentativePeople")]
    public virtual Person? RepresentativePerson { get; set; }

    [ForeignKey("SocialWorkerUserId")]
    [InverseProperty("TransactionSocialWorkerUsers")]
    public virtual User? SocialWorkerUser { get; set; }

    [InverseProperty("Transaction")]
    public virtual ICollection<TransactionService> TransactionServices { get; set; } = new List<TransactionService>();
}
