# Changelog

Every pull request, what it changed, and the commits that carried it. New entries go on top under
`Pending`; when the PR merges, the heading becomes its number and date.

The feature list these add up to is in [FEATURES.md](FEATURES.md); what is still planned is in
[ROADMAP.md](ROADMAP.md).

## Pending — `feat/api-change-audit`

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
## Pending — `feat/ui-auth-dashboard`

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

## Pending — `security/auth-core`

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

## Pending — `security/auth-audit`

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

## Pending — `feat/multipart`

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

## Pending — `feat/tests` (stacked on `feat/pwa`)

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

## Pending — `feat/pwa` (stacked on `feat/search-values`)

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

## Pending — `feat/search-values`

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
