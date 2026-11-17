using Claims.API.Data;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Services;

// CreditorPayments still works if finance rewords its descriptions
public record FinanceAccountDto(
  int Id, string? Code, string? Name, decimal? Amount, int AidObligations = 0, int CreditorPayments = 0)
{
  public bool PaysAid => AidObligations > 0 || CreditorPayments > 0;
}

// Obligated is approved only; Pending kept apart
public record FinanceUsageDto(
  int Id,
  decimal? Released,
  decimal? Obligated,
  decimal? Pending = null,
  decimal? Original = null,
  decimal? Supplemental = null,
  decimal? Adjustments = null);

public class FinanceUnavailableException(Exception inner)
  : Exception("The finance system can't be reached right now.", inner);

public interface IFinanceAccounts
{
  Task<IReadOnlyList<FinanceAccountDto>> AppropriationsAsync(int year);
  Task<IReadOnlyList<FinanceAccountDto>> TrustFundsAsync();
  Task<IReadOnlyList<FinanceUsageDto>> AppropriationUsageAsync(IReadOnlyCollection<int> ids);
  Task<IReadOnlyList<FinanceUsageDto>> TrustFundUsageAsync(IReadOnlyCollection<int> ids);
}

public class SqlFinanceAccounts(ApplicationDbContext db) : IFinanceAccounts
{
  private const string Finance = "[188.180.10.235].sysPFM.dbo";

  // OPSWDO in the finance system's b_OFFICES
  private const int OfficeId = 17;

  // checked on 2024-2026 data: only AICS, Donations and PAGCOR match
  private const string AidLines = """
    e.E_STAT <> 'CANCELLED' AND (e.E_DESC LIKE '%medical assistance%' OR e.E_DESC LIKE '%hospitalization assistance%'
      OR e.E_DESC LIKE '%mortuary assistance%' OR e.E_DESC LIKE '%burial assistance%' OR e.E_DESC LIKE '%financial assistance%')
    """;

  // letters only, so INC and punctuation don't matter
  private static string Key(string column) =>
    $"UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE({column}, ' ', ''), '.', ''), ',', ''), '''', ''), '-', ''), '&', ''), 'INCORPORATED', ''), 'INC', ''))";

  private sealed class PayeeRow
  {
    public int Id { get; set; }
    public string? Payee { get; set; }
    public int N { get; set; }
  }

  // grouped on the finance server, matching across servers was too slow
  private async Task<Dictionary<int, int>> CreditorPaymentsAsync(string sql, params object[] args)
  {
    var payees = await RunAsync(() => db.Database.SqlQueryRaw<PayeeRow>(sql, args).ToListAsync());
    var creditors = (await RunAsync(() => db.Database
        .SqlQueryRaw<string>($"SELECT {Key("c.CreditorName")} AS Value FROM dbo.Creditor c WHERE c.CreditorName IS NOT NULL")
        .ToListAsync()))
      .ToHashSet();
    return payees
      .Where(p => p.Payee is not null && creditors.Contains(p.Payee))
      .GroupBy(p => p.Id)
      .ToDictionary(g => g.Key, g => g.Sum(p => p.N));
  }

  private sealed class Row
  {
    public int Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public decimal? Amount { get; set; }
    public int AidObligations { get; set; }
  }

  public async Task<IReadOnlyList<FinanceAccountDto>> AppropriationsAsync(int year)
  {
    var accounts = await AppropriationRowsAsync(year);
    var paid = await CreditorPaymentsAsync($$"""
      SELECT e.A_IDn AS Id, {{Key("o.OBR_PYNAME")}} AS Payee, COUNT(*) AS N
      FROM {{Finance}}.b_EXPENSES e
      JOIN {{Finance}}.b_OBRS o ON o.OBR_IDn = e.OBR_IDn
      WHERE e.A_IDn IN (SELECT x.A_IDn FROM {{Finance}}.b_APPROPRIATIONS x WHERE x.O_IDn = {{OfficeId}} AND x.A_FORYEAR = {0})
        AND e.E_STAT <> 'CANCELLED'
      GROUP BY e.A_IDn, {{Key("o.OBR_PYNAME")}}
      """, year.ToString());
    return accounts.Select(a => a with { CreditorPayments = paid.GetValueOrDefault(a.Id) }).ToList();
  }

  private Task<IReadOnlyList<FinanceAccountDto>> AppropriationRowsAsync(int year) => QueryAsync($$"""
    SELECT a.A_IDn AS Id, ac.AC_CODE AS Code,
      COALESCE(NULLIF(LTRIM(RTRIM(s.SAC_DESCRIPTION)), ''), ac.AC_DESCRIPTION) AS Name,
      a.A_AMOUNT + ISNULL((SELECT SUM(su.A_AMOUNT) FROM {{Finance}}.b_SUPPLIMENTAL su
        WHERE su.A_IDn = a.A_IDn AND su.A_STATUS = 1), 0) AS Amount,
      ISNULL(aid.n, 0) AS AidObligations
    FROM {{Finance}}.b_APPROPRIATIONS a
    LEFT JOIN {{Finance}}.b_ACCOUNTS ac ON ac.AC_IDn = a.AC_IDn
    LEFT JOIN {{Finance}}.b_ACCOUNTSSUB s ON s.SAC_IDn = a.SAC_IDn
    LEFT JOIN (
      SELECT e.A_IDn, COUNT(*) AS n FROM {{Finance}}.b_EXPENSES e
      WHERE e.A_IDn IN (SELECT x.A_IDn FROM {{Finance}}.b_APPROPRIATIONS x WHERE x.O_IDn = {{OfficeId}} AND x.A_FORYEAR = {0})
        AND {{AidLines}}
      GROUP BY e.A_IDn) aid ON aid.A_IDn = a.A_IDn
    WHERE a.O_IDn = {{OfficeId}} AND a.A_STATUS = 1 AND a.A_FORYEAR = {0}
    """, year.ToString());

  public async Task<IReadOnlyList<FinanceAccountDto>> TrustFundsAsync()
  {
    var accounts = await TrustFundRowsAsync();
    // payee is on the OBR or the trust fund request
    var payee = "COALESCE(o.OBR_PYNAME, p.busn_name, LTRIM(CONCAT(p.p_fname, ' ', p.p_lname)))";
    var paid = await CreditorPaymentsAsync($$"""
      SELECT e.TF_ID AS Id, {{Key(payee)}} AS Payee, COUNT(*) AS N
      FROM {{Finance}}.b_EXPENSES e
      LEFT JOIN {{Finance}}.b_OBRS o ON o.OBR_IDn = e.OBR_IDn
      LEFT JOIN {{Finance}}.TF_REQUEST r ON r.tfreq_id = e.TFREQ_ID
      LEFT JOIN {{Finance}}.b_PAYEES p ON p.payee_id = r.payee_id
      WHERE e.TF_ID IN (SELECT x.tf_id FROM {{Finance}}.TRUST_FUND x WHERE x.o_idn = {{OfficeId}})
        AND e.E_STAT <> 'CANCELLED'
      GROUP BY e.TF_ID, {{Key(payee)}}
      """);
    return accounts.Select(a => a with { CreditorPayments = paid.GetValueOrDefault(a.Id) }).ToList();
  }

  private Task<IReadOnlyList<FinanceAccountDto>> TrustFundRowsAsync() => QueryAsync($$"""
    SELECT f.tf_id AS Id, f.tf_code AS Code, f.tf_desc AS Name,
      (SELECT SUM(t.amount) FROM {{Finance}}.TF_AMOUNTS t
        WHERE t.tf_id = f.tf_id AND ISNULL(t.del_status, 0) = 0) AS Amount,
      ISNULL(aid.n, 0) AS AidObligations
    FROM {{Finance}}.TRUST_FUND f
    LEFT JOIN (
      SELECT e.TF_ID, COUNT(*) AS n FROM {{Finance}}.b_EXPENSES e
      WHERE e.TF_ID IN (SELECT x.tf_id FROM {{Finance}}.TRUST_FUND x WHERE x.o_idn = {{OfficeId}}) AND {{AidLines}}
      GROUP BY e.TF_ID) aid ON aid.TF_ID = f.tf_id
    WHERE f.o_idn = {{OfficeId}} AND f.status = 1
    """);

  private sealed class UsageRow
  {
    public int Id { get; set; }
    public decimal? Released { get; set; }
    public decimal? Obligated { get; set; }
    public decimal? Pending { get; set; }
    public decimal? Original { get; set; }
    public decimal? Supplemental { get; set; }
    public decimal? Adjustments { get; set; }
  }

  // unlike getAnnualBalance: pending kept apart, deleted receipts skipped
  public Task<IReadOnlyList<FinanceUsageDto>> AppropriationUsageAsync(IReadOnlyCollection<int> ids) => UsageAsync(ids, $$"""
    SELECT v.Id,
      (SELECT SUM(b.AL_AMT) FROM {{Finance}}.b_ALLOTMENTS b WHERE b.A_IDn = v.Id) AS Released,
      (SELECT SUM(e.E_AMT) FROM {{Finance}}.b_EXPENSES e WHERE e.A_IDn = v.Id AND e.E_STAT = 'Approved') AS Obligated,
      (SELECT SUM(e.E_AMT) FROM {{Finance}}.b_EXPENSES e WHERE e.A_IDn = v.Id AND e.E_STAT = 'Pending') AS Pending,
      (SELECT a.A_AMOUNT FROM {{Finance}}.b_APPROPRIATIONS a WHERE a.A_IDn = v.Id) AS Original,
      (SELECT SUM(b.AL_AMT) FROM {{Finance}}.b_ALLOTMENTS b WHERE b.A_IDn = v.Id AND b.AL_TYPE = 'S') AS Supplemental,
      (SELECT SUM(b.AL_AMT) FROM {{Finance}}.b_ALLOTMENTS b
        WHERE b.A_IDn = v.Id AND b.AL_TYPE NOT IN ('1Q', '2Q', '3Q', '4Q', 'S')) AS Adjustments
    FROM (VALUES {IDS}) v(Id)
    """);

  public Task<IReadOnlyList<FinanceUsageDto>> TrustFundUsageAsync(IReadOnlyCollection<int> ids) => UsageAsync(ids, $$"""
    SELECT v.Id,
      (SELECT SUM(t.amount) FROM {{Finance}}.TF_AMOUNTS t WHERE t.tf_id = v.Id AND ISNULL(t.del_status, 0) = 0) AS Released,
      (SELECT SUM(e.E_AMT) FROM {{Finance}}.b_EXPENSES e WHERE e.TF_ID = v.Id AND e.E_STAT = 'Approved') AS Obligated,
      (SELECT SUM(e.E_AMT) FROM {{Finance}}.b_EXPENSES e WHERE e.TF_ID = v.Id AND e.E_STAT = 'Pending') AS Pending,
      CAST(NULL AS decimal(18, 2)) AS Original,
      CAST(NULL AS decimal(18, 2)) AS Supplemental,
      CAST(NULL AS decimal(18, 2)) AS Adjustments
    FROM (VALUES {IDS}) v(Id)
    """);

  private async Task<IReadOnlyList<FinanceUsageDto>> UsageAsync(IReadOnlyCollection<int> ids, string sql)
  {
    if (ids.Count == 0)
    {
      return [];
    }
    var values = string.Join(",", ids.Select((_, i) => $"({{{i}}})"));
    var rows = await RunAsync(() =>
      db.Database.SqlQueryRaw<UsageRow>(sql.Replace("{IDS}", values), ids.Cast<object>().ToArray()).ToListAsync());
    return rows
      .Select(r => new FinanceUsageDto(r.Id, r.Released, r.Obligated, r.Pending, r.Original, r.Supplemental, r.Adjustments))
      .ToList();
  }

  private async Task<IReadOnlyList<FinanceAccountDto>> QueryAsync(string sql, params object[] args)
  {
    var rows = await RunAsync(() => db.Database.SqlQueryRaw<Row>(sql, args).ToListAsync());
    return rows
      .Select(r => new FinanceAccountDto(r.Id, Tidy(r.Code), Tidy(r.Name), r.Amount, r.AidObligations))
      .OrderByDescending(r => r.Amount)
      .ThenBy(r => r.Id)
      .ToList();
  }

  private async Task<T> RunAsync<T>(Func<Task<T>> query)
  {
    try
    {
      // a slow linked server shouldn't hang the page
      db.Database.SetCommandTimeout(TimeSpan.FromSeconds(20));
      return await query();
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      throw new FinanceUnavailableException(ex);
    }
  }

  // finance descriptions carry trailing spaces and line breaks
  private static string? Tidy(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
