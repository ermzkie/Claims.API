namespace Claims.API.Services;

// reports rely on this list
public static class RelationshipGroups
{
  public static readonly string[] All =
  [
    "Spouse/Partner", "Children", "Parents", "Siblings", "Grandchildren", "Grandparents",
    "Step family", "Extended family", "In-laws", "Others",
  ];

  public static int Order(string? group)
  {
    var i = Array.FindIndex(All, g => g == group);
    return i < 0 ? All.Length : i;
  }
}
