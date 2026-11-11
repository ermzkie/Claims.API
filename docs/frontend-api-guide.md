# Calling Claims.API from the frontend

How the Razor Pages app should log users in and call the API.

## The short version

1. The login page sends the username and password to `POST /api/auth/login`.
2. The API returns a **token** (a JWT). It is valid for 8 hours.
3. The frontend keeps the token **on the server side** (inside its own login cookie) and sends it
   with every API call as a header:
   ```
   Authorization: Bearer <token>
   ```
4. That's it. **Every** API endpoint needs this header, except login. New controllers are
   protected automatically, nothing extra to do.

The browser never talks to the API directly and never sees the token. The Razor Pages server
calls the API from its page handlers (`OnGet`, `OnPost`).

## Try it in the browser

While the API runs on your machine in Development mode, open **http://localhost:5224/swagger**.
It lists every endpoint with its fields.

1. Open `POST /api/auth/login`, click **Try it out**, enter a username and password, **Execute**.
2. Copy the `token` value from the response (without the quotes).
3. Click **Authorize** at the top, paste the token, **Authorize**, **Close**.
4. Every other endpoint now works with **Try it out**.

The page only exists in Development. It is not available on the server.

## Endpoints

| Method | URL | Who | What |
|---|---|---|---|
| POST | `/api/auth/login` | anyone | Log in. Body: `{ "username", "password" }` |
| GET | `/api/auth/me` | logged in | Current user (id, username, names, roles, mustChangePassword) |
| POST | `/api/auth/change-password` | logged in | Body: `{ "currentPassword", "newPassword" }` |
| GET | `/api/users` | Admin / Super Admin | List accounts |
| GET | `/api/users/{id}` | Admin / Super Admin | One account |
| POST | `/api/users` | Admin / Super Admin | Create: `username, firstname, middlename, lastname, nameSuffixId, temporaryPassword, roleIds` |
| PUT | `/api/users/{id}` | Admin / Super Admin | Edit names |
| PUT | `/api/users/{id}/roles` | Admin / Super Admin | Replace roles: `{ "roleIds": [3, 4] }` |
| POST | `/api/users/{id}/reset-password` | Admin / Super Admin | `{ "temporaryPassword" }` |
| POST | `/api/users/{id}/deactivate` | Admin / Super Admin | Turn account off |
| POST | `/api/users/{id}/activate` | Admin / Super Admin | Turn account on |

Role IDs: 1 Super Administrator, 2 Administrator, 3 Frontliner, 4 Social Worker.
Administrators can only manage Frontliner and Social Worker accounts. Nobody can give the Super
Administrator role from the app (403).

### Login response

```json
{
  "token": "eyJhbGciOi...",
  "expiresAt": "2026-11-11T16:30:00",
  "mustChangePassword": false,
  "user": {
    "userId": 1,
    "username": "superadmin",
    "firstname": "...",
    "middlename": null,
    "lastname": "...",
    "mustChangePassword": false,
    "roles": ["Super Administrator"]
  }
}
```

`expiresAt` is server local time (Philippine time).

### Errors

Errors always come back as JSON with a `message` you can show to the user. Some also have a `code`
for the frontend to act on.

```json
{ "message": "Invalid username or password." }
{ "message": "Too many failed attempts. Try again later.", "code": "locked_out" }
{ "message": "You must change your password before continuing.", "code": "password_change_required" }
```

## What to do with each response

| Status | Meaning | Frontend should |
|---|---|---|
| 200 / 201 / 204 | OK | Continue. 204 has no body. |
| 400 | Bad input | Show `message` next to the form |
| 401 | Not logged in, token expired, or account deactivated | **Sign the user out** and go to the login page |
| 403 + `code: password_change_required` | User has a temporary password | **Go to the change password page** |
| 403 (no code) | Logged in but not allowed (wrong role) | Show "You don't have access to this" |
| 404 | Not found | Show `message` |
| 409 | Conflict (e.g. username taken) | Show `message` |

Notes:
- **Wrong password** and **unknown username** give the same 401 message on purpose. Don't try to
  tell them apart.
- After **5 wrong passwords** the account is locked for 15 minutes (`code: locked_out`).
  An admin can unlock it early with reset-password.
- A user with `mustChangePassword: true` can only call `/api/auth/me` and
  `/api/auth/change-password` until they change it. Send them straight to the change password page
  after login.
- If an admin deactivates someone or removes a role, it takes effect on the user's **next request**,
  even if they are already logged in. Expect a 401 or 403 at any time and handle it as above.

## Razor Pages setup

### appsettings.json

```json
"ClaimsApi": { "BaseUrl": "http://localhost:5224/" }
```

### Program.cs

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");                 // every page needs login...
    options.Conventions.AllowAnonymousToPage("/Account/Login"); // ...except the login page
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.SlidingExpiration = false; // the API token does not slide, so the cookie shouldn't either
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<ApiTokenHandler>();
builder.Services
    .AddHttpClient("ClaimsApi", c => c.BaseAddress = new Uri(builder.Configuration["ClaimsApi:BaseUrl"]!))
    .AddHttpMessageHandler<ApiTokenHandler>();

// ...

app.UseAuthentication();
app.UseAuthorization();
```

### ApiTokenHandler.cs: adds the token to every API call

Write this once. After that, every page just uses the `ClaimsApi` HttpClient and the header is
added for you.

```csharp
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

public class ApiTokenHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _accessor;

    public ApiTokenHandler(IHttpContextAccessor accessor) => _accessor = accessor;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = _accessor.HttpContext;
        var token = context is null ? null : await context.GetTokenAsync("access_token");

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}
```

### Login page (Pages/Account/Login.cshtml.cs)

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class LoginModel : PageModel
{
    private readonly IHttpClientFactory _httpFactory;
    public LoginModel(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    [BindProperty] public string Username { get; set; } = "";
    [BindProperty] public string Password { get; set; } = "";

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var api = _httpFactory.CreateClient("ClaimsApi");
        var response = await api.PostAsJsonAsync("api/auth/login", new { username = Username, password = Password });

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            ModelState.AddModelError("", error?.Message ?? "Login failed.");
            return Page();
        }

        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        // Keep the user's id, name and roles in the frontend's own cookie,
        // so pages can use User.IsInRole("Administrator") to show or hide menus.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, login.User.UserId.ToString()),
            new(ClaimTypes.Name, login.User.Username),
        };
        claims.AddRange(login.User.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var properties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTime.SpecifyKind(login.ExpiresAt, DateTimeKind.Local).ToUniversalTime(),
        };
        // The API token is stored inside the encrypted cookie, never in the browser's JavaScript.
        properties.StoreTokens(new[] { new AuthenticationToken { Name = "access_token", Value = login.Token } });

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), properties);

        if (login.MustChangePassword)
            return RedirectToPage("/Account/ChangePassword");

        return LocalRedirect(returnUrl ?? "/");
    }
}

public record ApiError(string Message, string? Code);
public record LoginResponse(string Token, DateTime ExpiresAt, bool MustChangePassword, CurrentUser User);
public record CurrentUser(int UserId, string Username, string? Firstname, string? Middlename,
    string? Lastname, bool MustChangePassword, List<string> Roles);
```

### Calling any other endpoint

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class UsersModel : PageModel
{
    private readonly IHttpClientFactory _httpFactory;
    public UsersModel(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public List<UserSummary> Users { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var api = _httpFactory.CreateClient("ClaimsApi"); // token is added automatically
        var response = await api.GetAsync("api/users");

        var problem = await ApiResponse.HandleAsync(this, response);
        if (problem is not null) return problem;

        Users = (await response.Content.ReadFromJsonAsync<List<UserSummary>>())!;
        return Page();
    }
}

public record UserSummary(int UserId, string Username, string? Firstname, string? Middlename,
    string? Lastname, short? NameSuffixId, List<string> Roles, bool Status,
    bool MustChangePassword, DateTime? LockedUntil, DateTime? LastLoginDate);
```

### One helper for 401 / 403 (use it after every API call)

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public static class ApiResponse
{
    // Returns a redirect when the user must be sent somewhere, or null to carry on.
    public static async Task<IActionResult?> HandleAsync(PageModel page, HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return null;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await page.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return page.RedirectToPage("/Account/Login");
        }

        // Some responses (e.g. 403 for a wrong role) have no body, so don't assume JSON.
        ApiError? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ApiError>(); } catch { }

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return error?.Code == "password_change_required"
                ? page.RedirectToPage("/Account/ChangePassword")
                : page.RedirectToPage("/Account/AccessDenied");
        }

        // 400 / 404 / 409: show the message on the page.
        page.ModelState.AddModelError("", error?.Message ?? "Something went wrong.");
        return page.Page();
    }
}
```

### Logout

The API has no logout endpoint. Signing out of the frontend cookie throws the token away:

```csharp
public async Task<IActionResult> OnPostAsync()
{
    await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return RedirectToPage("/Account/Login");
}
```

## Rules

- **Never** put the token in JavaScript, `localStorage`, a hidden field or the URL. It stays in the
  server-side cookie only.
- **Never** log the token or passwords.
- Showing or hiding menus by role (`User.IsInRole(...)`) is only for convenience. The API checks the
  role again on every request, so hiding a button is not the security.
- Use the IDs from the API (`userId`), not usernames, when calling other endpoints.
- For testing without the frontend, use `Claims.API.http` in the API project.
