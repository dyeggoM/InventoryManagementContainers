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
