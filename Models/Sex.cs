using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("Sex")]
public partial class Sex
{
    [Key]
    [Column("SexID")]
    public short SexId { get; set; }

    [StringLength(1)]
    [Unicode(false)]
    public string? SexCode { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? SexDesc { get; set; }

    [InverseProperty("Sex")]
    public virtual ICollection<Person> People { get; set; } = new List<Person>();
}
