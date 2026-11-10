using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("NameSuffix")]
public partial class NameSuffix
{
    [Key]
    [Column("NameSuffixID")]
    public short NameSuffixId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? NameSuffixCode { get; set; }

    [InverseProperty("NameSuffix")]
    public virtual ICollection<Person> People { get; set; } = new List<Person>();

    [InverseProperty("NameSuffix")]
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
