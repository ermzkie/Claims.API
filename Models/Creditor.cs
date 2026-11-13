using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("Creditor")]
public partial class Creditor
{
    [Key]
    [Column("CreditorID")]
    public int CreditorId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? CreditorName { get; set; }

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [InverseProperty("Creditor")]
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
