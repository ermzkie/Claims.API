using Claims.API.Data;
using Claims.API.DTOs.ReferenceData;
using Claims.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Services;

public enum ListOutcome
{
  Ok,
  NotFound,
  Invalid,
  Conflict,
}

public record ListResult(ListOutcome Outcome, string? Message = null, ListItemDto? Item = null);

// most of these tables have no status column, so values are renamed or deleted when unused
public class ReferenceDataService(ApplicationDbContext db)
{
  private const int MaxText = 50;
  public const string LockedLane =
    "The S, V and R lanes are used by the queue order and can't be renamed to another code or deleted.";
  // the queue orders by these codes and the front desk suggests S or R
  private static readonly string[] QueueLaneCodes = ["S", "V", "R"];

  private interface ILookup
  {
    ListInfoDto Info { get; }
    Task<bool> NameTakenAsync(ApplicationDbContext db, string candidate, int? exceptId);
    Task<bool> CodeTakenAsync(ApplicationDbContext db, string candidate, int? exceptId);
    (string? Code, string? Error) CheckCode(string? code);
    bool UniqueCode { get; }
    Task<List<ListItemDto>> AllAsync(ApplicationDbContext db);
    Task<ListItemDto> AddAsync(ApplicationDbContext db, string? code, string name, string? group, IReadOnlyDictionary<string, bool>? flags);
    Task<ListItemDto?> UpdateAsync(ApplicationDbContext db, int id, string? code, string name, string? group, IReadOnlyDictionary<string, bool>? flags);
    Task<(bool Found, ListItemDto? Item)> FindAsync(ApplicationDbContext db, int id);
    Task DeleteAsync(ApplicationDbContext db, int id);
  }

  private sealed record Groups<T>(IReadOnlyList<string> Choices, Func<T, string?> Get, Action<T, string?> Set);

  // Set gets the sent flags (null when none were sent) and whether the row is new
  private sealed record Flags<T>(
    IReadOnlyList<ListFlagDto> Defs, Func<T, IReadOnlyDictionary<string, bool>> Get,
    Action<T, IReadOnlyDictionary<string, bool>?, bool> Set);

  private sealed class Lookup<T>(
    string key,
    string title,
    bool hasCode,
    bool numberedByHand,
    Func<ApplicationDbContext, DbSet<T>> set,
    Func<T, int> id,
    Action<T, int> setId,
    Func<T, string?> code,
    Func<T, string?> name,
    Action<T, string?, string> write,
    Func<ApplicationDbContext, int, Task<int>> usedBy,
    Groups<T>? groups = null,
    Flags<T>? flags = null,
    Func<T, bool>? locked = null,
    Func<string?, (string? Code, string? Error)>? codeRule = null) : ILookup where T : class, new()
  {
    public ListInfoDto Info { get; } = new(key, title, hasCode, groups?.Choices, flags?.Defs);

    public bool UniqueCode => codeRule is not null;

    public (string? Code, string? Error) CheckCode(string? value) =>
      codeRule?.Invoke(value) ?? (value, null);

    private async Task<ListItemDto> ToDtoAsync(ApplicationDbContext db, T row) =>
      new(id(row), hasCode ? code(row) : null, name(row) ?? "", await usedBy(db, id(row)), groups?.Get(row),
        flags?.Get(row), locked?.Invoke(row) ?? false);

    public async Task<List<ListItemDto>> AllAsync(ApplicationDbContext db)
    {
      var rows = await set(db).AsNoTracking().ToListAsync();
      var items = new List<ListItemDto>();
      foreach (var row in rows.OrderBy(id))
      {
        items.Add(await ToDtoAsync(db, row));
      }
      return items;
    }

    public async Task<ListItemDto> AddAsync(
      ApplicationDbContext db, string? newCode, string newName, string? group, IReadOnlyDictionary<string, bool>? sent)
    {
      var row = new T();
      if (numberedByHand)
      {
        var ids = await set(db).AsNoTracking().ToListAsync();
        setId(row, ids.Count == 0 ? 1 : ids.Max(id) + 1);
      }
      write(row, newCode, newName);
      groups?.Set(row, group);
      flags?.Set(row, sent, true);
      set(db).Add(row);
      await db.SaveChangesAsync();
      return await ToDtoAsync(db, row);
    }

    public async Task<ListItemDto?> UpdateAsync(
      ApplicationDbContext db, int rowId, string? newCode, string newName, string? group, IReadOnlyDictionary<string, bool>? sent)
    {
      var row = (await set(db).ToListAsync()).FirstOrDefault(r => id(r) == rowId);
      if (row is null)
      {
        return null;
      }
      write(row, newCode, newName);
      groups?.Set(row, group);
      flags?.Set(row, sent, false);
      await db.SaveChangesAsync();
      return await ToDtoAsync(db, row);
    }

    public async Task<(bool Found, ListItemDto? Item)> FindAsync(ApplicationDbContext db, int rowId)
    {
      var row = (await set(db).AsNoTracking().ToListAsync()).FirstOrDefault(r => id(r) == rowId);
      return row is null ? (false, null) : (true, await ToDtoAsync(db, row));
    }

    public async Task DeleteAsync(ApplicationDbContext db, int rowId)
    {
      var row = (await set(db).ToListAsync()).First(r => id(r) == rowId);
      set(db).Remove(row);
      await db.SaveChangesAsync();
    }

    public async Task<bool> NameTakenAsync(ApplicationDbContext db, string candidate, int? exceptId) =>
      (await set(db).AsNoTracking().ToListAsync())
        .Any(r => id(r) != exceptId && string.Equals(name(r)?.Trim(), candidate, StringComparison.OrdinalIgnoreCase));

    public async Task<bool> CodeTakenAsync(ApplicationDbContext db, string candidate, int? exceptId) =>
      (await set(db).AsNoTracking().ToListAsync())
        .Any(r => id(r) != exceptId && string.Equals(code(r)?.Trim(), candidate, StringComparison.OrdinalIgnoreCase));
  }

  private static readonly ILookup[] Lookups =
  [
    new Lookup<NameSuffix>("name-suffixes", "Name suffixes", hasCode: false, numberedByHand: true,
      d => d.NameSuffixes, r => r.NameSuffixId, (r, v) => r.NameSuffixId = (short)v,
      _ => null, r => r.NameSuffixCode, (r, _, n) => r.NameSuffixCode = n,
      async (d, v) => await d.People.CountAsync(p => p.NameSuffixId == v) + await d.Users.CountAsync(u => u.NameSuffixId == v)),

    new Lookup<CivilStatus>("civil-statuses", "Civil statuses", hasCode: true, numberedByHand: true,
      d => d.CivilStatuses, r => r.CivilStatusId, (r, v) => r.CivilStatusId = (short)v,
      r => r.CivilStatusCode, r => r.CivilStatusDesc, (r, c, n) => { r.CivilStatusCode = c; r.CivilStatusDesc = n; },
      (d, v) => d.People.CountAsync(p => p.CivilStatusId == v)),

    new Lookup<RelationshipStatus>("relationships", "Relationships", hasCode: false, numberedByHand: true,
      d => d.RelationshipStatuses, r => r.RelationshipStatusId, (r, v) => r.RelationshipStatusId = (short)v,
      _ => null, r => r.RelationshipStatusDesc, (r, _, n) => r.RelationshipStatusDesc = n,
      (d, v) => d.FamilyCompositions.CountAsync(f => f.RelationshipStatusId == v),
      new Groups<RelationshipStatus>(RelationshipGroups.All, r => r.RelationshipGroup, (r, g) => r.RelationshipGroup = g)),

    new Lookup<SourceOfAssistance>("sources-of-assistance", "Sources of assistance", hasCode: false, numberedByHand: false,
      d => d.SourceOfAssistances, r => r.SourceId, (_, _) => { },
      _ => null, r => r.SourceDesc, (r, _, n) => r.SourceDesc = n,
      (d, v) => d.FundSources.CountAsync(f => f.SourceId == v)),

    new Lookup<ModeOfAssistance>("modes-of-assistance", "Modes of assistance", hasCode: false, numberedByHand: false,
      d => d.ModeOfAssistances, r => r.ModeId, (_, _) => { },
      _ => null, r => r.ModeDesc, (r, _, n) => r.ModeDesc = n,
      (d, v) => d.ServiceFundAllocations.CountAsync(allocation => allocation.ModeId == v)),

    new Lookup<LaneType>("lanes", "Lanes", hasCode: true, numberedByHand: false,
      d => d.LaneTypes, r => r.LaneTypeId, (_, _) => { },
      r => r.LaneTypeCode?.Trim(), r => r.LaneTypeDesc, (r, c, n) => { r.LaneTypeCode = c; r.LaneTypeDesc = n; },
      (d, v) => d.Transactions.CountAsync(t => t.LaneTypeId == v),
      flags: new Flags<LaneType>(
        [new ListFlagDto("active", "On at the front desk"), new ListFlagDto("needsEndorsement", "Needs an endorsement")],
        r => new Dictionary<string, bool> { ["active"] = r.Status, ["needsEndorsement"] = r.NeedsEndorsement },
        (r, sent, isNew) =>
        {
          r.Status = sent?.TryGetValue("active", out var on) == true ? on : isNew || r.Status;
          r.NeedsEndorsement = sent?.TryGetValue("needsEndorsement", out var needs) == true ? needs : r.NeedsEndorsement;
        }),
      locked: r => QueueLaneCodes.Contains(r.LaneTypeCode?.Trim().ToUpperInvariant()),
      codeRule: c =>
      {
        var value = c?.Trim().ToUpperInvariant();
        return value is { Length: >= 1 and <= 3 } && value.All(char.IsAsciiLetterUpper)
          ? (value, null)
          : (null, "Lane code is 1 to 3 letters.");
      }),
  ];

  private static ILookup? Find(string key) => Lookups.FirstOrDefault(l => l.Info.Key == key);

  public IReadOnlyList<ListInfoDto> Index() => [.. Lookups.Select(l => l.Info)];

  public async Task<List<ListItemDto>?> AllAsync(string key) =>
    Find(key) is { } list ? await list.AllAsync(db) : null;

  public async Task<ListResult> AddAsync(string key, SaveListItemDto dto)
  {
    if (Find(key) is not { } list)
    {
      return new ListResult(ListOutcome.NotFound, "List not found.");
    }
    var (error, code, name, group) = await ValidateAsync(list, dto, exceptId: null);
    return error ?? new ListResult(ListOutcome.Ok, Item: await list.AddAsync(db, code, name!, group, dto.Flags));
  }

  public async Task<ListResult> UpdateAsync(string key, int id, SaveListItemDto dto)
  {
    if (Find(key) is not { } list || await list.FindAsync(db, id) is not (true, { } current))
    {
      return new ListResult(ListOutcome.NotFound, "Value not found.");
    }
    var (error, code, name, group) = await ValidateAsync(list, dto, exceptId: id);
    if (error is null && current.Locked && !string.Equals(code, current.Code, StringComparison.OrdinalIgnoreCase))
    {
      return new ListResult(ListOutcome.Invalid, LockedLane);
    }
    return error ?? new ListResult(ListOutcome.Ok, Item: await list.UpdateAsync(db, id, code, name!, group, dto.Flags));
  }

  public async Task<ListResult> DeleteAsync(string key, int id)
  {
    if (Find(key) is not { } list || await list.FindAsync(db, id) is not (true, { } item))
    {
      return new ListResult(ListOutcome.NotFound, "Value not found.");
    }
    if (item.Locked)
    {
      return new ListResult(ListOutcome.Conflict, LockedLane);
    }
    if (item.UsedBy > 0)
    {
      var records = item.UsedBy == 1 ? "1 record uses" : $"{item.UsedBy} records use";
      return new ListResult(ListOutcome.Conflict, $"Can't delete \"{item.Name}\": {records} it. You can rename it instead.");
    }
    await list.DeleteAsync(db, id);
    return new ListResult(ListOutcome.Ok);
  }

  private async Task<(ListResult? Error, string? Code, string? Name, string? Group)> ValidateAsync(
    ILookup list, SaveListItemDto dto, int? exceptId)
  {
    var name = Clean(dto.Name);
    var (code, codeError) = list.Info.HasCode ? list.CheckCode(Clean(dto.Code)) : (null, null);
    if (name is null)
    {
      return (new ListResult(ListOutcome.Invalid, "Name is required."), null, null, null);
    }
    if (codeError is not null)
    {
      return (new ListResult(ListOutcome.Invalid, codeError), null, null, null);
    }
    if (list.UniqueCode && code is not null && await list.CodeTakenAsync(db, code, exceptId))
    {
      return (new ListResult(ListOutcome.Conflict, "That code is already used by another lane."), null, null, null);
    }
    if (name.Length > MaxText || code?.Length > MaxText)
    {
      return (new ListResult(ListOutcome.Invalid, $"Name can be at most {MaxText} characters."), null, null, null);
    }
    string? group = null;
    if (list.Info.Groups is { } choices)
    {
      group = choices.FirstOrDefault(g => string.Equals(g, Clean(dto.Group), StringComparison.OrdinalIgnoreCase));
      if (group is null)
      {
        return (new ListResult(ListOutcome.Invalid, $"Group must be one of: {string.Join(", ", choices)}."), null, null, null);
      }
    }
    return await list.NameTakenAsync(db, name, exceptId)
      ? (new ListResult(ListOutcome.Conflict, "That value is already in the list."), null, null, null)
      : (null, code, name, group);
  }

  private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
