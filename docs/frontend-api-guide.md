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
| GET                 | `/api/lookups/{list}`            | logged in           | Dropdown values `[{ id, code, name }]`: `sexes`, `civil-statuses`, `name-suffixes`, `relationship-statuses`, `modes-of-assistance`, `sources-of-assistance`, `provinces`, `cities?provinceId=`, `barangays?cityMunicipalityId=` |
| GET                 | `/api/persons`                   | logged in           | Search, paged: `{ items, total, page, pageSize }`. Query: `search`, `cityMunicipalityId`, `barangayId`, `sexId`, `sector` (repeatable), `page`, `pageSize` (max 100) |
| GET                 | `/api/persons/{id}`              | logged in           | One person with `family` (members with relationship, group, sex, birthdate, civil status, income, contact, occupation), `familyOf` (people who list this person as family) and the barangay's `cityMunicipalityId` / `provinceId` |
| GET                 | `/api/persons/{id}/transactions` | logged in           | Paged transaction history where the person is the patient or representative; query: `page` (10 items per page) |
| GET                 | `/api/persons/matches`           | logged in           | Persons who may be the one being typed in, up to 20: `personId`, `name`, `birthdate`, `address`, `claims`, `level` (`same` / `similar`), `reason`. Query: `lastName`, `firstName`, `middleName`, `nameSuffixId`, `birthdate`, `exceptPersonId` (the person being edited) |
| POST                | `/api/persons`                   | logged in           | Create (201 with the person). Body below                                                      |
| PUT                 | `/api/persons/{id}`              | logged in           | Replace **all** fields (send the whole person, not only what changed)                         |
| POST                | `/api/persons/{id}/family`       | logged in           | Add a family member `{ familyPersonId, relationshipStatusId, remark? }` (201). Relationship required; no self links or duplicates |
| PUT                 | `/api/persons/{id}/family/{familyCompId}` | logged in | Change the member or relationship, same body |
| DELETE              | `/api/persons/{id}/family/{familyCompId}` | logged in | Remove a family member (204), optional body `{ remark }` |
| GET                 | `/api/persons/{id}/family/history` | logged in | Every add, change and remove, newest first: `action`, `old` / `new` (`member`, `relationship`), `remark`, `changedBy`, `date` |
| GET                 | `/api/dashboard`                 | logged in           | Home page figures for one month. Query: `year`, `month` (default this month)                  |
| GET                 | `/api/dashboard/beneficiaries`   | logged in           | Who the claims were for. Query: `year`, `month`, `period` = `month` (default) or `year` (January up to the month) |
| GET                 | `/api/dashboard/budget`          | Admin / Super Admin | Released and obligated per fund source from the finance system. Query: `year`               |
| GET                 | `/api/person-review/duplicates`  | Admin / Super Admin | Possible duplicate persons, grouped, most claims first. Query: `search`, `decision` = `none` (to review, default), `same` or `different`, `page`, `pageSize` (max 100) |
| GET                 | `/api/person-review/nameless`    | Admin / Super Admin | Persons with no last or first name, most claims first. Query: `page`, `pageSize` |
| GET                 | `/api/person-review/persons/{id}/claims` | Admin / Super Admin | A person's latest claims as patient or representative, newest first: `role`, `otherPerson`, `program`, `creditorName`, `amount`, `socialWorker`. Query: `take` (1 to 50, default 10) |
| POST                | `/api/person-review/duplicates/same` | Admin / Super Admin | Confirm a whole group is one person and which record to keep, to merge later (204): `{ personIds, keepPersonId, remark? }`. Send again with another `keepPersonId` to change it |
| POST                | `/api/person-review/duplicates/not-same` | Admin / Super Admin | Mark a whole group as different people (204): `{ personIds, remark? }` |
| POST                | `/api/person-review/duplicates/reopen` | Admin / Super Admin | Undo that, same body (204) |

Role IDs: 1 Super Administrator, 2 Administrator, 3 Frontliner, 4 Social Worker.
Administrators can only manage Frontliner and Social Worker accounts. Nobody can give the Super
Administrator role from the app (403).

Swagger is always the up-to-date list. This table is a summary.

### Things to know about accounts

- Wrong password and unknown username give the same message on purpose.
- After 10 wrong passwords the account is locked for 5 minutes (`code: locked_out`). An admin can
  unlock it early with reset-password.
- The last 3 tries before that warn in the message ("Invalid username or password. 3 attempts
  left before the account is locked for 5 minutes."), and a locked account says how long to wait
  ("Too many failed attempts. Try again in 5 minutes."). The login page just shows `message`.
- Unknown usernames and deactivated accounts get the same messages and countdown, so the login
  page never reveals which usernames exist.
- A user with a temporary password can only change their password until they do. The login page
  and `RequireAuth` already send them there.
- If an admin deactivates someone or removes a role, it applies on that user's next request, even
  if they are already logged in. `apiFetch` handles the 401 / 403.
- `expiresAt` in the login response is server local time (Philippine time).

### Things to know about persons

Body for POST / PUT: `firstName`, `lastName` (required), `middleName`, `nameSuffixId`, `sexId`,
`civilStatusId`, `birthdate` (`"YYYY-MM-DD"`), `contactNo`, `skillOccupation`,
`estimatedMonthlyIncome`, `barangayId`, `purokSubdivision`, `street`, `noBuildingName`,
`isSeniorCitizen`, `isSoloParent`, `isPwd`, `isRr`, `is4P`, and `confirmDuplicate`,
`duplicateReason` (see below).

- **Search:** digits (with spaces, `+ - ( )`) match the person ID or part of the contact number;
  anything else matches first / middle / last name, every word must match. Filters combine with
  the text and with each other. `sector` is `senior`, `pwd`, `4ps` or `solo-parent`; a person
  must have all the sectors given.
- **Names** are saved in capitals. They may only contain letters (ñ and accents included), spaces,
  `. ' -`. Old records with other characters (commas, slashes) can still be saved as long as the
  name isn't changed.
- **Contact no.:** digits, spaces, `+ - ( )`, with 7 to 15 digits. **Birthdate:** 1900 to today.
  **Income:** 0 to 99,999,999.99, 2 decimals.
- **Text vs IDs:** old records only have the sex, barangay and municipality as text. The API
  returns that text as `sex`, `barangay` and `municipality`, and fills it from the IDs when a
  person is saved, so always show those fields and send the IDs. Filter and report by the IDs.
- Validation errors come back as 400 with a `message` to show, like everything else.
- **Same person check.** Saving a new person, or changing a person's name or birthdate, returns
  **409** when an active person has the same name: `{ message, matches }`, `matches` shaped like
  `/api/persons/matches`. Show the matches with a link to open each. To save anyway, send the same
  body again with `confirmDuplicate: true` and a `duplicateReason` (required, up to 500
  characters); it goes to the AuditLog (`Entity` = `Person`, `Action` = `SavedAsDuplicate`).
  Changing other fields of a person who already has a duplicate is not blocked.
- **How names are matched:** capitals, spaces, dots, hyphens, apostrophes and accents are ignored
  (DELA CRUZ = DE LA CRUZ, CAÑETE = CANETE, MA. NILDA = MA NILDA). A JR, SR, II, III or IV typed
  into the first or last name counts as the suffix. **Same:** same last, first and middle name and
  suffix. **Similar** (shown, never blocks): only one has a middle name, the middle name is only an
  initial, first and last name swapped, or only one has a suffix. Two different birthdates or
  suffixes (JR vs SR) are never a match; a missing birthdate matches any. Almost no old records have
  a birthdate, so in practice this is a name check.
- Double spaces inside names are collapsed when saving.

### Things to know about the dashboard

- Claims from the old system came over with status 0 but aren't deleted, so they're counted. Only
  claims made in this app can be deleted (status 0 with a `createdBy`).
- **Waiting** means made in this app, not deleted and not released. Old claims have no release
  status, so they never show as waiting. It isn't limited to the chosen month.
- **Beneficiaries** are patients, each counted once. `firstTime` had no counted claim before the
  period, `returning` did. Sectors can overlap, so they don't add up to the total. Age is at the
  end of the month.
- **Budget** is read live from the finance system (sysPFM) through `getAnnualBalance`. Only fund
  sources linked to a finance appropriation for that year show up. If the finance server can't be
  reached it returns 503 with a `message`; show that in the card and keep the rest of the page.
  Claims don't record their fund source yet, so the budget can't be matched to claims.

### Things to know about person review

- Nothing on persons or claims is changed or merged yet. The only thing saved is the decision
  (`SamePerson`, `NotSamePerson` or `Reopened`) in AuditLog (`Entity` = `DuplicateGroup`), with who,
  when and the remark. A decision can be changed straight from same to different and back.
- `review.keepPersonId` on a "same" group is the record the others will be merged into.
- **Possible duplicates** are active persons matched as **same** by the rule under persons above
  (spelling aside; a missing middle name on both counts as the same, and two different birthdates
  split a group). Each person shows
  their claims as patient, claims as representative, last claim date and family links, so staff can
  tell which record to keep.
- A marked group leaves the to-review list until someone new with the same name is added, then it shows again with everyone. `personIds` must be the whole current group.
- Claims from the old system count even though their status is 0; claims deleted in this app don't.

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
