# Changelog

Every pull request, what it changed, and the commits that carried it. New entries go on top under
`Pending`; when the PR merges, the heading becomes its number and date.

The feature list these add up to is in [FEATURES.md](FEATURES.md); what is still planned is in
[ROADMAP.md](ROADMAP.md).

## Pending — `chore/mit-license`

**The project is MIT licensed.** `LICENSE` carries the canonical text and AGENTS.md says so, so
"can I use this?" now has a written answer instead of a guess.

## Pending — `docs/security-gate`

The commit gate is documented where the next person looks. `AGENTS.md` now has the three commands that
satisfy it, says that a review is consumed by the commit that carries it, and records that the rendered
matrix stays out of Git because it carries the HEAD it was generated for.

Also corrected: `AGENTS.md` still advertised 57 tests; the suite is 101.

## #25 — The file opens in the app, searchable attribute lists, and dark mode · merged 2026-11-11

**Opening an uploaded file works again, inside the app.** The *open original* action was an `<a>`
pointing at `/api/attachment/{id}/file`, so the browser opened a new tab without the bearer token,
the route answered `401` and the tab stayed blank — the thumbnail looked fine because it is a stored
preview, not a request.

- The file is fetched with the session the app already holds (`api.getBlob`, same single-flight
  refresh as every other call) and shown in a **modal** instead of a tab: images render as images,
  PDFs in a viewer, anything else offers *Descargar*. The bytes are requested when the dialog opens,
  not before, and the object URL is revoked on close.
- The route stays behind `RequiereLectura`, which is what a modal can do and a new tab cannot.

**Attribute lists can be searched, and an admin can add a value from the form.**

- The value control is a real input on a phone now: typing filters the options instead of leaving the
  long tap-menu as the only way in.
- When what the user typed is not in the list, `admin` and `superadmin` get *Añadir «X» a «Label»*;
  it creates the constant in the same group, refreshes the catalogue and leaves the row with the new
  value, so a missing value no longer means leaving the form for Configuration. `operador` and
  `lector` never see the affordance, and the API still requires `admin`.
- The new value lands under the catalogue node that owns the group (`Brand` values under the `Brand`
  node), not under the `Attribute` definition that merely names the attribute.

**Dark mode, system by default and switchable.**

- Quasar's `Dark` plugin is installed; `stores/theme-store.ts` keeps `auto | light | dark` in
  `localStorage`, `auto` being the default and what a fresh browser gets. `boot/theme.ts` applies it
  before the app mounts, so a reload in dark does not flash a white page first.
- The header carries the control (icon reflects the mode, menu with *Sistema* / *Claro* / *Oscuro*,
  the active one ticked). `auto` follows the OS and keeps following it.
- The screens that would have broken were repaired rather than left to Quasar's defaults: the tab
  panels had a `bg-white`, the tree dialog a `background: #fff`, and Quasar's colour utilities
  (`text-grey-7`, `bg-orange-1`/`text-orange-10`, `bg-red-1`, `bg-blue-1`) are plain hex, so
  `css/app.scss` re-pairs them in dark mode with measured contrast (6:1 or better on the dark page).

Verified: `npm run test` **161 passed** (18 files), `npm run typecheck` clean, `npm run lint:check`
exit 0. Against the running API: an upload then `GET /api/attachment/{id}/file` with the token answers
`200 image/png` byte-identical (68/68), the same request without a token answers `401`, and a constant
created with the payload the page sends appears in `GET /api/constants/group/Brand` and in the
Configuration tree under the right parent. Dark mode was checked in a real browser at 780×437: the
inventory tree, the dashboard tables (no white panel left), the file preview modal and the login card
all render dark with readable text, and switching back to *Sistema* returns the body to the OS mode.

## #23 — Impersonation by role, the security round and the archive screen · merged 2026-11-11

**Impersonation is by role, and only downwards.** The superadmin picks a role, not an account, which
is the rule the app needed to be honest about: nobody signs in "as" a colleague.

- `POST /api/users/impersonate` takes `{ "role": "operador" }` and replaces the account-based route.
  `superadmin` may enter as `admin`, `operador` or `lector`; `admin` as `operador` or `lector`;
  `operador` as `lector`; `lector` may not impersonate at all. `superadmin` is never a valid target,
  and a role at or above the caller's own answers `400`.
- The token keeps the **actor's real id** in `sub` and carries the target role as its only role claim,
  plus `act`, `act_email`, `impersonated_role` and `scope=read`. Seven minutes, and deliberately not
  renewable: the refresh cookie belongs to the caller's own session, so when the borrowed token
  expires the session comes back as itself and the banner clears, instead of the expired token being
  re-installed and bouncing the user to the login form.
- Read-only is enforced in `Program.cs` for every unsafe method, including the archive restore, and an
  impersonation token cannot impersonate again. `ImpersonateStart`/`ImpersonateEnd` are audited with
  the actor taken from the token; no audit row ever names the target as the author.
- The dashboard's impersonation panel is a role picker limited to the roles below the caller, and the
  store refuses a role at or above the caller's own before the request leaves the browser.

**An account created in the app can log in.** It could not before: `CreateUser` made an Identity row
with no password, so every account the dashboard created was unusable.

- `POST /api/users` accepts an optional `password`; without one the server generates a 24-character
  password (one character from each required class, shuffled with `RandomNumberGenerator`) and returns
  it **once** as `initialPassword`. A password the caller chose is never echoed back.
- `POST /api/users/{id}/password` (superadmin, optional body) sets or resets a password, returns the
  generated value once, revokes that account's refresh tokens and writes a `PasswordReset` audit row.
- The interface surfaces both: an optional field in the create dialog, a one-time dialog with the
  generated password, and a *Restablecer contraseña* action per row.

**The audit trail is the archive's companion, and both now have the UI they were missing.**

- New **Archivo** tab: filter by label, deleter and date range, paginate over the server's total,
  and restore a record with confirmation; a row already restored is marked and cannot be restored
  again, and a `404` is reported as "ya fue restaurado" instead of an error screen.
- Restoring a record now obeys the same depth bound as create and update. It used to re-insert the
  row without checking, so a record archived at level 12 could come back at level 13 once its parent
  had been moved deeper — measured today, and closed with the same `AnalyzeParentChain` guard the
  update path uses.

**The login door has a brake.** Until now the only limit on `POST /api/auth/login` was Identity's
lockout, and `refresh`/`logout` had none.

- A fixed window per client address, configured with `Auth:RateLimit:PermitLimit` (default 10) and
  `Auth:RateLimit:WindowSeconds` (default 60), answers `429` once spent. It covers the `/api/auth`
  controller only: reading the inventory is not inside that window.
- Non-browser clients may now send `POST /api/auth/refresh` with a borrowed bearer without the
  impersonation write-block answering `403` — the refresh trades the cookie for the caller's own
  token, which is the whole point of the route.

- Leaving an impersonation now goes through `POST /api/auth/stop-impersonation` instead of swapping
  the token in the browser, so the audit trail records the end of every impersonation, not just the
  ones an API client closed. A refused or unreachable call still returns the caller to their own
  token, or ends the session when there is none — the banner never outlives the borrowed token.

**Security — the seven findings the scanner recorded are closed**
- **A file can no longer be written for a record that does not exist.** `POST /api/attachment/upload`
  checks the record first, and if the insert fails anyway the file it just wrote is removed. It used
  to answer `500` and leave an orphan under `media/items/{id}/` that no route could name or delete.
- **The extension saved to disk is an allow-list.** A client-supplied `.html`, `.svg` or double
  extension is re-detected from the content instead of being kept, which is what stops this from
  becoming stored XSS the day the media folder is served.
- **Reusing a refresh token kills the whole family.** The reuse was audited and then the replacement
  token stayed alive, so a copied cookie kept rotating a working session. Every live token of the
  account is revoked now, and the honest client signs in again.
- **`GET /api/logs/{date}` parses the segment** (`yyyy-MM-dd`) and refuses a path that resolves outside
  the `Logs` directory; an encoded separator or a drive letter never reaches `Path.Combine`.
- **`BaseRepository.Delete` can reach the row it was asked about.** The lookup ran through
  `HasQueryFilter(IsActive)`, so a soft-deleted `Constant` or `Attachment` could never be physically
  removed and the second delete silently did nothing.
- **A control that gets switched off now says so.** Outside Development the API refuses to start when
  `CORS:AllowedCors` is empty (the policy allows credentials, so "any origin" is not a default a
  deployment may take) or when `Auth:Enforce=false`; in Development both log a warning naming the risk.

- **The JSON attachment route got the same two guards** as the multipart one. The independent
  validation found the orphan-file bug alive on `POST /api/attachment` (`uploadBase64`), which is the
  route the form used before the multipart work landed.
- **`GET /api/logs/{date}` reads the files the logger actually writes.** The reader asked for
  `yyyy-MM-dd_Logs.txt` while `FileLoggerProvider` writes `yyyyMMdd_Logs.txt`, so every well-formed
  date answered 404 with the log sitting next to it. Found by the same validation pass.
- **The hard delete no longer 500s on a row that still has children.** A constant with live children
  cannot be removed by the database (`Restrict`), and the unfiltered lookup that made a second delete
  reach the row turned that into a 500 the UI could not explain: the row now stays soft-deleted and
  the call answers `204`, which is what "already deleted" means.

- **The failed-insert cleanup is reachable.** `base.Post` swallows the exception and answers `500`, so
  the cleanup around it never ran and the orphan came back the moment an insert failed. The JSON route
  now does the insert itself, removes the file and then answers `500`; `DeleteRange` got the same
  guard as `Delete`, and the thumbnail is computed before the write so a throw cannot leave a file
  behind either. A test renames the table out from under the insert and asserts the media folder is
  empty afterwards.

Verified: `dotnet test` **101 passed**, including `Testing/SecurityRegressionTests.cs` — nine tests
covering the upload guards on both routes and a forced insert failure, the extension allow-list, the
log path and its file-name mismatch, token-family revocation and the two deletes. Each finding's state
is tracked in `SECURITY_MATRIX.md` and the gate (`security_matrix.py check`) reports it.

**Database debt closed**
- `AuditLogs.Action` goes from 32 to 64 characters and `EntityId` from 512 to 1024. SQLite stores them
  as `TEXT` and never complained, which is exactly why it would have broken only after a move to SQL
  Server — one action name over 32 characters away. The migration records the model change and states
  that the width is metadata-only here.
- `ItemsArchive.Label` gained an index: the archive screen filters by label and the table only grows.

**Fixed**
- A password reset now invalidates the access tokens already issued: the token carries `sstamp` and
  the pipeline compares it with the account's current security stamp, so a reset actually ends the
  session (measured: the old bearer token answers `401` immediately after the reset).
- A role change now reaches the sessions already open: `PUT /api/users/{id}/roles` rotates the
  account's security stamp, so the token minted with the old roles answers `401` instead of carrying
  them until it expires (measured: a promoted `lector` token stops working at once, and the next
  login writes).
- `GET /api/item/archive` computed its page offset in `int`, so a large `page` wrapped and silently
  served the first page; the offset is now computed wide and clamped.
- `npm run lint:check` exited 2 on a clean checkout because its prettier half resolves
  `--ignore-path .gitignore` against `imcui/`, which had none, and walked the built bundle. The
  documented check now passes (`imcui/.gitignore` added, exit `0`).

Verified: `dotnet build IMCAPI/IMCAPI.csproj` 0 errors, `dotnet test` **89 passed**, `npm run test`
**129 passed**, `npm run typecheck` clean, `npm run lint:check` exit `0`.

## #22 — Roadmap notes: impersonation by role, audit widths, refresh dates · merged 2026-11-11

Documentation only. `ROADMAP.md` records the constraints that still have to be honoured before (c),
(d) and (e) close: impersonation is **by role and down the hierarchy**, never as a specific account;
the `AuditLog` column widths have to be reviewed against the fields they store; `ItemsArchive` is the
delete ledger and `AuditLogs` the change history, and they must not duplicate each other; and a
refresh token's dates stay server-side.

## #21 — Deleting a record archives it · merged 2026-11-10

- **Item deletion is now archival**, not soft deletion: `ItemsArchive` stores a detached item snapshot,
  deletion actor/time and attachment metadata; the live item and attachment rows are removed and the
  attachment files are deleted.
- **Superadmin archive listing** supports deletion date, actor, label and pagination filters; admin
  activation restores the latest unrestored snapshot, preferring the original ID when available and
  writing a `Restore` audit action.
- Deletion now returns `409 ProblemDetails` when children remain, preserving their parent links. Item
  hierarchies are cycle-checked and limited to seven levels by default; configure with `Items:MaxDepth`
  or override using `ITEMS_MAX_DEPTH`.
- `IsActive` is server-owned on item create and update. API coverage exercises deletion, restoration,
  filtering, authorization and server-managed activation state.

## #20 — The service worker registers without a lazily installed dependency · merged 2026-11-10

The PWA registration imported `register-service-worker`, which is declared in `src-pwa/package.json`
and installed by Quasar **lazily**, only when it builds PWA mode. Any checkout, worktree or CI run that
type-checks before that first PWA build failed with *Cannot find module 'register-service-worker'*, and
the dev server painted the error over the login screen.

The registration is now written against `navigator.serviceWorker` (typed, no dependency), so the code
that is type-checked is the code that runs, and it is wired through `sourceFiles.pwaRegisterServiceWorker`
instead of sitting in the commented block. The unused `sw-offline` dispatch is gone.

Verified: `npm test` 86 passed, `npm run typecheck` clean, `npm run build` and `npx quasar build -m pwa`
exit 0 with the `sw-updated` event in the bundle.

## #19 — The code map becomes a router index, and using it is a rule · merged 2026-11-10

`AI_INVENTORY.md` went from a single 131 KB document to a **6 KB router index plus 18 module slices**
under `inventory/`, so a change starts by opening the one module it touches instead of walking the
repository. The generator's default split threshold dropped from 160 KB to 40 KB for that reason, and
the "how to use" block is now a mandatory protocol (index first, ranges not files, reuse before
writing, read Impact before a signature change, never paste the map or a file into the prompt).

`AGENTS.md` gains the same rules as a required order of work, plus the ownership map, and the
pre-commit hook is now installed and versioned in `.githooks/` instead of being assumed: it had never
been installed in this clone, which is why the map had gone stale and still listed the routes from
before the users work.

## #18 — Users and impersonation · merged 2026-11-10

The superadmin can now manage accounts and look through another role's eyes without knowing its
password.

- **`UserController`** (`GET`, `POST`, `PUT /{id}/roles`, `DELETE /{id}`) under `RequiereSuperadmin`,
  answering the shape the dashboard already expects; `UserProfile` carries `IsActive` and the last
  access time.
- **Hard rules, verified against a live instance**: a duplicate email answers `409`; the `superadmin`
  role cannot be assigned through the API (`400`); the bootstrap superadmin cannot be deleted (`409`)
  or demoted (`400`); nobody can grant themselves a role (`400`). `DELETE` deactivates the Identity
  row instead of deleting it, so the audit history keeps naming a real account.
- **Impersonation** (`POST /api/users/{id}/impersonate`): a 7-minute access token whose `sub` is the
  impersonated account and whose `act` claims name the superadmin, with `scope=read`. With it, reads
  answer `200`, every write answers `403`, `/api/users` answers `403` and impersonating again answers
  `403`. `POST /api/auth/stop-impersonation` returns a normal token for the actor, which writes again
  (`200`).
- **Audited**: `RoleChange`, `ImpersonateStart` and `ImpersonateEnd` rows, all with the actor taken
  from the token.

Verified: `dotnet build` clean and `dotnet test` **74 passed**; the matrix above was executed against a
fresh instance on port 5210 with its own database.

Not done yet, and it is why the dashboard cannot log in as a freshly created account: **there is no
way to set a password for a new user**. That lands with the next change.

## #17 — Login sends its credentials · merged 2026-11-10

`imcui/src/stores/auth-store.ts` called `POST /api/auth/login` without `credentials: 'include'`, so the
browser discarded the `HttpOnly` refresh cookie and the session never outlived its access token. The
call now sends credentials, and refresh works cross-origin.

## #16 — Who changed what, with the before and after · merged 2026-11-10

The database now records **who changed what, with the before and after**, instead of only a
last-touched-by string the client could forge.

- **`AuditLog` table + migration** (`TimestampUtc`, actor id and email, impersonated user, action,
  entity, entity id, `BeforeJson`, `AfterJson`, IP, user agent, request id) with indexes on
  `(Entity, EntityId)` and `TimestampUtc`.
- **One `SaveChangesInterceptor` is the only writer**, hooked at the persistence layer so no
  controller can skip it. It serialises the changed values before and after, takes the actor from the
  token (never from the body) and the impersonated user from the actor claim when there is one, and
  redacts `PasswordHash`, `SecurityStamp`, tokens and token hashes before anything is stored. Audit
  rows are inserted through parameterised SQL from `SavedChanges`, which is what keeps the interceptor
  from auditing itself; modifying or deleting an audit row through EF throws.
- **`CreatedAt`/`CreatedBy` on the base entity**, written server-side. The client cannot set `Id`,
  `CreatedAt`, `CreatedBy`, `UpdatedAt` or `UpdatedBy` any more: `POST` with `id: 999` and forged
  metadata stores `id: 1` and the authenticated actor (verified).
- **Explicit rows** for login, failed login, logout, reuse of an already-rotated refresh token and
  role changes.
- **Soft deletes are their own action** (`SoftDelete`) so a delete is findable with
  `GET /api/audit?action=SoftDelete` instead of hiding as an `Update`.
- **`GET /api/audit`** with filters (actor, entity, entity id, action, from, to) and pagination,
  `RequiereSuperadmin`, `ProblemDetails` on invalid filters, and **no write route at all** (`POST`,
  `PUT`, `DELETE` answer 405).
- **The update path reads the real row before writing it.** `UpdateAsync` used to attach the entity
  that came from the request, which has no original values: that made `BeforeJson` and `AfterJson`
  identical and the audit useless. It now loads the tracked row, copies the client's fields and saves,
  so the audit can answer what changed.
- **CORS allows credentials**, which is what lets the PWA's `HttpOnly` refresh cookie survive a
  cross-origin call.

Verified: `dotnet build` clean, `dotnet test` **70 passed**, and against a live instance an update
whose audit row reads `Label: "forged-meta" → "renamed-by-verify"`, a `SoftDelete` row found by
action, forged audit fields ignored, and `405` on every write attempt against the audit route.

## #15 — The dev server and the API bind the LAN · merged 2026-11-10

`scripts/dev-lan.sh` starts the API on `0.0.0.0:5173` and the Quasar dev server on `0.0.0.0:9000`, so a
phone on the same network opens the app by address instead of only by `localhost`. The flag is
`--hostname`; `--host` is not a Quasar CLI option and aborted the run.

## #14 — Login screen, role-aware navigation and the superadmin dashboard · merged 2026-11-10

The API started refusing anonymous requests, and the PWA had no way to answer: it read the token once
when the module loaded and never showed a login form. This makes the browser client usable again and
gives the superadmin somewhere to work.

- **Session for real**: the token is read on every request, attached, and on a `401` the client tries
  `POST /api/auth/refresh` once (credentials included) before retrying; if that fails the session is
  cleared and the app sends the user to `/login`.
- **Login screen** with the server's message on failure and a return to the interrupted route.
- **Role-aware navigation**: the drawer and the forms follow the role — `lector` has no write
  controls, `operador` edits records and attachments, `admin` opens configuration, the dashboard is
  `superadmin` only. The router guard is the enforcement; the UI just stops offering what would
  bounce back.
- **Superadmin dashboard** with three panels: users (list, create with a role, change roles,
  deactivate), audit (actor/entity/action/date filters, before-after viewer, CSV export) and
  impersonation (pick a user, permanent banner, all writes disabled while impersonating).
- **`/api/users` and `/api/audit` do not exist yet**: the service layer is written against the agreed
  contract and a `404` renders an explicit "API pending" state instead of an error screen.
- **Pending on the API side**: the silent refresh needs the API's CORS policy to call
  `AllowCredentials()`, because the refresh token is an `HttpOnly` cookie that the browser discards on
  a cross-origin call. That one-line change ships with the audit-trail pull request; until it lands,
  refresh only works when the app and the API share an origin.

Verified: `npm test` 86 passed (9 files), `npm run typecheck` clean, `npm run build` succeeded.

Both API gaps named above closed later: `/api/audit` and `AllowCredentials` in #16, `/api/users` in #18.

## #13 — Working rules for the repository · merged 2026-11-10

`AGENTS.md`: how a change lands here — one branch and one concern per pull request, the checks that are
actually run and the command that runs each, the inventory-first order of work, and the security
invariants that are not negotiable.

## #12 — Photos travel as bytes · merged 2026-11-09

Photos travel as bytes, not as base64 inside JSON.

- **`POST /api/attachment/upload` takes multipart form data** (`itemId`, `type`, `fileName`, `file`)
  and streams the file to disk: a third smaller than the base64 payload and no encoded copy in
  memory on the phone. Rejects a missing file, a bad record id and anything over the 20 MB cap.
- **The item payload no longer carries files.** The form saves the record first, then uploads the
  picked files against the id it got back (`afterSave` in `useTreeEditor`), so a new record and its
  first photo work in one pass. The sheet stays busy until the uploads finish; a failed upload is
  reported and does not undo the record.
- **The preview is local until it is saved.** A picked photo is shown through an object URL instead
  of a base64 copy — no encoding, no megabyte string in the form, and the URL is released when the
  row goes away or the sheet closes.
- **Deleting a file prunes its folder** when the last one is gone, so deleting records leaves no
  empty directories behind.

## #7 — Feat/UI polish · merged 2026-11-09 (2 commits, 8 files)

- **The list reads as records, not rows.** No more `0 - Add New` node (creation is a button and an
  empty state), 44px targets, hairline separators, tinted parents, child counts, chevrons on leaves,
  and `4 matches` when searching.
- **The form has a rhythm.** Sections (Details / Attributes / Attachments), the record's own name as
  the title, labels as words (`BuyPrice` → *Buy Price*), one header row for attributes, numbers
  formatted until focused and right-aligned, prose spanning the row.
- **Attachments are a compact list** with a 48px thumbnail instead of full-width cards.
- **Delete moved to an overflow menu** that names what it deletes; the footer is Reset and Save.
- **An empty pane says “Select an item to edit it”.**
- `25d23d9 docs: roadmap…`, `0ca39cc feat(ui): a clearer list, a real form rhythm and fewer traps`

## #6 — Photo opens the camera, a document the file manager · merged 2026-11-09 (1 commit, 2 files)

- A `Photo` attachment opens the camera (`capture="environment"`, “Take a photo” with a gallery
  option); every other type opens the file manager (“Select a file”). New attachments default to
  Photo when the catalogue has it.
- `e5aa36d feat(attachments): a photo opens the camera, a document opens the file manager`

## #5 — Attachments on disk · merged 2026-11-09 (1 commit, 18 files)

- **The original goes to disk, a preview in the row.** `media/items/{itemId}/{guid}{ext}` plus a
  320px JPEG (`ThumbnailBase64`, quality 72), 20 MB cap, SkiaSharp (MIT).
- `GET /api/attachment/{id}/file` streams the original with range support; deleting a row deletes the
  file; `FileBase64` → `ThumbnailBase64` with a migration and a one-time backfill.
- The database went from 16.4 MB to 221 KB once images stopped living in rows.
- `a472273` (branch `feat/attachments-on-disk`)

## #4 — Seed and mobile · merged 2026-11-09 (2 commits, 13 files)

- **The database is not in source.** `imc.db` untracked and ignored, migrations at startup, the
  configuration tree seeded on first run — items are never seeded.
- **Phone-first list and edit sheet**: the list is the page, the form is a full-height sheet with a
  pinned action bar above the safe area.
- `chore(db): stop tracking the SQLite file…`, `feat(ui): phone-first list and edit sheet`

## #3 — Attributes and attachments driven by the constants tree · merged 2026-11-09 (1 commit, 5 files)

- Attribute rows come from the `Attribute` constants: a label with children is a choice list,
  anything else is free text. Attachments follow the `AttachmentType` catalogue.
- `feat(ui): attributes and attachments driven by the constants tree`

## #2 — One tree editor instead of two copies · merged 2026-11-09 (1 commit, 4 files)

- `useTreeEditor` holds load / select / save / delete / dirty state; `TreeMasterDetail` renders the
  split view and the sheet; the pages keep only their own fields (348 → 84 lines for Configuration).
- `feat(ui): one tree editor instead of two copies`

## #1 — UI p0 · merged 2026-11-09 (6 commits, 14 files)

- **Runnable from a clean clone**: `npm ci`, dev server, no missing env var.
- **A feedback layer**: every failure shows the API's own message; every success confirms.
- **Dialog correctness**: validation actually runs, deletes ask for confirmation, buttons cannot
  double-post.
- **Honest state**: loading, empty and error states instead of a blank pane; valid attribute JSON.
- **Reachable from another machine**: API on `0.0.0.0:5173`, development CORS open, the client
  derives the API host from the page.
- **Pages named after their job**: `inventory` and `configuration`, `/` redirects to `/inventory`.

## Auth core: JWT on every route · merged 2026-11-10

The API stopped being an open CRUD endpoint. **C-1** (critical, CWE-306), and with it **A-1**,
**A-2** and **A-3**, are closed: every route now answers `401` without a credential except the five
declared in [endpoints.allow-anonymous.txt](endpoints.allow-anonymous.txt) and the auth endpoints
themselves.

- **JWT authentication wired, and wired in the right order.** `AddIdentityCore` over the existing
  `IdentityDbContext` (the `AspNet*` tables were already in the initial migration, just empty) plus
  `AddJwtBearer` validating issuer, audience, signature and lifetime, with the signing key required
  to be at least 32 bytes at startup. `app.UseAuthentication()` now runs **before**
  `app.UseAuthorization()` — before this, the authorization middleware had no scheme behind it, so no
  `[Authorize]` could ever be satisfied.
- **`POST /api/auth/login`, `/refresh`, `/logout`.** Access token of 15 minutes carrying the roles;
  refresh token of 64 random bytes stored **hashed** (`SHA-256`) with rotation
  (`ReplacedByTokenHash`), revocation on logout, and the cookie is `HttpOnly`, `SameSite=Lax` and
  scoped to `/api/auth` so it never rides along on business requests. Login with a bad password
  answers `401` and counts towards Identity's lockout.
- **Roles and the single superadmin, by construction.** `superadmin`, `admin`, `operador` and `lector`
  seeded at startup; the bootstrap superadmin comes from `Auth:Bootstrap:*` / `AUTH_BOOTSTRAP_*` (no
  default password in source), and the app refuses to boot when more than one account carries the
  role.
- **`[Authorize]` per action, not per controller**, following
  [SECURITY_AUTH_MATRIX.md](SECURITY_AUTH_MATRIX.md): reads need `RequiereLectura`, writes
  `RequiereEscritura`, deletes and the attribute catalogue `RequiereAdmin`, `api/logs/{date}`
  `RequiereAdmin`. The three test endpoints and the read side of `/api/constants` stay anonymous on
  purpose.
- **Production stops leaking traces**: `UseExceptionHandler` outside Development (**M-2**), so the
  developer exception page no longer hands out paths, SQL and versions.
- **The `ParentId` cycle DoS is actually fixed now (C-2).** The first pass only survived two-node
  cycles; the re-audit measured a `Stack overflow` (exit 134) on three or more, because the `Parent`
  walk was seeded with `{ id }` alone. The parent recursion now carries the ancestors it walked plus a
  depth bound, both guards use `IgnoreQueryFilters()` so a cycle through a soft-deleted row is refused
  instead of stored, and `parent` is rendered again (the first pass had silently nulled it).
- **Tests: 57 green**, including a contract test that fails when an anonymous endpoint is not
  declared, login/refresh/revoke coverage, `401` without a token and `403` with the wrong role, the
  single-superadmin invariant, and three new C-2 tests — one of which persists a three-node cycle
  straight into the database, the scenario a controller guard cannot see.
- **The authorization matrix is executed, not described** (`Testing/AuthMatrixTests.cs`): all 17
  protected routes are called with no credential (`401`), with every role below their declared
  minimum (`403`) and with the minimum role itself (authorized), plus token abuse (foreign signing
  key, expired, tampered) and the refresh lifecycle (rotation, replay of the retired token, no
  cookie, authenticated account with no role → `403`). Verified by mutation: widening one policy or
  adding an `[AllowAnonymous]` breaks three tests at once.

## HTTP surface audit: 15 findings · merged 2026-11-10

The HTTP surface audited end to end and reproduced in a live run: **15 findings — 2 critical, 3 high,
6 medium, 4 low**. Full report in [docs/security/2026-11-10-audit.md](docs/security/2026-11-10-audit.md);
the per-route decision is [SECURITY_AUTH_MATRIX.md](SECURITY_AUTH_MATRIX.md), and the five routes that
may stay anonymous are declared in
[endpoints.allow-anonymous.txt](endpoints.allow-anonymous.txt). Neither critical is fixed yet: C-2 is
in progress, C-1 is phase (b) of the roadmap.

- **C-1 (critical, CWE-306) — the API authenticates nobody.** `Program.cs:117-124` calls
  `UseAuthorization()` with no authentication scheme registered and never calls `UseAuthentication()`,
  and every `[Authorize]` is commented out (`Controllers.cs:15,138,259`, `BaseController.cs:26`).
  Confirmed without a credential: `POST`, `PUT`, `DELETE`, `upload` and `/file` all answered 200/204
  — full inventory read, write, delete and arbitrary file upload from anyone who reaches the port.
- **C-2 (critical, CWE-674) — a `ParentId` cycle kills the process.** Two anonymous `PUT`s (a
  record's parent set to its own child) leave EF's `Children` navigation closed on itself and the DTO
  constructors recurse until the stack is gone (`DTOs.cs:49-51` and `:132-134`, plus the twin in
  `Controllers.cs:63-70`). Confirmed: `GET /api/item` and `GET /api/constants` stop answering and the
  process dies (`exit 134`, the overflow repeated 23,792 times); the database stays poisoned, so it
  dies again on the first `GET` after a restart.
- **A-1 (high, CWE-862) — destructive writes and deletes with no credential, and the delete reaches
  the disk.** `ItemController.Delete` (`Controllers.cs:76-95`) removes the attachment files before the
  rows, and `AttachmentStorage.Delete` is not a move to a bin: a loop over `1..N` empties the
  inventory and its photos for good.
- **A-2 (high, CWE-915) — mass assignment of `Id`, `IsActive` and `UpdatedBy`.** The client sets the
  audit field itself (`Models.cs:7-15`, `BaseRepository.cs:135-140`); `POST` returned
  `"updatedBy":"attacker"` verbatim, so every change can be filed under a name the attacker chooses.
  An explicit `Id` also collides with a live primary key and answers 500 instead of 409.
- **A-3 (high, CWE-532 / CWE-200) — `GET /api/logs/{date}` serves raw logs.** `LogsController.cs:19-37`
  returns the day's file as JSON, absolute server paths and full exception `ToString()`s included: a
  map of the host and of the code paths that fail.

## Merged — `feat/tests` (stacked on `feat/pwa`, 2026-11-09)

The suite stops lying, and grows teeth.

- **The two failing tests were a real bug.** `BaseRepository.GetById` used `AsNoTracking`, so EF's
  relationship fix-up never filled a record's children — the list endpoint got them only because it
  loads the whole table. `GetById` now tracks and loads the entity's collections; all six cases of
  `GetById_ReturnsChildEntities` pass, and `GET /api/item/{id}` returns a record with its children.
- **API integration tests** (`Testing/ApiTests.cs`, `WebApplicationFactory`): a throwaway SQLite
  database and media folder per run, migrations and seeding included. Nine tests over the real HTTP
  surface — tree and panels, create/read/update/delete, children on the detail endpoint, attachment
  upload → served bytes compared by SHA-256 → delete removes row and file, `attachmentCount` on the
  tree, the seeded catalogue's data types, and a record without a name being refused (which needed
  `[Required]` on `Item.Label`).
- **UI unit tests** (Vitest + jsdom, `npm test`): the tree editor's state machine (dirty tracking,
  child and duplicate forms, parent options, validation, confirmed delete) and the attribute editor's
  typed rows (money keypad, prose textarea, date picker, text kept verbatim, custom keys, filter).
- `AttachmentStorage` takes an optional `Media:Root` so tests (and a deployment) can keep files out
  of the project folder.
- **Deleting a record deletes its files.** The API soft-deleted the record and left the attachment
  rows and their files behind, although the confirm dialog says “Its attachments are deleted with
  it.” `ItemController.Delete` now removes the files and the rows first; a test proves the file is
  gone from disk and the list endpoint returns `204`.
- **A review pass over the suite**: the search logic moved out of the component into
  `services/search.ts` so it can be tested at all (14 cases: key/value matching, case and punctuation
  insensitivity, malformed JSON, parents kept for context, the count), and the API gained 8 cases it
  was missing — 404 on an unknown id, `400` on a mismatched PUT, range requests on the file endpoint,
  attachments by item (`204` when empty), the group endpoint, a label over the column length, an
  invalid upload, and the delete cascade. UI cases added for the attachment editor (camera vs file
  manager by type, preview vs icon, remove/add, the saved-file link) and for the tree editor's save
  paths (update vs create, the sheet closing only on success, a failed load keeping the page usable,
  a failed delete keeping the record, and the page's own snapshot counting as dirty).

## Merged — `feat/pwa` (stacked on `feat/search-values`, 2026-11-09)

Installable on a phone's home screen.

- **PWA mode on** (`quasar mode add pwa`): manifest, service worker and the Workbox runtime generated
  by the build; `src-pwa/register-sw.ts` replaces the scaffold comment block and reports an upgrade
  through a window event, which `src/boot/pwa.ts` turns into a notification with a Reload action
  instead of reloading under someone's edits.
- **Its own icon set**: 128/192/256/384/512 plus maskable 192/512, Apple touch icons and a pinned-tab
  mark, all generated from one gradient-and-monogram design on `#0f4c81`; the manifest names the app
  `IMC — Inventory Management`, `short_name` `IMC`, `display: standalone`, `start_url: /`.
- Verified on the built bundle (`quasar build -m pwa`, served from `dist/pwa`): manifest linked and
  parsed (7 icons), service worker registered at scope `/`, the app renders the six records, and with
  the network off the shell still loads and reports `Cannot reach the API`.

## Merged — `feat/search-values` (2026-11-09)

Search now looks inside the records, not only at their names.

- **The list no longer disappears while searching.** The match counter had taken the tree's `v-else`
  branch, so typing a query replaced the rows with a line of text — a regression from the polish
  round. The counter and the tree now share one branch and the tree keeps rendering.
- **Search matches attribute values.** `SN-0042` finds the record that carries it, `serial` finds the
  records that have that key at all, and a matched row shows the pair under its name
  (`Serial · SN-0042`) with the count reporting only rows that matched themselves, not the parents
  kept for context.

## Merged — `feat/ui-polish` (PR #8, 9 commits)

Guarded form, typed values, attributes of your own, duplicate, a filter and the committed inventory.

- **Unsaved work is protected.** Closing the sheet or picking another record on a desktop asks
  before dropping edits; the form says `Unsaved changes` while it differs from what was loaded.
  `bfd7400 feat(ui): ask before throwing edits away`
- **The list marks records that carry files.** One grouped query sets `AttachmentCount`; a paperclip
  appears on those rows. `17b4231 feat(api): tell the list which records carry files`
- **Attribute definitions have a data type.** `Constants.DataType` (Text, Number, Money, Date,
  LongText) with a migration and a one-time backfill of the seeded attributes; a “Data type” select
  in Configuration. `575d29b feat(attributes): an attribute definition carries a data type`
- **The item form follows the type.** Decimal keypad and separators for money, the device calendar
  for dates, a textarea for prose, and text that is no longer scrubbed to digits (`SN-0042` used to
  save as `-0042`). `93b4f50 feat(ui): typed values, attributes of your own, and a filter`
- **Attributes only one item needs.** “Add custom” adds a key of your own, stored on that item and
  rebuilt on load; a value without a name, or two rows with the same name, is refused.
- **Duplicate a record** from the overflow menu: same parent, icon and attributes, named
  `<name> (copy)`; files are not copied.
- **Filter inside the form** once there are more than five attributes.
- **Add a child from the open record**, so the parent does not have to be found in a list again.
  `90638db feat(ui): add a child from the record you are looking at`
- **The inventory is committed** and refreshed by `.githooks/pre-commit`, with `--no-stamp` keeping
  it byte-identical when nothing changed. `2c03125 chore: commit the inventory and keep it in step with the code`
- **Roadmap rewritten** to what is actually left. `89995d1 docs: the roadmap records what landed`,
  `a305759 fix(ui): put the attribute actions above the rows`

