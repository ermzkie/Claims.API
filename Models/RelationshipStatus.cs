using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("RelationshipStatus")]
public partial class RelationshipStatus
{
    [Key]
    [Column("RelationshipStatusID")]
    public short RelationshipStatusId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? RelationshipStatusDesc { get; set; }

    [InverseProperty("RelationshipStatus")]
    public virtual ICollection<FamilyComposition> FamilyCompositions { get; set; } = new List<FamilyComposition>();
}
