using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Keyless]
[Table("Address")]
public partial class Address
{
    [Column("ID")]
    public int Id { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? MunCode { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? BrgyCode { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Municipality { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Barangay { get; set; }

    [Column("Address")]
    [StringLength(50)]
    [Unicode(false)]
    public string? Address1 { get; set; }
}
