using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("ApprovalStatus")]
public partial class ApprovalStatus
{
    [Key]
    [Column("ApprovalStatusID")]
    public short ApprovalStatusId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? ApprovalStatusCode { get; set; }

    [StringLength(150)]
    [Unicode(false)]
    public string? ApprovalStatusDesc { get; set; }

    [InverseProperty("ApprovalStatus")]
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
