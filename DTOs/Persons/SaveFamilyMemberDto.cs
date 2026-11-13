namespace Claims.API.DTOs.Persons;

public class SaveFamilyMemberDto
{
  public int FamilyPersonId { get; set; }
  public short? RelationshipStatusId { get; set; }
}
