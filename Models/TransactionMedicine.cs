using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

public partial class TransactionMedicine
{
    [Key]
    [Column("TranMedicineID")]
    public int TranMedicineId { get; set; }

    [Column("ServiceFundAllocationID")]
    public int ServiceFundAllocationId { get; set; }

    [Column("MedicineID")]
    public int MedicineId { get; set; }

    public int Quantity { get; set; }

    [Column("Unit_Price", TypeName = "decimal(18, 2)")]
    public decimal UnitPrice { get; set; }

    public bool? Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [ForeignKey("MedicineId")]
    [InverseProperty("TransactionMedicines")]
    public virtual Medicine Medicine { get; set; } = null!;

    [ForeignKey("ServiceFundAllocationId")]
    [InverseProperty("TransactionMedicines")]
    public virtual ServiceFundAllocation ServiceFundAllocation { get; set; } = null!;
}
