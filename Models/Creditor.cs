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

    [InverseProperty("Creditor")]
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
