using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("UserRole")]
[Index("UserId", "RoleId", Name = "UX_UserRole_User_Role", IsUnique = true)]
public partial class UserRole
{
    [Key]
    [Column("UserRoleID")]
    public int UserRoleId { get; set; }

    [Column("UserID")]
    public int UserId { get; set; }

    [Column("RoleID")]
    public short RoleId { get; set; }

    public bool Status { get; set; }

    public int? CreatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    public int? LastUpdatedBy { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? DateLastUpdated { get; set; }

    [ForeignKey("RoleId")]
    [InverseProperty("UserRoles")]
    public virtual Role Role { get; set; } = null!;

    [ForeignKey("UserId")]
    [InverseProperty("UserRoles")]
    public virtual User User { get; set; } = null!;
}
