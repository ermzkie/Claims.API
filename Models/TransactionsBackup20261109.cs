using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Keyless]
[Table("Transactions_Backup_20261109")]
public partial class TransactionsBackup20261109
{
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

    [StringLength(50)]
    [Unicode(false)]
    public string? Date { get; set; }

    [Column("PersonID")]
    public int? PersonId { get; set; }

    [Column("RepresentativePersonID")]
    public int? RepresentativePersonId { get; set; }

    [Unicode(false)]
    public string? Representative { get; set; }

    [Column("CreditorID")]
    [StringLength(50)]
    [Unicode(false)]
    public string? CreditorId { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? Ammount { get; set; }

    [Unicode(false)]
    public string? Remarks { get; set; }

    [Column("isActive")]
    public bool? IsActive { get; set; }
}
