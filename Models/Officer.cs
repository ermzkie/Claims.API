using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

public partial class Officer
{
    [Key]
    [Column("IDNumber")]
    public int Idnumber { get; set; }

    [Unicode(false)]
    public string? Name { get; set; }

    [Unicode(false)]
    public string? Position { get; set; }

    [Unicode(false)]
    public string? Username { get; set; }

    [Unicode(false)]
    public string? Password { get; set; }

    [Unicode(false)]
    public string? AccountType { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Office { get; set; }
}
