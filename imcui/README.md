# IMCUI (imcui)

## Run locally

Two processes: the API and this UI.

```bash
# 1. API  — http://localhost:5173 (routes under /api)
export DOTNET_ROOT="$HOME/.dotnet" && export PATH="$DOTNET_ROOT:$PATH"
dotnet run --project ../IMCAPI                # local only
dotnet run --project ../IMCAPI --launch-profile lan   # reachable from the LAN

# 2. UI   — http://localhost:9000
cp .env.example .env      # optional: QCLI_API_URL=http://localhost:5173/api
npm install
npm run dev
```

Without `QCLI_API_URL` the UI calls `http://<the host serving the page>:5173/api`, so opening
`http://<lan-ip>:9000` from another machine works without editing `.env`. In Development the API
accepts any origin (CORS is pinned to `CORS:AllowedCors` only outside Development); changing the
Quasar dev port therefore no longer breaks it, but `--launch-profile lan` is required for the API to
listen on anything other than loopback.

The API creates `IMCAPI/imc.db` on first run: migrations build the schema and, when the constant
table is empty, `IMCAPI/DB/Seed/seed.sql` fills the configuration tree (attribute names, attachment
types, categories, brands…). The database is git-ignored — inventory items are user data and are
never seeded. To start over, stop the API and delete the file.

Attachments keep the uploaded file on disk under `IMCAPI/media/items/{itemId}/`; the row stores the
relative path plus a compressed preview (JPEG, longest side 320px) in `ThumbnailBase64`, so lists
stay small. `GET /api/attachment/{id}/file` serves the original. Rows written before this change
carry the whole image inline; they are moved to disk on the next start.

## Code inventory

`AI_INVENTORY.md` is committed: one line per symbol (`path:line`, purpose, callers) so an assistant
can find what exists and what a change would affect without reading the whole repository.

It is generated, never hand-edited. A pre-commit hook refreshes it from the staged code, so the
document in a diff always matches the code in the same commit:

```bash
git config core.hooksPath .githooks    # once per clone
```

The hook needs the `code-inventory` skill (it looks in `~/.hermes/skills/software-development/`;
override with `CODE_INVENTORY_HOME`). Without it the hook warns and lets the commit through.
`git commit --no-verify` skips it for one commit. Regenerate by hand with:

```bash
python3 "$CODE_INVENTORY_HOME/scripts/inventory.py" sync --root . --lang js
python3 "$CODE_INVENTORY_HOME/scripts/inventory.py" sync --root IMCAPI --lang cs
python3 "$CODE_INVENTORY_HOME/scripts/ai_inventory.py" --root . --no-stamp
```

## Tests

```bash
npm test          # Vitest + jsdom: the tree editor's state machine, the attribute editor's rows
npm run typecheck # vue-tsc
npm run lint:check
```

Components mount with Quasar's own components stubbed, so no dev server or browser is needed.

## Branches

Merged branches accumulate. `scripts/branches.sh` lists every branch — local and remote — whose work
is already in `origin/main`, and removes them with a flag:

```bash
scripts/branches.sh                  # dry run: what is safe to delete
scripts/branches.sh --delete         # asks once, then deletes locally and on the remote
scripts/branches.sh --delete --yes   # no prompt, for scripts
```

GitHub can handle the remote half itself: **Settings → General → “Automatically delete head
branches”**. It only applies to pull requests merged after it is switched on, so the script still
covers everything older.

## Install the dependencies

```bash
npm install
```

### Start the app in development mode (HMR, error reporting, etc.)

```bash
quasar dev
```

### Format & Lint the files

```bash
npm run lint
```

...or just check formatting & linting:

```bash
npm run lint:check
```

### Build the app for production

```bash
quasar build
```

### Customize the configuration

See [Configuring quasar.config.js](https://v2.quasar.dev/quasar-cli-vite/quasar-config-file).
