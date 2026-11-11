# Writing controllers in Claims.API

How to protect endpoints and how to get the logged-in user.

## Login is already required

Every endpoint needs a valid token, automatically. You don't add `[Authorize]` to a new
controller for that. The only endpoint open without a token is `POST /api/auth/login`.

To limit an endpoint (or a whole controller) to certain roles:

```csharp
[Authorize(Roles = RoleNames.SocialWorker)]                                // one role
[Authorize(Roles = RoleNames.Administrator + "," + RoleNames.Frontliner)]  // either role
[Authorize(Policy = AuthPolicies.UserAdmin)]                               // Super Administrator or Administrator
```

Role names are in `Auth/RoleNames.cs`. Use the constants, not typed strings.

## Getting the logged-in user

Use `ICurrentUser`. Two steps.

**1. Ask for it in the constructor**

```csharp
using Claims.API.Auth;

[Route("api/[controller]")]
[ApiController]
public class TransactionsController(ApplicationDbContext context, ICurrentUser currentUser) : ControllerBase
{
  ...
}
```

The services go in the class line (`(ApplicationDbContext context, ICurrentUser currentUser)`)
and you use them directly in the methods below. No fields, no constructor body.
`AuthController` and `UsersController` are written this way.

The longer style works the same:

```csharp
private readonly ICurrentUser _currentUser;

public TransactionsController(ApplicationDbContext context, ICurrentUser currentUser)
{
  _context = context;
  _currentUser = currentUser;
}
```

**2. Use it**

```csharp
[HttpPost]
public async Task<IActionResult> Create(CreateTransactionDto dto)
{
  var transaction = new Transaction
  {
    // ... fields from dto
    CreatedBy = currentUser.Id,
  };

  if (currentUser.IsInRole(RoleNames.SocialWorker))
    transaction.SocialWorkerUserId = currentUser.Id;

  context.Transactions.Add(transaction);
  await context.SaveChangesAsync();
  return Ok();
}
```

### What it gives you

| Code | Value | Database call |
|---|---|---|
| `currentUser.Id` | the user's `UserID`, e.g. `4` | no |
| `currentUser.Username` | e.g. `"maria"` | no |
| `currentUser.Roles` | e.g. `["Social Worker"]` | no |
| `currentUser.IsInRole(RoleNames.Administrator)` | `true` / `false` | no |
| `await currentUser.GetProfileAsync()` | first, middle, last name, roles (same as `GET /api/auth/me`) | once per request |

`Id`, `Username` and `Roles` are free to use as often as you like. The API already checks the
user against the database at the start of every request (still active, current roles), so they
are always up to date. `GetProfileAsync` reads the database the first time and reuses the
result for the rest of the request.

### When to use it

When the table records **who** did something. In sysCLAIMS these have a `CreatedBy` column:

- `Transactions`
- `Person`
- `FamilyComposition`
- `User`
- `UserRole`

Set `CreatedBy = currentUser.Id` when you insert a row. For lookup tables like
`NatureOfService` you don't need it.

### Services

It works the same in a service class. Add it to the constructor and use it there, so the
controller doesn't have to pass the user id along:

```csharp
public class TransactionService(ApplicationDbContext db, ICurrentUser currentUser)
{
  public async Task CreateAsync(CreateTransactionDto dto)
  {
    var transaction = new Transaction { CreatedBy = currentUser.Id };
    ...
  }
}
```

Register the service in `Program.cs` as scoped: `builder.Services.AddScoped<TransactionService>();`

### Things to know

- **Roles on the attribute, not in code.** Use `[Authorize(Roles = ...)]` to decide who may
  call an endpoint. Use `IsInRole` only for logic inside an endpoint that everyone allowed in
  can call (like the social worker example above).
- **Not on the login endpoint.** Nobody is logged in yet there, so `currentUser.Id` throws.
  Every other endpoint has a logged-in user.
- **Don't read the user id from the request body.** Always use `currentUser.Id`. A value sent
  by the frontend could be changed by anyone.

## Trying it

Run the API, open **http://localhost:5224/swagger**, log in, click **Authorize**, paste the
token, then call `GET /api/auth/me`. It returns exactly what `currentUser` holds for that user.
