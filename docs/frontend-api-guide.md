# Calling Claims.API from the frontend

How the React app (Claims.Web) talks to the API.

## The short version

Every API call goes through one function, `apiFetch`, in `src/api/client.js`. You give it the path,
it gives you back the JSON.

```js
import { apiFetch } from "src/api/client";

const users = await apiFetch("/api/users");
```

You never write the server address, never touch the token and never check for 401 yourself.
`apiFetch` does that.

## Running it

Two terminals:

```powershell
# Claims.API
dotnet run --launch-profile http      # http://localhost:5224

# Claims.Web
npm start                             # http://localhost:3000
```

Open http://localhost:3000. Vite forwards every `/api/...` request to the API on port 5224
(`vite.config.mjs`), so the browser only ever talks to port 3000.

## What endpoints exist

Open **http://localhost:5224/swagger** while the API is running. It lists every endpoint with its
fields and lets you try them:

1. `POST /api/auth/login` → **Try it out** → enter a username and password → **Execute**.
2. Copy the `token` from the response (without the quotes).
3. **Authorize** at the top → paste the token → **Authorize** → **Close**.
4. Every other endpoint now works with **Try it out**.

Swagger only exists in Development, not on the server.

## Calling the API

```js
// GET
const users = await apiFetch("/api/users");
const user = await apiFetch(`/api/users/${id}`);

// POST / PUT: pass a plain object as body, it is sent as JSON
await apiFetch("/api/users", {
  method: "POST",
  body: {
    username: "maria",
    firstname: "Maria",
    lastname: "Cruz",
    temporaryPassword: "Temp1234",
    roleIds: [4],
  },
});

await apiFetch(`/api/users/${id}/roles`, {
  method: "PUT",
  body: { roleIds: [3, 4] },
});

// No body
await apiFetch(`/api/users/${id}/deactivate`, { method: "POST" });
```

It returns the parsed JSON, or `null` when the API answers 204 No Content.

### Keep the paths in one file per feature

Don't scatter `apiFetch('/api/...')` across pages. Put the calls for a feature in its own file
under `src/api/`, and have pages call those functions:

```js
// src/api/users.js
import { apiFetch } from "./client";

export const getUsers = () => apiFetch("/api/users");
export const getUser = (id) => apiFetch(`/api/users/${id}`);
export const createUser = (user) =>
  apiFetch("/api/users", { method: "POST", body: user });
export const updateUserRoles = (id, roleIds) =>
  apiFetch(`/api/users/${id}/roles`, { method: "PUT", body: { roleIds } });
export const deactivateUser = (id) =>
  apiFetch(`/api/users/${id}/deactivate`, { method: "POST" });
export const activateUser = (id) =>
  apiFetch(`/api/users/${id}/activate`, { method: "POST" });
```

If an endpoint changes, only this file changes.

### In a page

Loading a list:

```jsx
import React, { useEffect, useState } from "react";
import { CAlert, CSpinner, CTable } from "@coreui/react";
import { getUsers } from "src/api/users";

const UserManagement = () => {
  const [users, setUsers] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    getUsers()
      .then(setUsers)
      .catch((err) => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  if (loading) return <CSpinner />;
  if (error) return <CAlert color="danger">{error}</CAlert>;

  return <CTable>{/* users.map(...) */}</CTable>;
};
```

Saving a form:

```jsx
const handleSubmit = async (event) => {
  event.preventDefault();
  setError("");
  setSaving(true);
  try {
    await createUser(form);
    navigate("/user-management");
  } catch (err) {
    setError(err.message); // e.g. "Username already exists."
  } finally {
    setSaving(false);
  }
};
```

## Errors

When a call fails, `apiFetch` throws an `ApiError`:

| Property      | Example                      | Use it for                                                     |
| ------------- | ---------------------------- | -------------------------------------------------------------- |
| `err.message` | `"Username already exists."` | Show it to the user as is. Always set.                         |
| `err.status`  | `409`                        | Rarely needed. `0` means the server is unreachable.            |
| `err.code`    | `"locked_out"`               | Only when you need to react to a specific case                 |
| `err.traceId` | `"00-4bf9...-01"`            | Set on server errors (500). Show it so the user can report it. |

What `apiFetch` already does for you:

| Response                                  | What happens                                     |
| ----------------------------------------- | ------------------------------------------------ |
| 401 (not logged in, expired, deactivated) | Session cleared, user sent to the login page     |
| 403 `password_change_required`            | User sent to the change password page            |
| 403 (wrong role)                          | Throws with "You do not have access to this."    |
| 400 validation errors from ASP.NET        | Throws with the first field error as the message |
| Server unreachable                        | Throws with "Cannot reach the server..."         |

For server errors, show the reference so the user can pass it on:

```jsx
catch (err) {
  setError(err.traceId ? `${err.message} Reference: ${err.traceId}` : err.message)
}
```

## The logged-in user

```js
import { getUser, hasRole, logout } from "src/api/client";

const user = getUser(); // { userId, username, firstname, middlename, lastname, roles, mustChangePassword }
hasRole("Administrator"); // true / false
logout(); // clears the session and goes to the login page
```

Use `hasRole` to show or hide menus and buttons. It is only for looks: the API checks the role
again on every request, so hiding a button is not what keeps users out.

Every page inside the main layout already requires login (`src/components/RequireAuth.jsx`). New
pages added to `routes.js` are protected automatically.

## Endpoints

| Method              | URL                              | Who                 | What                                                                                          |
| ------------------- | -------------------------------- | ------------------- | --------------------------------------------------------------------------------------------- |
| POST                | `/api/auth/login`                | anyone              | Log in. Body: `{ username, password }`. Use `login()` from `client.js`, not `apiFetch`.       |
| GET                 | `/api/auth/me`                   | logged in           | Current user (id, username, names, roles, mustChangePassword)                                 |
| POST                | `/api/auth/change-password`      | logged in           | Body: `{ currentPassword, newPassword }`. Use `changePassword()` from `client.js`.            |
| GET                 | `/api/users`                     | Admin / Super Admin | List accounts                                                                                 |
| GET                 | `/api/users/{id}`                | Admin / Super Admin | One account                                                                                   |
| POST                | `/api/users`                     | Admin / Super Admin | Create: `username, firstname, middlename, lastname, nameSuffixId, temporaryPassword, roleIds` |
| PUT                 | `/api/users/{id}`                | Admin / Super Admin | Edit names                                                                                    |
| PUT                 | `/api/users/{id}/roles`          | Admin / Super Admin | Replace roles: `{ roleIds: [3, 4] }`                                                          |
| POST                | `/api/users/{id}/reset-password` | Admin / Super Admin | `{ temporaryPassword }`                                                                       |
| POST                | `/api/users/{id}/deactivate`     | Admin / Super Admin | Turn account off                                                                              |
| POST                | `/api/users/{id}/activate`       | Admin / Super Admin | Turn account on                                                                               |
| GET                 | `/api/natureofservice`           | logged in           | List natures of service                                                                       |
| POST / PUT / DELETE | `/api/natureofservice[/{id}]`    | Admin / Super Admin | `{ natureDesc }`                                                                              |

Role IDs: 1 Super Administrator, 2 Administrator, 3 Frontliner, 4 Social Worker.
Administrators can only manage Frontliner and Social Worker accounts. Nobody can give the Super
Administrator role from the app (403).

Swagger is always the up-to-date list. This table is a summary.

### Things to know about accounts

- Wrong password and unknown username give the same message on purpose.
- After 5 wrong passwords the account is locked for 15 minutes (`code: locked_out`). An admin can
  unlock it early with reset-password.
- A user with a temporary password can only change their password until they do. The login page
  and `RequireAuth` already send them there.
- If an admin deactivates someone or removes a role, it applies on that user's next request, even
  if they are already logged in. `apiFetch` handles the 401 / 403.
- `expiresAt` in the login response is server local time (Philippine time).

## Rules

- The token stays in `sessionStorage`, handled by `client.js`. Don't copy it into `localStorage`,
  cookies, the URL or Redux, and never log it or a password.
- To stay logged in while developing, put `VITE_KEEP_SESSION=true` in your own
  `.env.development.local` (see the README, "Stay logged in while developing"). It only works on
  `npm start`; a production build always uses `sessionStorage`.
- Don't use `dangerouslySetInnerHTML` with data from the API. A script that gets onto the page can
  read the token.
- Use IDs from the API (`userId`), not usernames, when calling other endpoints.
- Calls made outside the frontend (Postman, `Claims.API.http`) need the header
  `Authorization: Bearer <token>`. In the React app, `apiFetch` adds it.

## On the server

In production the frontend is built (`npm run build`) and served as static files. If the API is
on the same address under `/api`, nothing changes. If it is on a different address, set
`VITE_API_URL` in `.env` before building, and the API will need CORS enabled for the frontend's
address.
