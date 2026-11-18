using System.Globalization;
using System.Text;

namespace Claims.API.Services;

public record NameParts(string Last, string First, string Middle, string? Suffix);

public enum MatchLevel
{
  None,
  Similar,
  Same,
}

public record NameMatch(MatchLevel Level, string Reason);

public static class PersonMatcher
{
  // V is left out: in old records a trailing V is usually a middle initial
  private static readonly HashSet<string> Suffixes = ["JR", "SR", "II", "III", "IV"];
  private static readonly char[] Separators = [' ', ',', '.'];

  public static string Letters(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return "";
    }
    var builder = new StringBuilder(value.Length);
    foreach (var c in value.Normalize(NormalizationForm.FormD))
    {
      if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
      {
        continue;
      }
      var upper = char.ToUpperInvariant(c);
      if (upper is >= 'A' and <= 'Z')
      {
        builder.Append(upper);
      }
    }
    return builder.ToString();
  }

  // old records type the suffix into the name
  public static NameParts Parse(string? last, string? first, string? middle, string? suffixCode)
  {
    var (lastName, lastSuffix) = SplitSuffix(last);
    var (firstName, firstSuffix) = SplitSuffix(first);
    var suffix = Letters(suffixCode) is { Length: > 0 } code ? code : firstSuffix ?? lastSuffix;
    return new NameParts(Letters(lastName), Letters(firstName), Letters(middle), suffix);
  }

  public static string GroupKey(NameParts p) => $"{p.Last}|{p.First}|{p.Middle}|{p.Suffix}";

  public static NameMatch Compare(NameParts a, DateOnly? aBorn, NameParts b, DateOnly? bBorn)
  {
    var none = new NameMatch(MatchLevel.None, "");
    if (a.Last.Length == 0 || a.First.Length == 0 || b.Last.Length == 0 || b.First.Length == 0)
    {
      return none;
    }
    if (aBorn is not null && bBorn is not null && aBorn != bBorn)
    {
      return none;
    }
    // JR and SR are father and son
    if (a.Suffix is not null && b.Suffix is not null && a.Suffix != b.Suffix)
    {
      return none;
    }

    var direct = a.Last == b.Last && a.First == b.First;
    var swapped = !direct && a.Last == b.First && a.First == b.Last;
    if (!direct && !swapped)
    {
      return none;
    }

    string? middle = null;
    if (a.Middle != b.Middle)
    {
      if (a.Middle.Length == 0 || b.Middle.Length == 0)
      {
        middle = "Only one has a middle name";
      }
      else if ((a.Middle.Length == 1 || b.Middle.Length == 1) && a.Middle[0] == b.Middle[0])
      {
        middle = "Middle name is only an initial";
      }
      else
      {
        return none;
      }
    }

    var oneSuffix = a.Suffix != b.Suffix;
    if (direct && middle is null && !oneSuffix)
    {
      var born = aBorn is not null && aBorn == bBorn;
      return new NameMatch(MatchLevel.Same, born ? "Same name and birthdate" : "Same name");
    }

    List<string> reasons = [];
    if (swapped)
    {
      reasons.Add("First and last name swapped");
    }
    if (middle is not null)
    {
      reasons.Add(middle);
    }
    if (oneSuffix)
    {
      reasons.Add($"Only one has the suffix {a.Suffix ?? b.Suffix}");
    }
    return new NameMatch(MatchLevel.Similar, string.Join(", ", reasons));
  }

  private static (string? Name, string? Suffix) SplitSuffix(string? name)
  {
    var words = (name ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    if (words.Length > 1 && Letters(words[^1]) is var last && Suffixes.Contains(last))
    {
      return (string.Join(' ', words[..^1]), last);
    }
    return (name, null);
  }
}
