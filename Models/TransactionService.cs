using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Index("TransactionId", Name = "IX_TransactionServices_TransactionID")]
public partial class TransactionService
{
    [Key]
    [Column("TranServiceID")]
    public int TranServiceId { get; set; }

    [Column("TransactionID")]
    public int TransactionId { get; set; }

    [Column("NatureID")]
    public short? NatureId { get; set; }

    [ForeignKey("NatureId")]
    [InverseProperty("TransactionServices")]
    public virtual NatureOfService? Nature { get; set; }

    [InverseProperty("TranService")]
    public virtual ICollection<ServiceFundAllocation> ServiceFundAllocations { get; set; } = new List<ServiceFundAllocation>();

    [ForeignKey("TransactionId")]
    [InverseProperty("TransactionServices")]
    public virtual Transaction Transaction { get; set; } = null!;
}
