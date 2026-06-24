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

# Load .env (ORBIT_FRONTEND_VERSION, optional GHCR_TOKEN, etc.)
if [[ -f .env ]]; then
  # shellcheck disable=SC1091
  source .env
fi
if [[ -n "${GHCR_TOKEN:-${NUGET_TOKEN:-}}" ]]; then
  echo "${GHCR_TOKEN:-${NUGET_TOKEN}}" | docker login ghcr.io -u "${GHCR_USER:-REBUS-ORBIT}" --password-stdin
fi

# Public upstream images; orbit-server/frontend/docs are built locally
docker compose pull postgres redis minio caddy webhook-service fileimport-service

# GHCR preview image — best effort when logged in, otherwise use local cache
docker compose pull orbit-preview || echo "Note: could not pull orbit-preview (using local image if present)"

# ── ORBIT server (patched) ──────────────────────────────────────────────────
# Keep the cached patched image if present (private GHCR base needs auth to pull).
SERVER_IMAGE="orbit-server-patched:${ORBIT_SERVER_VERSION:-latest}"
if docker image inspect "${SERVER_IMAGE}" >/dev/null 2>&1; then
  echo "Using existing ${SERVER_IMAGE} (skip rebuild; prune image or set ORBIT_SERVER_VERSION to force)"
else
  docker compose build orbit-server
fi

# ── ORBIT frontend (CUSTOM branded build) ───────────────────────────────────
# Build the ORBIT-branded frontend from the BRANDED fork source (rebus-dev) via
# ./frontend/Dockerfile. This is NOT stock Speckle. First build can take
# 20–40 min. We do NOT pass --pull so cached node/distroless base layers are
# reused even when GHCR is unauthenticated.
docker compose build orbit-frontend

docker compose up -d --remove-orphans --pull never
echo "Deploy complete: $(date)"
