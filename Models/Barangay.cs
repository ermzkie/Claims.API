using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("Barangay")]
public partial class Barangay
{
    [Key]
    [Column("BarangayID")]
    public int BarangayId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string BarangayName { get; set; } = null!;

    [StringLength(3)]
    [Unicode(false)]
    public string? BarangayCode { get; set; }

    [Column("CityMunicipalityID")]
    public int? CityMunicipalityId { get; set; }

    [ForeignKey("CityMunicipalityId")]
    [InverseProperty("Barangays")]
    public virtual CityMunicipality? CityMunicipality { get; set; }

    [InverseProperty("BarangayNavigation")]
    public virtual ICollection<Person> People { get; set; } = new List<Person>();
}
