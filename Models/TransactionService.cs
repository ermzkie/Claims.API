using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

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

    [ForeignKey("TransactionId")]
    [InverseProperty("TransactionServices")]
    public virtual Transaction Transaction { get; set; } = null!;
}
