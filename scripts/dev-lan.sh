#!/usr/bin/env bash
# Start both halves of the app so another machine on the LAN can use them.
#
#   ./scripts/dev-lan.sh            # API on :5173 and dev server on :9000, both on 0.0.0.0
#   ./scripts/dev-lan.sh api        # only the API
#   ./scripts/dev-lan.sh ui         # only the dev server
#
# Why a script: the two defaults that make remote testing work are easy to lose in a config file
# (the API's launch profile has to bind 0.0.0.0, the dev server has to listen on every interface),
# and the API refuses to start without a signing key and a bootstrap superadmin. The credentials
# below are for a local test box only: override them from the environment for anything else.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
what="${1:-both}"

# Test-only values. Nothing here is a secret worth keeping: the database is the throwaway
# IMCAPI/imc.db and the account exists so you can log in from the other machine.
export AUTH_JWT_KEY="${AUTH_JWT_KEY:-dev-lan-signing-key-at-least-32-bytes-long}"
export AUTH_JWT_ISSUER="${AUTH_JWT_ISSUER:-imc.dev.lan}"
export AUTH_BOOTSTRAP_EMAIL="${AUTH_BOOTSTRAP_EMAIL:-admin@imc.local}"
export AUTH_BOOTSTRAP_PASSWORD="${AUTH_BOOTSTRAP_PASSWORD:-ChangeMe!12345}"
export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

lan_ip="$(ip -4 addr show scope global | awk '/inet /{print $2}' | cut -d/ -f1 | head -1)"

start_api() {
  echo "API      -> http://${lan_ip:-<host-ip>}:5173/api   (binds 0.0.0.0:5173, profile 'http')"
  echo "           login: ${AUTH_BOOTSTRAP_EMAIL} / ${AUTH_BOOTSTRAP_PASSWORD}"
  dotnet run --project "$repo_root/IMCAPI" --no-launch-profile \
    --urls "http://0.0.0.0:5173" &
}

start_ui() {
  echo "Dev app  -> http://${lan_ip:-<host-ip>}:9000/     (binds 0.0.0.0:9000)"
  ( cd "$repo_root/imcui" && npx quasar dev --host 0.0.0.0 --port 9000 )
}

trap 'kill 0' INT TERM EXIT
case "$what" in
  api) start_api; wait ;;
  ui)  start_ui ;;
  both)
    start_api
    sleep 5
    start_ui
    ;;
  *) echo "usage: $0 [both|api|ui]" >&2; exit 2 ;;
esac
