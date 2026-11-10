using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("CityMunicipality")]
public partial class CityMunicipality
{
    [Key]
    [Column("CityMunicipalityID")]
    public int CityMunicipalityId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string CityMunicipalityName { get; set; } = null!;

    [StringLength(2)]
    [Unicode(false)]
    public string CityMunicipalityCode { get; set; } = null!;

    [Column("ProvinceID")]
    public int ProvinceId { get; set; }

    [InverseProperty("CityMunicipality")]
    public virtual ICollection<Barangay> Barangays { get; set; } = new List<Barangay>();

    [ForeignKey("ProvinceId")]
    [InverseProperty("CityMunicipalities")]
    public virtual Province Province { get; set; } = null!;
}
