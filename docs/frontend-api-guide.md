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
| GET                 | `/api/natureofservice`           | logged in           | Active natures `{ natureId, natureDesc, status }`. Admins: `?includeInactive=true` for turned-off ones |
| POST / PUT / DELETE | `/api/natureofservice[/{id}]`    | Admin / Super Admin | `{ natureDesc, status? }`. PUT without `status` keeps it. Same name = 409. DELETE of a nature claims use = 409 (turn it off instead) |
| GET                 | `/api/lookups/{list}`            | logged in           | Dropdown values `[{ id, code, name }]`: `sexes`, `civil-statuses`, `name-suffixes`, `relationship-statuses`, `modes-of-assistance`, `sources-of-assistance`, `provinces`, `cities?provinceId=`, `barangays?cityMunicipalityId=` |
| GET                 | `/api/persons`                   | logged in           | Search, paged: `{ items, total, page, pageSize }`. Query: `search`, `cityMunicipalityId`, `barangayId`, `sexId`, `sector` (repeatable), `page`, `pageSize` (max 100) |
| GET                 | `/api/persons/{id}`              | logged in           | One person with `family`, `familyOf`, `cityMunicipalityId`, `provinceId` |
| GET                 | `/api/persons/{id}/transactions` | logged in           | Claims as patient or representative, 10 per page. Query: `page` |
| GET                 | `/api/persons/matches`           | logged in           | Possible matches (up to 20), `level` `same` / `similar`. Query: `lastName`, `firstName`, `middleName`, `nameSuffixId`, `birthdate`, `exceptPersonId` |
| POST                | `/api/persons`                   | logged in           | Create (201 with the person). Body below                                                      |
| PUT                 | `/api/persons/{id}`              | logged in           | Replace **all** fields (send the whole person, not only what changed). Send back `version` from the GET: 409 with `stale: true` if someone saved in between |
| GET                 | `/api/persons/{id}/history`      | logged in           | Edits, newest first: only the changed fields in `oldValues` / `newValues` (JSON) |
| POST                | `/api/persons/{id}/family`       | logged in           | Add a family member `{ familyPersonId, relationshipStatusId, remark? }` (201). Relationship required; no self links or duplicates |
| PUT                 | `/api/persons/{id}/family/{familyCompId}` | logged in | Change the member or relationship, same body |
| DELETE              | `/api/persons/{id}/family/{familyCompId}` | logged in | Remove a family member (204), optional body `{ remark }` |
| GET                 | `/api/persons/{id}/family/history` | logged in | Family change log, newest first |
| GET                 | `/api/transactions`              | logged in           | Claims, paged: `{ items, total, page, pageSize, totalPages }`. Query: `search`, `startDate`, `endDate`, `status` (`active` default, `cancelled`, `all`), `socialWorkerId`, `assistanceCode`, `sortBy`, `sortDirection`, `page`, `pageSize` (max 100) |
| GET                 | `/api/transactions/{id}`         | logged in           | One claim |
| GET                 | `/api/transactions/{id}/history` | logged in           | Change log, oldest first |
| POST                | `/api/transactions/save`         | logged in           | Create (201) or edit (204, with `transactionId`). On edit, fields left out keep their value; blank text clears it. Money side is Social Worker / admins |
| POST                | `/api/transactions/{id}/checkout-social-worker` | Social Worker / admins | Take the claim. Someone else has it: 409 with their name; send `{ takeOver: true, reason }` to take it over |
| GET                 | `/api/queue/today`               | logged in           | Today's queue: `{ day, serverTime, nextQueueCode, lanes: [{ laneTypeId, code, name, waiting: [], serving: [], done }], notServed: [], waiting, serving, done }`. Lanes come in serving order |
| POST                | `/api/queue/next`                | Social Worker / admins | Take the next client: 200 with the claim, 204 when nobody waits, 409 `{ message, transactionId }` while you still serve one |
| POST                | `/api/queue/{id}/return`         | holder / admins     | Back to the queue, behind those waiting now. Body `{ reason? }` (204) |
| POST                | `/api/queue/{id}/not-served`     | holder / admins     | Out of today's queue. Body `{ reason }` required (204). A checkout brings it back |
| GET                 | `/api/lookups/fund-sources?year=` | logged in           | Active fund sources for that year `[{ id, name, year }]` |
| POST                | `/api/transactions/{id}/cancel`  | Admin / Super Admin | Cancel (204): `{ reason }` required. `DELETE /api/transactions/{id}?reason=` does the same |
| POST                | `/api/transactions/{id}/restore` | Admin / Super Admin | Undo a cancel (204), optional `{ reason }` |
| GET                 | `/api/dashboard`                 | logged in           | Home page figures for one month. Query: `year`, `month` (default this month)                  |
| GET                 | `/api/dashboard/beneficiaries`   | logged in           | Who the claims were for. Query: `year`, `month`, `period` = `month` (default) or `year` (January up to the month) |
| GET                 | `/api/dashboard/budget`          | Admin / Super Admin | Released and obligated per fund source from the finance system. Query: `year`               |
| GET                 | `/api/fund-sources`              | Admin / Super Admin | Fund sources for a year: `{ year, years, financeAvailable, items }`; each item `fundSourceId`, `year`, `sourceId`, `program`, `kind` (`appropriation` / `trustFund`), `financeId`, `finance` (`id`, `code`, `name`, `amount`), `released`, `obligated`, `pending`, `original`, `supplemental`, `adjustments`, `claims`, `claimsAmount`, `addedOn`, `addedBy`, `changedOn`, `changedBy`. Query: `year` |
| GET                 | `/api/fund-sources/{id}/claims`  | Admin / Super Admin | Latest claims charged to it: `transactionId`, `refNo`, `date`, `patientPersonId`, `patient`, `assistanceCode`, `program`, `creditorId`, `creditorName`, `amount`, `isReleased`. Query: `take` (1 to 100, default 20) |
| GET                 | `/api/fund-sources/finance-accounts` | Admin / Super Admin | Accounts that paid aid: `id`, `code`, `name`, `amount`, `aidObligations`, `creditorPayments`, `paysAid`. Query: `kind` (`appropriation` needs `year`, or `trustFund`) |
| POST / PUT          | `/api/fund-sources`, `/api/fund-sources/{id}` | Admin / Super Admin | `{ year, sourceId, appropriationId }` or `{ year, sourceId, trustFundId }` |
| DELETE              | `/api/fund-sources/{id}`         | Admin / Super Admin | Remove (204), only when no claim uses it |
| GET                 | `/api/person-review/duplicates`  | Admin / Super Admin | Possible duplicate persons, grouped, most claims first. Query: `search`, `decision` = `none` (to review, default), `same` or `different`, `page`, `pageSize` (max 100) |
| GET                 | `/api/person-review/nameless`    | Admin / Super Admin | Persons with no last or first name, most claims first. Query: `page`, `pageSize` |
| GET                 | `/api/person-review/persons/{id}/claims` | Admin / Super Admin | A person's latest claims. Query: `take` (1 to 50) |
| POST                | `/api/person-review/duplicates/same` | Admin / Super Admin | Same person (204): `{ personIds, keepPersonId, remark? }` |
| POST                | `/api/person-review/duplicates/not-same` | Admin / Super Admin | Mark a whole group as different people (204): `{ personIds, remark? }` |
| POST                | `/api/person-review/duplicates/reopen` | Admin / Super Admin | Undo that, same body (204) |

Role IDs: 1 Super Administrator, 2 Administrator, 3 Frontliner, 4 Social Worker.
Administrators can only manage Frontliner and Social Worker accounts. Nobody can give the Super
Administrator role from the app (403).

Swagger is always the up-to-date list. This table is a summary.

### Things to know about accounts

- Wrong password and unknown username give the same message on purpose.
- 10 wrong passwords lock the account for 5 minutes (`code: locked_out`). Reset-password unlocks it.
  The message counts down the last 3 tries; just show `message`.
- Temporary password: the user can only change it until they do.
- Deactivating or removing a role applies on the user's next request.
- `expiresAt` is Philippine time.

### Things to know about persons

Body for POST / PUT: `firstName`, `lastName` (required), `middleName`, `nameSuffixId`, `sexId`,
`civilStatusId`, `birthdate` (`"YYYY-MM-DD"`), `contactNo`, `skillOccupation`,
`estimatedMonthlyIncome`, `barangayId`, `purokSubdivision`, `street`, `noBuildingName`,
`isSeniorCitizen`, `isSoloParent`, `isPwd`, `isRr`, `is4P`, `confirmDuplicate`, `duplicateReason`.

- Search: digits match the ID or contact number, words match the names (every word).
- Names are saved in capitals: letters, spaces and `. ' -` only. Contact no. 7 to 15 digits.
  Birthdate 1900 to today.
- Old records only have sex, barangay and municipality as text. Show the text, send the IDs, filter by IDs.
- Same person: create or rename returns 409 `{ message, matches }` when someone has the same name.
  Save anyway with `confirmDuplicate: true` and a `duplicateReason` (logged).
- Name matching ignores spacing, punctuation and accents, and reads JR/SR/II/III/IV from the name.
  Different birthdates or JR vs SR never match.

### Things to know about the dashboard

- Old-system claims came over with status 0 but count, until they are cancelled here.
- Waiting = made here, not released, any month.
- Beneficiaries are counted once. Sectors overlap, so they don't add up.
- Budget is live from sysPFM (`getAnnualBalance`). 503 when finance is down: show it in the card.

### Who can see what

- Claims (list, details, history), dashboard, creditor totals and claims, reports and a person's claims:
  Social Worker and admins only. Frontliners get 403.
- Creditor claims CSV export and the removed creditors list: admins only.
- Frontliners file new claims and can fix one until a social worker picks it up (403 after that).
  The money side stays social workers and admins only.
- A frontliner (without other roles) edits person basics only: changing civil status, occupation or income,
  or any family link, is 403. Resending the same values is fine.

### Things to know about claims

- New claims need a date, lane and patient. The queue number is per day and lane.
- Frontliners can't set `amount`, `creditorId`, `fundSourceId`, `modeId`, `socialWorkerAssessment` or
  `isReleased` (403).
- Releasing needs an amount. A released claim is locked (409) until an admin saves `{ transactionId,
  isReleased: false }` on its own.
- The fund source must be for the claim's year. Only admins move a claim to another day.
- Send dates as picked (`2026-11-17T09:00`). UTC (`...Z`) is turned into PH time.
- Cancelled claims are hidden from lists and totals, and can't be edited.
- Every change goes to AuditLog (`Transaction`).
- A new claim for someone already open in that day's queue (not released, not cancelled) is 409
  `{ message, transactionId, queueNoCode, sameDay: true }`. Send `confirmSameDay: true` for a real second visit.
- Releasing needs the client's birthdate (exact, or estimated from an approximate age: `birthdateEstimated` on the
  person), every claim, old ones too.
- `clearRepresentative: true` removes the representative (the linked person and the old free-text one). The
  representative can't be the client.
- `assistanceCode` (program) is a code from `/api/lookups/assistance-programs`, Social Worker / admins only.
  Releasing needs it, like the amount.
- Money fields, program and release: only the social worker holding the claim, or an admin (403 otherwise:
  "Check out the claim first." or "{name} is handling this claim.").

### Things to know about the queue

- Today only (PH date), cancelled claims never show. Lanes are served Senior/PWD (S), then VIP (V), then
  Regular (R): a lane waits until the ones before it are empty.
- Waiting = no social worker, not released. Being served = has one. Done = released. Not served = marked so
  with a reason.
- One open client per worker: release it, return it or mark it not served before taking the next.
- A returned client lines up again from when they were returned: behind those already waiting, ahead of
  anyone who arrives later. They keep their number.
- A client marked not served comes back with a checkout; send `{ reason }` and it is logged.

### Things to know about reports

- `GET /api/reports/transactions` and `/perennial-clients` (logged in) count the same claims as the dashboard:
  old-system claims yes, cancelled no. Query: `assistanceCode`, `dateFrom`, `dateTo`.
- The claims report stops at 5,000 claims: 400 with a message to narrow the dates.
- `GET /api/balance/annual?year=` is admins only, 503 when finance is down.

### Things to know about fund sources

- An appropriation (per year) or a trust fund. The dashboard budget reads them through `getAnnualBalance`.
- Accounts come live from sysPFM, OPSWDO only (office 17). Finance down: saving returns 503, the list
  still loads with `financeAvailable: false`.
- Only accounts that paid aid can be picked: aid wording in finance (`aidObligations`) or paid to a
  Claims creditor (`creditorPayments`). Saving any other account returns 400. Show
  `finance.paysAid: false` as a problem.
- One account per year. No inactive: remove instead, only when no claims use it. Once claims use it,
  year and program are locked.
- `released` = `original + supplemental + adjustments`, `obligated` is approved only, `pending` is
  separate. Trust funds skip deleted receipts. Can differ from the dashboard budget.
- Changes go to AuditLog (`FundSource`).

### Things to know about person review

- Only the decision is saved (AuditLog, `DuplicateGroup`). Nothing is merged yet.
- `review.keepPersonId` is the record the others will be merged into.
- `personIds` must be the whole current group. A group comes back if someone new joins it.

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
