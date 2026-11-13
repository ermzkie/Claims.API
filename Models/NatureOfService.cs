using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("NatureOfService")]
public partial class NatureOfService
{
    [Key]
    [Column("NatureID")]
    public short NatureId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? NatureDesc { get; set; }

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [InverseProperty("Nature")]
    public virtual ICollection<TransactionService> TransactionServices { get; set; } = new List<TransactionService>();
}
