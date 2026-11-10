using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("FamilyComposition")]
public partial class FamilyComposition
{
    [Key]
    [Column("FamilyCompID")]
    public int FamilyCompId { get; set; }

    [Column("PersonID")]
    public int PersonId { get; set; }

    [Column("FamilyPersonID")]
    public int? FamilyPersonId { get; set; }

    [Column("RelationshipStatusID")]
    public short? RelationshipStatusId { get; set; }

    public bool Status { get; set; }

    public int CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [ForeignKey("FamilyPersonId")]
    [InverseProperty("FamilyCompositionFamilyPeople")]
    public virtual Person? FamilyPerson { get; set; }

    [ForeignKey("PersonId")]
    [InverseProperty("FamilyCompositionPeople")]
    public virtual Person Person { get; set; } = null!;

    [ForeignKey("RelationshipStatusId")]
    [InverseProperty("FamilyCompositions")]
    public virtual RelationshipStatus? RelationshipStatus { get; set; }
}
