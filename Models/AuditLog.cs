using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Models;

[Table("AuditLog")]
[Index("Entity", "EntityId", Name = "IX_AuditLog_Entity")]
[Index("PersonId", Name = "IX_AuditLog_Person")]
public partial class AuditLog
{
    [Key]
    [Column("AuditLogID")]
    public int AuditLogId { get; set; }

    [StringLength(50)]
    [Unicode(false)]
    public string Entity { get; set; } = null!;

    [Column("EntityID")]
    public int EntityId { get; set; }

    [Column("PersonID")]
    public int? PersonId { get; set; }

    [StringLength(20)]
    [Unicode(false)]
    public string Action { get; set; } = null!;

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    [StringLength(500)]
    public string? Remark { get; set; }

    [Column("UserID")]
    public int UserId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DateCreated { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("AuditLogs")]
    public virtual User User { get; set; } = null!;
}
