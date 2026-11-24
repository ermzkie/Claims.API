using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("Medicine")]
public partial class Medicine
{
    [Key]
    [Column("MedicineID")]
    public int MedicineId { get; set; }

    [StringLength(150)]
    [Unicode(false)]
    public string MedicineDesc { get; set; } = null!;

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [InverseProperty("Medicine")]
    public virtual ICollection<TransactionMedicine> TransactionMedicines { get; set; } = new List<TransactionMedicine>();
}
