namespace Claims.API.DTOs.Persons;

public class SaveFamilyMemberDto
{
  public int FamilyPersonId { get; set; }
  public short? RelationshipStatusId { get; set; }
  public string? Remark { get; set; }
}

public class RemoveFamilyMemberDto
{
  public string? Remark { get; set; }
}

public record FamilyLinkValues(int? MemberId, string? Member, short? RelationshipId, string? Relationship);

public record FamilyChangeDto(
  int AuditLogId,
  int FamilyCompId,
  string Action,
  FamilyLinkValues? Old,
  FamilyLinkValues? New,
  string? Remark,
  string? ChangedBy,
  DateTime Date);
