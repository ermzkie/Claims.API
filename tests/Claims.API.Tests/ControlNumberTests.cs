using System.Net;
using System.Net.Http.Json;
using Claims.API.DTOs.Transactions;
using Claims.API.Models;
using Claims.API.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Claims.API.Tests.Infrastructure.TestData;

namespace Claims.API.Tests;

public class ControlNumberTests
{
  private const string Password = "Str0ng-Passw0rd!x";
  private const short CreditSlip = 1, Cash = 2, GuaranteeLetter = 3;

  private sealed class Office : IAsyncDisposable
  {
    public required ClaimsApiFactory App { get; init; }
    public required HttpClient Front { get; init; }
    public required HttpClient Worker { get; init; }
    public required HttpClient Worker2 { get; init; }
    public required HttpClient Approver { get; init; }
    public required HttpClient Admin { get; init; }
    public short LaneId { get; set; }
    public int PatientId { get; set; }

    public async ValueTask DisposeAsync()
    {
      Front.Dispose();
      Worker.Dispose();
      Worker2.Dispose();
      Approver.Dispose();
      Admin.Dispose();
      await App.DisposeAsync();
    }
  }

  private static async Task<Office> SetupAsync()
  {
    var app = new ClaimsApiFactory();
    await SeedRolesAsync(app);
    await CreateUserAsync(app, "front", Password, "Frontliner");
    var workerId = await CreateUserAsync(app, "worker", Password, "Social Worker");
    await CreateUserAsync(app, "worker2", Password, "Social Worker");
    var approverId = await CreateUserAsync(app, "approver", Password, "Approver");
    await CreateUserAsync(app, "admin", Password, "Administrator");
    var office = new Office
    {
      App = app,
      Front = await LoginAsAsync(app, "front", Password),
      Worker = await LoginAsAsync(app, "worker", Password),
      Worker2 = await LoginAsAsync(app, "worker2", Password),
      Approver = await LoginAsAsync(app, "approver", Password),
      Admin = await LoginAsAsync(app, "admin", Password),
    };

    await using var db = app.CreateDbContext();
    var worker = await db.Users.SingleAsync(u => u.UserId == workerId);
    worker.Firstname = "Ana";
    worker.Lastname = "Reyes";
    var approver = await db.Users.SingleAsync(u => u.UserId == approverId);
    approver.Firstname = "Liza";
    approver.Lastname = "Cruz";
    var lane = new LaneType { LaneTypeCode = "R", LaneTypeDesc = "Regular Lane" };
    var patient = new Person
    {
      FirstName = "MARIA", LastName = "SANTOS", Status = true, Birthdate = new DateOnly(1958, 3, 1),
      Barangay = "ZONE III", Municipality = "KORONADAL",
    };
    db.AddRange(lane, patient,
      new Creditor { CreditorId = 501, CreditorName = "PROVINCIAL HOSPITAL", Status = true },
      new FundSource { FundSourceId = 601, Year = 2026, Status = true },
      new ModeOfAssistance { ModeId = CreditSlip, ModeDesc = "Credit Slip" },
      new ModeOfAssistance { ModeId = Cash, ModeDesc = "Cash" },
      new ModeOfAssistance { ModeId = GuaranteeLetter, ModeDesc = "Guarantee Letter" });
    await db.SaveChangesAsync();
    await db.AddProgramsAsync(("A", "AICS"), ("G", "Donations - OPSWDO"));
    office.LaneId = lane.LaneTypeId;
    office.PatientId = patient.PersonId;
    return office;
  }

  // filed by the front desk, set and sent by the worker, ready for an approver
  private static async Task<int> SentAsync(Office o, string program = "A", string date = "2026-11-10T09:00",
    short mode = CreditSlip, decimal amount = 2500.5m)
  {
    var created = await o.Front.PostAsJsonAsync("/api/transactions/save", new
    {
      date, laneTypeId = o.LaneId, patientPersonId = o.PatientId, confirmSameDay = true,
    });
    Assert.Equal(HttpStatusCode.Created, created.StatusCode);
    var id = (await created.Content.ReadFromJsonAsync<TransactionResponseDto>())!.TransactionId;
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync($"/api/transactions/{id}/checkout-social-worker", null)).StatusCode);
    var saved = await o.Worker.PostAsJsonAsync("/api/transactions/save", new
    {
      transactionId = id, assistanceCode = program, amount, modeId = mode, creditorId = 501, fundSourceId = 601,
    });
    Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync($"/api/transactions/{id}/submit-for-approval", null)).StatusCode);
    return id;
  }

  private static async Task<string?> ApproveAsync(Office o, int id)
  {
    var approved = await o.Approver.PostAsync($"/api/transactions/{id}/approve", null);
    Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
    return (await approved.Content.ReadFromJsonAsync<TransactionResponseDto>())!.RefNo;
  }

  private static Task<HttpResponseMessage> Print(HttpClient client, int id) =>
    client.PostAsync($"/api/transactions/{id}/print", null);

  [Fact]
  public async Task Approve_GivesTheNextNumberForTheProgramAndDay()
  {
    await using var o = await SetupAsync();
    var first = await SentAsync(o);
    var second = await SentAsync(o);
    var otherProgram = await SentAsync(o, program: "G");
    var otherDay = await SentAsync(o, date: "2026-11-11T09:00");

    Assert.Equal("A-11-10-26-1", await ApproveAsync(o, first));
    Assert.Equal("A-11-10-26-2", await ApproveAsync(o, second));
    Assert.Equal("G-11-10-26-1", await ApproveAsync(o, otherProgram));
    Assert.Equal("A-11-11-26-1", await ApproveAsync(o, otherDay));

    await using var db = o.App.CreateDbContext();
    Assert.Equal("A-11-10-26-2", (await db.Transactions.FindAsync(second))!.RefNo);
  }

  [Fact]
  public async Task Approve_CountsOnFromOldNumbers_SkipsHandTypedOnes()
  {
    await using var o = await SetupAsync();
    await using (var db = o.App.CreateDbContext())
    {
      db.Transactions.AddRange(
        new Transaction { PatientPersonId = o.PatientId, LaneTypeId = o.LaneId, Date = new DateTime(2026, 11, 10), AssistanceCode = "A", RefNo = "A-11-10-26-7", Status = true },
        new Transaction { PatientPersonId = o.PatientId, LaneTypeId = o.LaneId, Date = new DateTime(2026, 11, 10), AssistanceCode = "A", RefNo = "A-11-10-26-X", Status = true },
        new Transaction { PatientPersonId = o.PatientId, LaneTypeId = o.LaneId, Date = new DateTime(2026, 11, 10), AssistanceCode = "A", RefNo = "A-11-10-26-10a", Status = true });
      await db.SaveChangesAsync();
    }
    var id = await SentAsync(o);

    Assert.Equal("A-11-10-26-8", await ApproveAsync(o, id));
  }

  [Fact]
  public async Task Approve_KeepsANumberTheClaimAlreadyHas()
  {
    await using var o = await SetupAsync();
    var id = await SentAsync(o);
    await using (var db = o.App.CreateDbContext())
    {
      (await db.Transactions.FindAsync(id))!.RefNo = "A-11-09-26-118";
      await db.SaveChangesAsync();
    }

    Assert.Equal("A-11-09-26-118", await ApproveAsync(o, id));
  }

  [Fact]
  public async Task Approve_RefusedOnesTakeNoNumber()
  {
    await using var o = await SetupAsync();
    var id = await SentAsync(o);
    // the worker who sent it can't approve it
    Assert.Equal(HttpStatusCode.Forbidden, (await o.Worker.PostAsync($"/api/transactions/{id}/approve", null)).StatusCode);

    await using var db = o.App.CreateDbContext();
    Assert.Null((await db.Transactions.FindAsync(id))!.RefNo);
    Assert.Equal("A-11-10-26-1", await ApproveAsync(o, id));
  }

  [Fact]
  public async Task Print_GivesTheLetterOrSlip_SecondCopyIsAReprint()
  {
    await using var o = await SetupAsync();
    var slip = await SentAsync(o, mode: CreditSlip);
    var letter = await SentAsync(o, mode: GuaranteeLetter);
    await ApproveAsync(o, slip);
    await ApproveAsync(o, letter);

    var first = await Print(o.Worker, slip);
    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    var doc = (await first.Content.ReadFromJsonAsync<ClaimDocumentDto>())!;
    Assert.Equal("Credit Slip", doc.Title);
    Assert.Equal("A-11-10-26-1", doc.ControlNo);
    Assert.Equal("PROVINCIAL HOSPITAL", doc.CreditorName);
    Assert.Equal("MARIA SANTOS", doc.PatientName);
    Assert.Equal("ZONE III, KORONADAL", doc.Address);
    Assert.Equal("AICS", doc.ProgramDesc);
    Assert.Equal(2500.5m, doc.Amount);
    Assert.Equal("ANA REYES", doc.SocialWorkerName);
    Assert.Equal("LIZA CRUZ", doc.ApproverName);
    Assert.False(doc.Reprint);

    var again = (await (await Print(o.Worker, slip)).Content.ReadFromJsonAsync<ClaimDocumentDto>())!;
    Assert.True(again.Reprint);

    var gl = (await (await Print(o.Approver, letter)).Content.ReadFromJsonAsync<ClaimDocumentDto>())!;
    Assert.Equal("Guarantee Letter", gl.Title);
    Assert.Equal("A-11-10-26-2", gl.ControlNo);

    var history = (await o.Admin.GetFromJsonAsync<List<TransactionHistoryDto>>($"/api/transactions/{slip}/history"))!;
    Assert.Equal(2, history.Count(h => h.Action == "Printed"));
  }

  [Fact]
  public async Task Print_DoesNotRelease()
  {
    await using var o = await SetupAsync();
    var id = await SentAsync(o);
    await ApproveAsync(o, id);
    Assert.Equal(HttpStatusCode.OK, (await Print(o.Worker, id)).StatusCode);

    var claim = (await o.Worker.GetFromJsonAsync<TransactionResponseDto>($"/api/transactions/{id}"))!;
    Assert.NotEqual(true, claim.IsReleased);
    Assert.Equal(HttpStatusCode.OK, (await o.Worker.PostAsync($"/api/transactions/{id}/release", null)).StatusCode);
  }

  [Fact]
  public async Task Print_OnlyApprovedNonCash_ByTheWorkerApproversOrAdmins()
  {
    await using var o = await SetupAsync();
    var waiting = await SentAsync(o);
    var cash = await SentAsync(o, mode: Cash);
    var approved = await SentAsync(o);
    await ApproveAsync(o, cash);
    await ApproveAsync(o, approved);

    var notYet = await Print(o.Worker, waiting);
    Assert.Equal(HttpStatusCode.Conflict, notYet.StatusCode);
    Assert.Contains("Only approved claims can be printed.", await notYet.Content.ReadAsStringAsync());
    var noPaper = await Print(o.Worker, cash);
    Assert.Equal(HttpStatusCode.Conflict, noPaper.StatusCode);
    Assert.Contains("Cash claims have no letter or slip.", await noPaper.Content.ReadAsStringAsync());

    Assert.Equal(HttpStatusCode.Forbidden, (await Print(o.Worker2, approved)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await Print(o.Front, approved)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await Print(o.Admin, approved)).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await Print(o.Admin, 999999)).StatusCode);
  }
}
