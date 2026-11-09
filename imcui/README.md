# IMCUI (imcui)

## Run locally

Two processes: the API and this UI.

```bash
# 1. API  — http://localhost:5173 (routes under /api)
export DOTNET_ROOT="$HOME/.dotnet" && export PATH="$DOTNET_ROOT:$PATH"
dotnet run --project ../IMCAPI

# 2. UI   — http://localhost:9000
cp .env.example .env      # QCLI_API_URL=http://localhost:5173/api
npm install
npm run dev
```

The API allows exactly `http://localhost:9000` and `https://localhost:9000` (see
`IMCAPI/appsettings.Development.json`), so if you change the Quasar dev port you must change CORS
too — otherwise every request fails as an opaque network error. `QCLI_API_URL` is required at
startup: `src/services/api.ts` throws if it is missing.

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
