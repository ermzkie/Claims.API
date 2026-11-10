using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("User")]
public partial class User
{
    [Key]
    [Column("UserID")]
    public int UserId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Username { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Password { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Lastname { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Firstname { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string? Middlename { get; set; }

    [Column("NameSuffixID")]
    public short? NameSuffixId { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    public bool Status { get; set; }

    [ForeignKey("NameSuffixId")]
    [InverseProperty("Users")]
    public virtual NameSuffix? NameSuffix { get; set; }

    [InverseProperty("CreatedByNavigation")]
    public virtual ICollection<Person> People { get; set; } = new List<Person>();

    [InverseProperty("User")]
    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
