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

    [InverseProperty("Nature")]
    public virtual ICollection<TransactionService> TransactionServices { get; set; } = new List<TransactionService>();
}
