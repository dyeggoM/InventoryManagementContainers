# InventoryManagementContainers

The inventory of a workshop's equipment: what is in each container, who took it, what changed, and
what is left over. Two halves in one repository:

| Half | What it is | Runs on |
| --- | --- | --- |
| `IMCAPI/` | ASP.NET Core 9 API over SQLite, EF Core, ASP.NET Identity + JWT bearer | `http://localhost:5173/api` |
| `imcui/` | Quasar (Vue 3 + TypeScript) PWA that talks to that API | `http://localhost:9000` |

The same features work on the desktop and on a phone: the tree is a two-pane view with its own
scrollbars on a wide screen, and a list plus a full-height sheet below that breakpoint.

---

## 1. What you need

| Tool | Version | Why |
| --- | --- | --- |
| .NET SDK | **9.0** (verified with 9.0.320) | builds `IMCAPI` and `Testing` |
| Node.js | **>= 26, ^24 or ^22.12** (`imcui/package.json` `engines`) | runs the Quasar dev server, builds the PWA |
| npm | 10+ | `npm ci` |
| EF Core CLI | 9.x (`dotnet ef --version`) | applying migrations by hand |
| SQLite CLI | optional | looking at the database without the API |

Nothing else. There is no Docker, no external database and no message broker.

---

## 2. Quick start

```bash
git clone https://github.com/dyeggoM/InventoryManagementContainers.git
cd InventoryManagementContainers

# 1. the API (it needs a signing key and a bootstrap superadmin the first time)
export AUTH_JWT_KEY="a-signing-key-of-at-least-32-bytes-long"
export AUTH_JWT_ISSUER="imc.local"
export AUTH_BOOTSTRAP_EMAIL="admin@imc.local"
export AUTH_BOOTSTRAP_PASSWORD="ChangeMe!12345"
dotnet run --project IMCAPI

# 2. the UI, in another terminal
cd imcui
npm ci
npm run dev
```

Open <http://localhost:9000>, sign in with the bootstrap account, and that is the app running.

Both halves at once, listening on every interface so a phone on the same network can use them:

```bash
./scripts/dev-lan.sh          # API on 0.0.0.0:5173, dev server on 0.0.0.0:9000
./scripts/dev-lan.sh api      # only the API
./scripts/dev-lan.sh ui       # only the dev server
```

The script prints the LAN URL and the account to use. Its default password is a test value: it only
creates the account when the database has no superadmin, so on an existing database it is ignored.

### First login

The seeder creates **exactly one** `superadmin` from `Auth:Bootstrap:*` (or
`AUTH_BOOTSTRAP_EMAIL` / `AUTH_BOOTSTRAP_PASSWORD`) and refuses to start if the count is later
anything other than one. Every other account is created from the dashboard: *Dashboard → Usuarios →
Nuevo usuario*, which returns a one-time `initialPassword`; or with
`POST /api/users` and `POST /api/users/{id}/password` for a reset.

Roles, in the order they may act: `superadmin` > `admin` > `operador` > `lector`. What each route
requires is not a convention: it is `SECURITY_AUTH_MATRIX.md`, and `Testing/AuthMatrixTests.cs`
executes that table.

> The local dev database (`IMCAPI/imc.db`) already has an `admin@imc.local` account. Its password is
> deliberately **not in this file**: reset it from *Usuarios → Contraseña*, or delete `IMCAPI/imc.db`
> and let the seeder build a fresh one from `AUTH_BOOTSTRAP_*`.

---

## 3. Configuration

Every key can be set in `IMCAPI/appsettings.json`, as an environment variable with `__` in place of
the colon (`Auth__Jwt__Key`), or — for the ones listed — with the environment name in the right
column. Environment wins over the file.

| Key | Environment variable | Default | What it does |
| --- | --- | --- | --- |
| `Auth:Jwt:Key` | `AUTH_JWT_KEY` | — (**required**) | signing key, at least 32 UTF-8 bytes; the API refuses to start without it |
| `Auth:Jwt:Issuer` | `AUTH_JWT_ISSUER` | — (**required**) | token issuer, validated on every request |
| `Auth:Bootstrap:Email` | `AUTH_BOOTSTRAP_EMAIL` | — (**required on an empty database**) | the superadmin the seeder creates |
| `Auth:Bootstrap:Password` | `AUTH_BOOTSTRAP_PASSWORD` | — (**required on an empty database**) | its password, first run only |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | `Data Source=./imc.db` | SQLite file, relative to the API's working directory |
| `Media:Root` | `Media__Root` | the API's content root | where attachments are written (`media/items`, `media/trash`) |
| `Items:MaxDepth` | `ITEMS_MAX_DEPTH` | `12` | deepest the item tree may go; create, move and restore all enforce it |
| `CORS:AllowedCors` | `CORS__AllowedCors` | `http://localhost:9000,https://localhost:9000` | comma-separated origins, or `*`. Development reflects any origin; **outside Development a pin is required** |
| `Auth:Enforce` | `Auth__Enforce` | `true` | `false` turns every role policy into a no-op. It is a **local diagnostic**, refused outside Development |
| `Auth:RateLimit:PermitLimit` | `Auth__RateLimit__PermitLimit` | `10` | `/api/auth/*` calls per window per address |
| `Auth:RateLimit:WindowSeconds` | `Auth__RateLimit__WindowSeconds` | `60` | length of that window; the answer is `429` |
| `Archive:Retention:Days` | `ARCHIVE_RETENTION_DAYS` | `0` (keep forever) | age at which a purge may drop archive rows |

Access tokens live 15 minutes and refresh tokens 7 days (rotated, stored hashed, revocable). Those
values are in `AuthController`, not configuration.

---

## 4. Running it

### API

```bash
dotnet run --project IMCAPI                  # http://localhost:5173, Swagger at /swagger in Development
```

It applies pending migrations and seeds roles on start. In Development the Swagger UI is on
<http://localhost:5173/swagger>.

### UI

```bash
cd imcui
npm ci          # installs and runs `quasar prepare` (postinstall)
npm run dev     # http://localhost:9000; listens on 0.0.0.0 so a phone can reach it
npm run build   # production build in imcui/dist/spa
npx quasar build -m pwa   # installable build in imcui/dist/pwa (manifest + service worker)
```

The UI derives the API address from the page's own hostname (`window.location.hostname:5173/api`),
so opening the app as `http://192.168.1.20:9000` talks to `http://192.168.1.20:5173/api` without any
per-machine configuration. `API_BASE_URL` in `imcui/src/services/api.ts` overrides it.

### Database

SQLite, one file: `IMCAPI/imc.db` (ignored by git). It is created, migrated and seeded on first run.

```bash
dotnet ef database update --project IMCAPI                              # apply migrations by hand
dotnet ef migrations add <Name> --project IMCAPI --output-dir Migrations
dotnet ef database drop --project IMCAPI                               # throw the data away
```

`dotnet ef` runs the API's own startup to find the `DbContext`, so **`AUTH_JWT_KEY` (≥ 32 bytes) and
`AUTH_JWT_ISSUER` must be set for these commands too**, exactly as for `dotnet run`; without them the
tool reports *"Unable to create a 'DbContext'"*, which is the startup validation talking, not a
database problem.

`IMCAPI/DB/DatabaseSeeder.cs` creates the four roles and the single superadmin, then enforces the
"exactly one superadmin" invariant **on every start**.

### Files

Attachments are written outside the database, under `Media:Root`:

- `media/items/{itemId}/{file}` — the live originals;
- `media/trash/items/{itemId}/{file}` — what a deleted record's files become.

The row keeps the relative path plus a small JPEG preview. Deleting a record moves its files to the
trash, restoring it moves them back, and only an archive purge destroys bytes. Uploads are capped at
20 MB and the extension comes from an allow-list, re-detected from the content when the name lies.

---

## 5. Build and test

```bash
# API
dotnet build IMCAPI/IMCAPI.csproj
dotnet test Testing/Testing.csproj          # 134 tests: auth matrix, auth abuse, cycles, attachments,
                                            # archive, audit, import (xlsx + csv), rate limiting

# UI
cd imcui
npm run test        # vitest — 207 tests in 21 files
npm run typecheck   # vue-tsc
npm run lint:check  # prettier + eslint, no writes (npm run lint fixes)
npm run build       # the production SPA in imcui/dist/spa (`quasar build -m pwa` for the PWA)
```

**Build the `.csproj` files, not `IMCAPI.sln`**: the solution references `imcui/imcui.esproj`, which
is not committed, so a solution build fails on a fresh clone.

The tests run the real API over a throwaway SQLite database and a temporary media folder (`ApiFactory`),
with migrations, seeding and backfills — the same path a fresh clone takes.

---

## 6. Importing stock from a spreadsheet

Two calls, by design: look first, then write.

1. `POST /api/import/preview` — multipart `file` (`.xlsx`, `.xls` or `.csv`), admin. Answers the tree
   the file describes with **every row classified** and the reason, and writes **nothing**:
   - **green** — import as-is;
   - **yellow** — importable, but worth a look (brand outside the catalogue, no code, description too
     short, a subcategory spelling that was corrected);
   - **red** — never imported (no description, or a code repeated in the file — both rows of a
     duplicate go red).
2. `POST /api/import` — same file plus `includeYellow` (default `true`) and `skipCodes` (one per code
   the user unticked). Writes inside **one transaction**, skips what is already there, and is
   idempotent: the same file twice creates nothing the second time.

The shape it understands is the one the workshop already keeps: **sheet → root of the tree**,
`SUB CATEGORIA` → the level under it, **one row → one record**, with `Brand`, `Code`, `Reference`,
`Serial`, `Notes` and `Dimensions` stored in the record's attributes. A code is optional but, when
present, must be unique across the whole inventory (an application check, not a database constraint —
the code lives in the JSON attributes and the schema was deliberately not changed for it).

The same thing with a screen: *Dashboard → Importar desde Excel* renders that tree in its three
colours, filters to the doubtful rows, lets you untick the yellow ones, and says how many records the
button will create.

---

## 7. Project layout

```
IMCAPI/            ASP.NET Core 9 API
  Controllers/     routes: items, attachments, constants, users, audit, logs, import
  DB/              DbContext, entities, repositories, seeder, migrations
  Services/        auth/tokens, attachments + trash, audit interceptor, importer (xlsx + csv)
Testing/           NUnit integration tests over a throwaway database
imcui/             Quasar PWA
  src/pages/       inventory, configuration, dashboard
  src/components/  tree master-detail, attribute/attachment editors, dashboard panels
  src/stores/      auth, theme, inventory state
  src/services/    api client and one module per endpoint group
scripts/           dev-lan.sh (both halves on the LAN)
docs/security/     the audit that started the auth work
```

---

## 8. Where the documentation lives

| File | Read it for |
| --- | --- |
| `AGENTS.md` | the working rules: inventory-first, one concern per branch, the commit gate |
| `CHANGELOG.md` | what changed, per pull request |
| `FEATURES.md` | what the product does today, and which module implements it |
| `ROADMAP.md` | what is planned, in order, and what is deliberately not built |
| `SECURITY_AUTH_MATRIX.md` | which role each route requires (executed by the test suite) |
| `AI_INVENTORY.md` | a router index over the code: modules, symbols, `path:line` |
| `docs/security/` | the original audit and its findings |

`AI_INVENTORY.md` is regenerated by the pre-commit hook; it is a map, not a duplicate of the code.

---

## 9. Security notes

- **The token carries the identity, never the body.** `CreatedBy`, `UpdatedBy` and `Id` are written
  server-side.
- **The audit trail is append-only**, written at the persistence layer by an EF interceptor, readable
  only by `superadmin`, with secrets redacted and no write endpoint.
- **Impersonation is by role, never by account**: a 7-minute read-only token that keeps the real
  subject in `sub` and records `act`, never renewable, never targeting `superadmin`.
- **Rate limiting** covers `/api/auth/*` only (10 per minute per address by default).
- **Commit gate.** `.githooks/pre-commit` asks `security_matrix.py` before a commit and refuses while
  a finding is open; a review is consumed by the commit that carries it. Enable it once per clone:

  ```bash
  git config core.hooksPath .githooks
  ```

  A clone without the security tooling warns and lets the commit through. `git commit --no-verify`
  skips it for one commit.

---

## 10. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `dotnet build` fails on `IMCAPI.sln` | the solution references the uncommitted `imcui/imcui.esproj`. Build `IMCAPI/IMCAPI.csproj` and `Testing/Testing.csproj` |
| The API will not start: *Configure Auth:Jwt:Key* | the signing key is mandatory; set `AUTH_JWT_KEY` (≥ 32 bytes) |
| *Exactly one superadmin account must exist* | the database has zero or two. Delete `IMCAPI/imc.db` to reseed, or fix the extra account's role |
| Login answers `401` with the bootstrap password | the database already had a superadmin, so the bootstrap value was ignored. Reset that account's password |
| `429` on `/api/auth/login` | the rate limit: 10 attempts per minute per address. Wait a window or raise `Auth:RateLimit:PermitLimit` |
| The UI loads but every call fails | the API is not on `<same host>:5173`, or the origin is not in `CORS:AllowedCors` outside Development |
| The phone cannot reach the app | both halves must bind `0.0.0.0`: `./scripts/dev-lan.sh` does; `dotnet run` alone does not |
| Commit blocked by the security gate | `python3 ~/.hermes/security-tools/security_matrix.py --repo . check` says which finding; see `AGENTS.md` § *The security gate* |

---

## Licence

MIT — see [LICENSE](LICENSE).
