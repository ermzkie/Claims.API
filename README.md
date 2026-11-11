# Claims.API

Backend for the Claims system (sysCLAIMS). ASP.NET Core on .NET 10, SQL Server, JWT login.
The frontend is [Claims.Web](https://github.com/tsarls88/Claims.Web).

## Run it on your machine

### 1. Install

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Access to the sysCLAIMS database server (office network)

### 2. Set your secrets

The API needs two settings that are **never** committed: the database connection string and a
signing key for login tokens. Put them in .NET user-secrets (stored in your Windows profile, not in
the repo). In the project folder:

```powershell
cd Claims.API

# Database: ask the team lead for this value. Don't paste it in chat groups or commit it.
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=sysCLAIMS;User Id=...;Password=...;Encrypt=True;TrustServerCertificate=True"

# Signing key: make your own, it only has to stay the same on your machine
$b = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
dotnet user-secrets set "Jwt:SigningKey" ([Convert]::ToBase64String($b))
```

Check with `dotnet user-secrets list`. The API won't start without both:

| Error at startup | Fix |
|---|---|
| `Connection string 'DefaultConnection' was not found` | Set `ConnectionStrings:DefaultConnection` |
| `Jwt:SigningKey must be configured and at least 32 bytes long` | Set `Jwt:SigningKey` (the command above makes a long enough one) |

### 3. Run

```powershell
dotnet run --launch-profile http
```

The API listens on **http://localhost:5224**. Opening that address shows a blank page; that's
normal. Go to:

**http://localhost:5224/swagger**: every endpoint, with **Try it out**.

1. `POST /api/auth/login` → **Try it out** → your username and password → **Execute**
2. Copy the `token` from the response (without quotes)
3. **Authorize** (top right) → paste → **Authorize** → **Close**
4. Every other endpoint now works

Ask an administrator for an account. New accounts have a temporary password: change it first
(`POST /api/auth/change-password`, or log in through the frontend, which sends you there).

Swagger only exists in Development, not on the server.

### 4. Run the frontend against it

In Claims.Web, `npm start` and open http://localhost:3000. It forwards `/api` to port 5224, so
just keep this API running. See [docs/frontend-api-guide.md](docs/frontend-api-guide.md).

## Tests

```powershell
dotnet test tests/Claims.API.Tests
```

The tests use an in-memory SQLite database. They don't need secrets or the real database, and
they don't touch sysCLAIMS.

If the build says `Claims.API.exe` is locked, the API is still running (Visual Studio, or a
terminal). Stop it first.

## Docs

| File | For |
|---|---|
| [docs/backend-guide.md](docs/backend-guide.md) | Writing controllers: protecting endpoints, getting the logged-in user (`ICurrentUser`) |
| [docs/authentication.md](docs/authentication.md) | How login, lockout, temporary passwords and user management work |
| [docs/frontend-api-guide.md](docs/frontend-api-guide.md) | Calling the API from Claims.Web |
| `Claims.API.http` | Manual requests with the VS Code REST Client extension |

## Working on it

- Branch from an up-to-date `main`, one branch per change, open a PR into `main`.
- New branches must track their own name, not `main`:
  ```powershell
  git fetch
  git switch -c my-change --no-track origin/main
  git push -u origin my-change
  ```
  A branch that tracks `main` gets pushed straight to `main` by VS Code's **Sync Changes**.
- Never commit secrets. `appsettings.json` only holds non-secret settings.

## On the server (IIS)

`Program.cs` also reads `secrets.json` from a fixed user-secrets path on the server, then
environment variables, which win over everything else. Environment variable names use `__` instead
of `:`, for example `ConnectionStrings__DefaultConnection` and `Jwt__SigningKey`. The server's
signing key must stay the same between deployments, or everyone gets logged out.
