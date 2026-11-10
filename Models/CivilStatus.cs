using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("CivilStatus")]
public partial class CivilStatus
{
    [Key]
    [Column("CivilStatusID")]
    public short CivilStatusId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? CivilStatusCode { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? CivilStatusDesc { get; set; }

    [InverseProperty("CivilStatus")]
    public virtual ICollection<Person> People { get; set; } = new List<Person>();
}
