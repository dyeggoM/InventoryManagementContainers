# Working rules for this repository

Read this before changing anything. It is written for the agent team that works here (and for any
human who joins), and it is short on purpose: the details live in the docs it points to.

## What this is

Inventory of containers for a workshop. Two halves:

- `IMCAPI/` — ASP.NET Core 9 API over SQLite, EF Core, ASP.NET Identity + JWT bearer.
- `imcui/` — Quasar (Vue 3 + TypeScript) PWA that talks to that API. It has the same features on
  web and mobile, so a change that only works on desktop is half a change.

Documentation is part of the codebase:
`CHANGELOG.md` (what changed), `FEATURES.md` (what the product does today),
`ROADMAP.md` (what is planned, in order), `SECURITY_AUTH_MATRIX.md` (which route requires which
role), `AI_INVENTORY.md` (generated map of the code, refreshed by the pre-commit hook).

## Read the inventory before you read the code

`AI_INVENTORY.md` is a **router index**: it names each module, its size and its main symbols, and
links to one file per module under `inventory/`. It exists so a change does not start with a repo
walk. This is not advice, it is the required order of work:

1. **Open `AI_INVENTORY.md` first.** Find the module that owns the change, open **only** that slice.
   Loading every slice is the same mistake as reading the whole repository.
2. **Fetch a range, not a file.** Symbols carry `path:line`: read that range plus ~10 lines. Opening a
   700-line controller to look at one method is how a cheap change becomes an expensive one.
3. **Reuse before writing.** If the symbol, route or DTO already exists, extend it. A second
   implementation of the same concern is a defect, not a shortcut.
4. **Read the Impact line before changing a signature.** Every caller listed goes in the same change,
   including callers in another module (they carry their own `path:line`).
5. **Never paste the inventory or a whole source file into the conversation as "context".** Cite
   `path:line` and read what the change needs; anything quoted into the prompt is re-sent on every
   later turn. This is measurable: a session that ignored the inventory made 332 API calls and read
   `Controllers.cs` 30 times for one change.
6. **One symbol, one question?** Ask the CLI instead of a document:
   `python3 <skill>/scripts/ai_inventory.py --root . --context SYMBOL_NAME --budget 8000`.
7. **Absence is not proof.** Dynamic dispatch, reflection and generated code are not indexed. Grep
   before concluding that something is unused.

Who owns what: `IMCAPI/**` and `Testing/**` are the back-end bot's; `imcui/**` is the front-end bot's;
nobody edits another bot's directories. Security reviews are read-only.

Regenerate the map (the `code-inventory` skill does this automatically in its pre-commit hook):

```bash
python3 <skill>/scripts/inventory.py sync --root . --lang cs --full
python3 <skill>/scripts/inventory.py sync --root . --lang js --full
python3 <skill>/scripts/ai_inventory.py --root . --max-chars 40000 --split auto
```

The split flags matter: without them the document is a single 130 KB file that no agent can afford to
open, and the whole point is lost.

## Branches and pull requests

- **`main` is always deployable. Nothing is committed to it directly.** Every change lands through a
  pull request, including a one-line fix and including documentation.
- Branch from `main`, name it for the change: `feat/<thing>`, `fix/<thing>`, `docs/<thing>`,
  `test/<thing>`, `chore/<thing>`.
- One concern per branch and per pull request. If you find an unrelated bug on the way, fix it in its
  own branch.
- The pull request body states **what changed and how it was verified**, with the real command output
  (`dotnet test`, `npm test`, a measured status code). "Should work" is not a verification.
- Head branches are **deleted automatically when the pull request merges** — the repository flag
  `delete_branch_on_merge` is on, so do not reuse a merged branch name for new work.
- `CHANGELOG.md` gains its entry in the same pull request that changes the behaviour, under
  `Pending — <branch>`. When the pull request merges, that heading becomes its number and date.
- A pull request that touches authentication, authorization or the audit trail also updates
  `SECURITY_AUTH_MATRIX.md`. If a new route appears, it is either authenticated or declared in
  `endpoints.allow-anonymous.txt` with a written reason — the contract test fails otherwise.

## Build, test, verify

```bash
# API
dotnet build IMCAPI/IMCAPI.csproj
dotnet test Testing/Testing.csproj          # 57 tests: matrix, auth abuse, cycles, attachments
dotnet run --project IMCAPI                 # needs AUTH_JWT_KEY/AUTH_JWT_ISSUER
                                            # and AUTH_BOOTSTRAP_EMAIL/AUTH_BOOTSTRAP_PASSWORD

# Frontend
cd imcui && npm ci
npm run test        # vitest
npm run typecheck   # vue-tsc
npm run lint:check
npm run build
```

Build the `.csproj` files, not `IMCAPI.sln`: the solution references an `imcui.esproj` that is not
committed, so a solution build fails on a fresh clone.

## Security invariants (do not weaken these)

- **Authorization is by decision, not by habit.** `SECURITY_AUTH_MATRIX.md` says which role each
  route needs; `Testing/AuthMatrixTests.cs` executes that table. Widening a policy is a security
  change and belongs in a pull request that says so.
- **Identity comes from the token, never from the body.** `UpdatedBy`, `CreatedBy` and `Id` are
  written server-side; a request that echoes a client-supplied actor is the A-2 finding coming back.
- **The audit trail is append-only.** It is written at the persistence layer
  (`SaveChangesInterceptor`), it is read only by `superadmin`, and it has no write endpoint. Secrets
  (`PasswordHash`, `SecurityStamp`, tokens) are redacted before serialization.
- **Impersonation never forges identity**: a short-lived token with an actor claim, read-only by
  default, unable to reach user or role management.
- **Soft delete is a one-way door today.** `HasQueryFilter(IsActive)` hides rows from every query,
  which is why cycle guards and the delete path use `IgnoreQueryFilters()` when they must see them.
- **Never trust a path from the client**: validate the shape (`TryParseExact`, regex) and confine the
  result to its directory, whatever the proxy in front does.

## How work is routed here

Four agents work in this repository, one orchestrator and three specialists:

- **Code Orchestrator** — decomposes work, assigns it, keeps `CHANGELOG.md`/`FEATURES.md`/`ROADMAP.md`
  truthful, and is the only one who talks to everybody.
- **OpenCode Coder** — implementation, usually `imcui/` and mechanical backend work.
- **OpenAI Coder** — implementation with more reasoning behind it, usually `IMCAPI/` and the tests.
- **Unfiltered** — security reviewer. It reads, reproduces and reports; it never edits code.

Coders do not talk to each other: every dependency, correction and follow-up goes through the
orchestrator. Reviews from Unfiltered arrive at the orchestrator as findings with a route to a fix.

## Language

Code, comments, commit messages, branch names and pull request bodies in English. Conversation with
the owner, Diego, in Spanish.
