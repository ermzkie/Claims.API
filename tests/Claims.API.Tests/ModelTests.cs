using Claims.API.Models;
using Microsoft.EntityFrameworkCore;
using Claims.API.Tests.Infrastructure;

namespace Claims.API.Tests;

public class ModelTests
{
  [Fact]
  public async Task User_PasswordHash_IsStoredInPasswordColumn_AndNewColumnsRoundTrip()
  {
    await using var app = new ClaimsApiFactory();
    int id;
    await using (var db = app.CreateDbContext())
    {
      var user = new User
      {
        Username = "a",
        Password = new string('x', 84),
        MustChangePassword = true,
        FailedLoginCount = 2,
        LockoutEnd = app.Clock.GetLocalNow().DateTime.AddHours(1)
      };
      db.Users.Add(user);
      await db.SaveChangesAsync();
      id = user.UserId;
    }

    await using var db2 = app.CreateDbContext();
    var u = await db2.Users.SingleAsync(x => x.UserId == id);
    Assert.Equal(84, u.Password.Length);
    Assert.True(u.MustChangePassword);
    Assert.Equal(2, u.FailedLoginCount);
    Assert.NotNull(u.LockoutEnd);
    Assert.Equal("Password", db2.Model.FindEntityType(typeof(User))!.FindProperty(nameof(User.Password))!.GetColumnName());
  }

  [Fact]
  public async Task User_DuplicateUsername_IsRejected()
  {
    await using var app = new ClaimsApiFactory();
    await using var db = app.CreateDbContext();
    db.Users.Add(new User { Username = "dup", Password = "h" });
    db.Users.Add(new User { Username = "dup", Password = "h" });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
  }
}
