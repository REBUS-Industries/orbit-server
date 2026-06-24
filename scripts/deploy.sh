#!/usr/bin/env bash
# Manual deploy helper — run on the VM directly
set -euo pipefail

# After git pull, re-exec so we run the updated script (bash loads the file once at start).
if [[ -z "${ORBIT_DEPLOY_REEXEC:-}" ]]; then
  STACK_DIR="$(dirname "$0")/.."
  cd "$STACK_DIR"
  git pull origin main
  export ORBIT_DEPLOY_REEXEC=1
  exec bash "$0" "$@"
fi

STACK_DIR="$(dirname "$0")/.."
cd "$STACK_DIR"

# Optional GHCR auth (set GHCR_TOKEN or NUGET_TOKEN in .env on the VM)
if [[ -f .env ]]; then
  # shellcheck disable=SC1091
  source .env
fi
if [[ -n "${GHCR_TOKEN:-${NUGET_TOKEN:-}}" ]]; then
  echo "${GHCR_TOKEN:-${NUGET_TOKEN}}" | docker login ghcr.io -u "${GHCR_USER:-REBUS-ORBIT}" --password-stdin
fi

# Public upstream images; orbit-server/frontend/docs are built locally
docker compose pull postgres redis minio caddy webhook-service fileimport-service

# GHCR images may be private — pull when logged in, otherwise keep local cache
for svc in orbit-preview prism; do
  docker compose pull "$svc" || echo "Note: could not pull ${svc} (using local image if present)"
done

SERVER_IMAGE="orbit-server-patched:${ORBIT_SERVER_VERSION:-latest}"
if docker image inspect "${SERVER_IMAGE}" >/dev/null 2>&1; then
  echo "Using existing ${SERVER_IMAGE} (skip rebuild; set ORBIT_SERVER_VERSION or prune image to force)"
else
  docker compose build orbit-server
fi

docker compose build orbit-frontend
docker compose up -d --remove-orphans
docker image prune -f
echo "Deploy complete: $(date)"
