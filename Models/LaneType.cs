using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("LaneType")]
public partial class LaneType
{
    [Key]
    [Column("LaneTypeID")]
    public short LaneTypeId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string? LaneTypeCode { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LaneTypeDesc { get; set; }

    [InverseProperty("LaneType")]
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
