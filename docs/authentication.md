# Authentication and user accounts

Notes on how login and account management work in Claims.API.

## Background

The API will hold beneficiary personal data (medical, financial, burial, legal
assistance), so the Data Privacy Act (RA 10173) applies. Before this change every
endpoint was public. Now every request has to come from a logged-in, active user.

The system is LAN-only for now and there is no email/SMS, so there is no
"forgot password". Admins reset passwords.

The `Officers` table is the old system's data. It is not used for login.

## Database changes (already applied to sysCLAIMS)

- `[User]`: `Username` unique and not null, `Password` not null, plus
  `MustChangePassword`, `FailedLoginCount`, `LockoutEnd`, `LastLoginDate`.
- `UserRole`: `UserID` / `RoleID` not null, unique on `(UserID, RoleID)`.
- Roles: 1 Super Administrator, 2 Administrator, 3 Frontliner, 4 Social Worker.

In code, the `Password` column maps to `User.PasswordHash` so nobody mistakes it
for plaintext. Passwords are stored as ASP.NET Core Identity hashes (PBKDF2).

## Tokens

- `POST /api/auth/login` returns a JWT (HS256), valid 8 hours (`Jwt:LifetimeMinutes`).
- Signing key is `Jwt:SigningKey` (user-secrets in dev, environment variable on the
  server). The app won't start without it or if it's under 32 bytes.
- The token only carries the user ID. On every request the API reloads the user and
  their active roles from the database, so deactivating a user or removing a role
  works right away.

## Login

- Wrong username, wrong password and deactivated account all return the same 401.
- 10 wrong passwords in a row locks the account for 5 minutes.
- From the 7th wrong try the message warns: "Invalid username or password. 3 attempts left
  before the account is locked for 5 minutes." A locked account gets "Too many failed
  attempts. Try again in N minutes."
- Unknown usernames and deactivated accounts get exactly the same messages, countdown and
  lockout, so the login page never reveals which usernames exist. Unknown usernames are
  counted in memory (up to 100,000 names, reset when the API restarts).
- Lockout doesn't kick out sessions the user already has, otherwise anyone could
  log someone out by typing their username wrong 10 times.

## Temporary passwords

New accounts and admin resets set `MustChangePassword`. Until the user changes it,
they can only call `GET /api/auth/me` and `POST /api/auth/change-password`.
Everything else returns 403 with `password_change_required` so the frontend can
redirect.

Password rules: 8 to 128 characters, not the same as the username, and on change not
the same as the current one.

## Account management (`api/users`)

Administrator or Super Administrator only.

| Endpoint | What it does |
|---|---|
| `GET /api/users`, `GET /api/users/{id}` | List / view users |
| `POST /api/users` | Create (temporary password, at least one role) |
| `PUT /api/users/{id}` | Edit names |
| `PUT /api/users/{id}/roles` | Replace roles |
| `POST /api/users/{id}/reset-password` | Set a new temporary password, clears lockout |
| `POST /api/users/{id}/deactivate`, `/activate` | Turn account off / on |

Rules:
- Administrators can only manage Frontliner and Social Worker accounts. Only a Super
  Administrator can manage Administrators and Super Administrators.
- You can't deactivate yourself, change your own roles or reset your own password
  (use change-password).
- The API refuses anything that would leave no active Super Administrator.
- Nobody can give the Super Administrator role from the app, not even a Super Administrator.
  New Super Administrators are set up directly in the database. Existing ones keep the role.
- No delete. Other tables reference `UserID`, so accounts are deactivated instead.

## Other endpoints

- `NatureOfService`: reading needs login, writing needs Administrator or Super Administrator.
- `/api/health/database`: needs login.

## Not done yet

- HTTPS. Passwords and tokens currently go over plain HTTP on the LAN.
- If a user changes their own password, their old token keeps working until it
  expires (max 8 hours). Fixing that needs a `PasswordChangedDate` column.
- The deployed app should use its own SQL login with only the permissions it needs,
  not db_owner.
