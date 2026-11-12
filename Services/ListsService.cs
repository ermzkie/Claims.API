using Claims.API.Data;
using Claims.API.DTOs.Lists;
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

// The small lookup lists admins maintain from File Maintenance. None of these tables has a status
// column, so a value can't be hidden: it can be renamed, or deleted while nothing uses it.
public class ListsService(ApplicationDbContext db)
{
  private const int MaxText = 50;

  private interface ILookup
  {
    ListInfoDto Info { get; }
    Task<bool> NameTakenAsync(ApplicationDbContext db, string candidate, int? exceptId);
    Task<List<ListItemDto>> AllAsync(ApplicationDbContext db);
    Task<ListItemDto> AddAsync(ApplicationDbContext db, string? code, string name);
    Task<ListItemDto?> UpdateAsync(ApplicationDbContext db, int id, string? code, string name);
    Task<(bool Found, ListItemDto? Item)> FindAsync(ApplicationDbContext db, int id);
    Task DeleteAsync(ApplicationDbContext db, int id);
  }

  // Each table is a handful of rows, so rows are loaded and matched in memory.
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
    Func<ApplicationDbContext, int, Task<int>> usedBy) : ILookup where T : class, new()
  {
    public ListInfoDto Info { get; } = new(key, title, hasCode);

    private async Task<ListItemDto> ToDtoAsync(ApplicationDbContext db, T row) =>
      new(id(row), hasCode ? code(row) : null, name(row) ?? "", await usedBy(db, id(row)));

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

    public async Task<ListItemDto> AddAsync(ApplicationDbContext db, string? newCode, string newName)
    {
      var row = new T();
      if (numberedByHand)
      {
        var ids = await set(db).AsNoTracking().ToListAsync();
        setId(row, ids.Count == 0 ? 1 : ids.Max(id) + 1);
      }
      write(row, newCode, newName);
      set(db).Add(row);
      await db.SaveChangesAsync();
      return await ToDtoAsync(db, row);
    }

    public async Task<ListItemDto?> UpdateAsync(ApplicationDbContext db, int rowId, string? newCode, string newName)
    {
      var row = (await set(db).ToListAsync()).FirstOrDefault(r => id(r) == rowId);
      if (row is null)
      {
        return null;
      }
      write(row, newCode, newName);
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
      (d, v) => d.FamilyCompositions.CountAsync(f => f.RelationshipStatusId == v)),

    new Lookup<SourceOfAssistance>("sources-of-assistance", "Sources of assistance", hasCode: false, numberedByHand: false,
      d => d.SourceOfAssistances, r => r.SourceId, (_, _) => { },
      _ => null, r => r.SourceDesc, (r, _, n) => r.SourceDesc = n,
      (d, v) => d.FundSources.CountAsync(f => f.SourceId == v)),

    new Lookup<ModeOfAssistance>("modes-of-assistance", "Modes of assistance", hasCode: false, numberedByHand: false,
      d => d.ModeOfAssistances, r => r.ModeId, (_, _) => { },
      _ => null, r => r.ModeDesc, (r, _, n) => r.ModeDesc = n,
      (d, v) => d.Transactions.CountAsync(t => t.ModeId == v)),
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
    var (error, code, name) = await ValidateAsync(list, dto, exceptId: null);
    return error ?? new ListResult(ListOutcome.Ok, Item: await list.AddAsync(db, code, name!));
  }

  public async Task<ListResult> UpdateAsync(string key, int id, SaveListItemDto dto)
  {
    if (Find(key) is not { } list || !(await list.FindAsync(db, id)).Found)
    {
      return new ListResult(ListOutcome.NotFound, "Value not found.");
    }
    var (error, code, name) = await ValidateAsync(list, dto, exceptId: id);
    return error ?? new ListResult(ListOutcome.Ok, Item: await list.UpdateAsync(db, id, code, name!));
  }

  public async Task<ListResult> DeleteAsync(string key, int id)
  {
    if (Find(key) is not { } list || await list.FindAsync(db, id) is not (true, { } item))
    {
      return new ListResult(ListOutcome.NotFound, "Value not found.");
    }
    if (item.UsedBy > 0)
    {
      var records = item.UsedBy == 1 ? "1 record uses" : $"{item.UsedBy} records use";
      return new ListResult(ListOutcome.Conflict, $"Can't delete \"{item.Name}\": {records} it. You can rename it instead.");
    }
    await list.DeleteAsync(db, id);
    return new ListResult(ListOutcome.Ok);
  }

  private async Task<(ListResult? Error, string? Code, string? Name)> ValidateAsync(ILookup list, SaveListItemDto dto, int? exceptId)
  {
    var name = Clean(dto.Name);
    var code = list.Info.HasCode ? Clean(dto.Code) : null;
    if (name is null)
    {
      return (new ListResult(ListOutcome.Invalid, "Name is required."), null, null);
    }
    if (name.Length > MaxText || code?.Length > MaxText)
    {
      return (new ListResult(ListOutcome.Invalid, $"Name can be at most {MaxText} characters."), null, null);
    }
    return await list.NameTakenAsync(db, name, exceptId)
      ? (new ListResult(ListOutcome.Conflict, "That value is already in the list."), null, null)
      : (null, code, name);
  }

  private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
