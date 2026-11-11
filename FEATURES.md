# Features

What the app does today, by area. Every line here is implemented and can be exercised in the running
app; the history of how it got here is in [CHANGELOG.md](CHANGELOG.md), what is planned is in
[ROADMAP.md](ROADMAP.md).

## Inventory

- A tree of records with children nested under their parent; a record can hold any depth.
- Create from the list (“New item”, a floating button on phones), **add a child straight from the
  open record**, or **duplicate** one (parent, icon and attributes carried over, named `<name> (copy)`).
- Search matches the record's name **and its attribute values**: `SN-0042` finds the item that
  carries it, `serial` finds the items that have that key, and a matched row shows the pair under the
  name. The count reports rows that matched on their own, and the panel says when nothing matches.
- Rows show a child count while collapsed and a paperclip when the record carries files.
- Delete from an overflow menu that names what it deletes, with confirmation. A record with children
  is refused (`409` with a message that says how many), so the tree never loses a branch by accident;
  a record without them is moved to the archive instead of disappearing. Restoring and listing the
  archive exists in the API only (`PATCH /api/item/{id}/activate`, `GET /api/item/archive`), not yet
  in the UI.
- Empty states everywhere: no records yet, nothing matches, and “Select an item to edit it”.

## Attributes

- The set of attributes comes from Configuration (`Attribute` constants); a label with children there
  becomes a choice list, anything else is free text.
- **Typed values**: Text, Number, Money, Date or LongText per attribute definition. Money and Number
  get the decimal keypad, digits only, right alignment and `10,000,000` while unfocused; Date opens
  the device calendar; LongText is a textarea across the row.
- **Attributes of your own**: a key the catalogue does not define can be added to a single item and
  is kept with that item only. A value without a name, or two rows with the same name, cannot be
  saved.
- A filter inside the form once there are more than five attributes, matching name or value.
- camelCase keys read as words (`BuyPrice` → *Buy Price*) without changing what is stored.

## Attachments

- A `Photo` opens the camera (`Take a photo`, with a gallery option); every other type opens the file
  manager (`Select a file`). New rows default to Photo when the catalogue has it.
- Uploads are multipart: the file travels as bytes (no base64, no third of the size in escaping),
  streamed to `media/items/{itemId}/`, with a 320px JPEG preview in the row (quality 72) and a 20 MB
  cap. The picker shows the photo immediately through a local object URL.
- “Open original” streams the file from `GET /api/attachment/{id}/file` (range requests supported) so
  a large photo opens in the phone's viewer instead of being decoded into the page.
- Deleting an attachment or a record removes the files from disk, not only the row.

## Configuration

- A tree of constants (FieldType, AttachmentType, Category, Brand, Classification, Type, Attribute)
  with the same create / add child / duplicate / delete behaviour as inventory records.
- Attribute definitions carry a **data type**, with a one-line explanation of each choice.
- Seeded on first run (249 nodes) so a fresh database is usable immediately; items are never seeded.

## Phones and the local network

- The list is the page and the form is a full-height sheet with a pinned footer above the safe area;
  the action that matters is never below the fold.
- Unsaved edits are protected: closing the sheet, or switching record on a desktop, asks first.
- The API binds `0.0.0.0:5173` and the UI `0.0.0.0:9000` in the `lan` profile, development CORS is
  open, and the client derives the API host from the page it was served from — so the same build
  works from `localhost` and from the LAN address.

## Installable

- The app is a PWA: add it to the home screen from the browser and it opens without the address bar
  (`display: standalone`), with its own icon (gradient monogram, maskable variants for Android).
- A service worker caches the shell, so opening it without a network shows the interface and an
  honest `Cannot reach the API` instead of a blank page.
- When a new version is deployed, a notification offers **Reload** rather than reloading under an
  open form.

## Access and roles

- The API answers `401` without a login. The five routes that may stay anonymous are declared one per
  line in `endpoints.allow-anonymous.txt` (the three `/test` health checks, the attribute catalogue
  and the three `/api/auth` endpoints), and a contract test fails when an anonymous route is not
  declared there.
- **Four roles, enforced by action rather than by controller**: `superadmin` > `admin` > `operador` >
  `lector`. Reads need any role, records and attachments are written by `operador` and up, deletes and
  the configuration tree by `admin`, users, audit and impersonation by `superadmin`. The table is
  [SECURITY_AUTH_MATRIX.md](SECURITY_AUTH_MATRIX.md) and `Testing/AuthMatrixTests.cs` executes it.
- **Login screen** (`pages/login.vue`) with the server's message on failure and a return to the route
  the user was trying to open. The access token lives 15 minutes; on a `401` the client refreshes once
  (`POST /api/auth/refresh`, `HttpOnly` cookie, rotation) and otherwise sends the user back to the
  login instead of leaving a blank screen.
- **The interface follows the role**: the drawer, the forms and the router guard read the same list
  (`services/roles.ts`), so `lector` is not offered a write control that would bounce, and
  `admin`/`superadmin` screens are unreachable by URL for the roles below them.
- **Superadmin dashboard** with three panels: accounts (list, create, change roles, deactivate),
  audit (actor/entity/action/date filters, before/after viewer, CSV export) and impersonation.
- **Impersonation is read-only and visible**: `POST /api/users/{id}/impersonate` returns a 7-minute
  token whose `sub` is the other account and whose `act` claim names the superadmin, every write
  answers `403`, a permanent banner sits over the app, and starting/stopping it writes
  `ImpersonateStart`/`ImpersonateEnd` to the audit trail.

## Audit trail

- Every change is recorded **at the persistence layer**, by one `SaveChangesInterceptor`
  (`Services/AuditSaveChangesInterceptor.cs`), so a controller cannot forget to log: timestamp, actor
  id and email, impersonated account, action, entity, entity id, `BeforeJson`, `AfterJson`, IP, user
  agent and request id.
- **The actor comes from the token**, never from the request body; `PasswordHash`, `SecurityStamp`,
  tokens and token hashes are redacted before anything is written, and audit rows cannot be modified
  or deleted through EF.
- Explicit rows for `Login`, failed login, `Logout`, refresh-token reuse, `RoleChange` and `SoftDelete`,
  so a deletion is findable as such instead of hiding as an update.
- `GET /api/audit` with filters and pagination, `superadmin` only, `ProblemDetails` on a bad filter and
  **no write route at all** — `POST`, `PUT` and `DELETE` answer `405`.

## Data

- SQLite through EF Core; migrations run at startup, so a clone builds its own schema.
- The database file is not committed; deleting it starts over from the seed.
- Images and files live on disk, not in rows.

## Repository tooling

- `AI_INVENTORY.md` — a router index of both projects (790 symbols across 88 files, call edges,
  `path:line` anchors) plus one slice per module under `inventory/`, committed with the code and
  refreshed by `.githooks/pre-commit` from the staged files.
- `ROADMAP.md` — what is deliberately not built yet.
- `CHANGELOG.md` — every pull request and what it changed; this file — the resulting capability list.
- Checks used before each commit: `vue-tsc`, `eslint`, `dotnet build`, `dotnet test`, `npm test`.
- **API tests** (85) run the app itself over a throwaway database and media folder: the authorization
  matrix route by route, token abuse, the tree shape, the write paths, attachments written and served
  back byte-for-byte, archival deletion with restore, the audit redaction, user administration and the
  impersonation contract.
- **UI tests** (86) cover the tree editor's state machine, the attribute and attachment editors, the
  auth store and role-aware router guards, and the audit/user service clients.
