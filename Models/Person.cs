using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("Person")]
public partial class Person
{
    [Key]
    [Column("PersonID")]
    public int PersonId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? FirstName { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? MiddleName { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? LastName { get; set; }

    [Column("NameSuffixID")]
    public short? NameSuffixId { get; set; }

    [Column("SexID")]
    public short? SexId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Gender { get; set; }

    [Column("CivilStatusID")]
    public short? CivilStatusId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? NoBuildingName { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Street { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? PurokSubdivision { get; set; }

    [Column("BarangayID")]
    public int? BarangayId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Barangay { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Municipality { get; set; }

    public bool? IsSeniorCitizen { get; set; }

    public bool? IsSoloParent { get; set; }

    [Column("IsPWD")]
    public bool? IsPwd { get; set; }

    [Column("IsRR")]
    public bool? IsRr { get; set; }

    public bool? Is4P { get; set; }

    public DateOnly? Birthdate { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? ContactNo { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? SkillOccupation { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? EstimatedMonthlyIncome { get; set; }

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [ForeignKey("BarangayId")]
    [InverseProperty("People")]
    public virtual Barangay? BarangayNavigation { get; set; }

    [ForeignKey("CivilStatusId")]
    [InverseProperty("People")]
    public virtual CivilStatus? CivilStatus { get; set; }

    [ForeignKey("CreatedBy")]
    [InverseProperty("People")]
    public virtual User? CreatedByNavigation { get; set; }

    [InverseProperty("FamilyPerson")]
    public virtual ICollection<FamilyComposition> FamilyCompositionFamilyPeople { get; set; } = new List<FamilyComposition>();

    [InverseProperty("Person")]
    public virtual ICollection<FamilyComposition> FamilyCompositionPeople { get; set; } = new List<FamilyComposition>();

    [ForeignKey("NameSuffixId")]
    [InverseProperty("People")]
    public virtual NameSuffix? NameSuffix { get; set; }

    [ForeignKey("SexId")]
    [InverseProperty("People")]
    public virtual Sex? Sex { get; set; }

    [InverseProperty("PatientPerson")]
    public virtual ICollection<Transaction> TransactionPatientPeople { get; set; } = new List<Transaction>();

    [InverseProperty("RepresentativePerson")]
    public virtual ICollection<Transaction> TransactionRepresentativePeople { get; set; } = new List<Transaction>();
}
