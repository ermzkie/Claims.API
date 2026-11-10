using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("Province")]
public partial class Province
{
    [Key]
    [Column("ProvinceID")]
    public int ProvinceId { get; set; }

    [StringLength(100)]
    [Unicode(false)]
    public string ProvinceName { get; set; } = null!;

    [StringLength(2)]
    [Unicode(false)]
    public string ProvinceCode { get; set; } = null!;

    [Column("RegionID")]
    public int RegionId { get; set; }

    [InverseProperty("Province")]
    public virtual ICollection<CityMunicipality> CityMunicipalities { get; set; } = new List<CityMunicipality>();
}
