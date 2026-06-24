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
# Regenerate orbit-frontend-patched from the pristine branded base image
# (ghcr.io/rebus-orbit/orbit-frontend:<ver>) via docker commit. This is the
# real ORBIT frontend — NEVER a stock-Speckle source rebuild. If the minified
# micro-patches fail to apply, fall back to the pristine branded base so the
# ORBIT-branded UI is still restored.
FE_VER="${ORBIT_FRONTEND_VERSION:-v2.4.9}"
FE_BASE="ghcr.io/rebus-orbit/orbit-frontend:${FE_VER}"
FE_TARGET="orbit-frontend-patched:${FE_VER}"
if ORBIT_FRONTEND_VERSION="${FE_VER}" sh patches/orbit-frontend/build-patched.sh; then
  echo "Built branded ${FE_TARGET} (with camera/@elements patches)"
elif docker image inspect "${FE_BASE}" >/dev/null 2>&1; then
  echo "WARN: build-patched.sh failed — falling back to pristine branded base ${FE_BASE}"
  docker tag "${FE_BASE}" "${FE_TARGET}"
else
  echo "ERROR: cannot build branded frontend and base image ${FE_BASE} is missing on this host." >&2
  echo "       Refusing to ship a non-branded frontend. Pull/restore ${FE_BASE} and re-run." >&2
  exit 1
fi

docker compose up -d --remove-orphans --pull never
docker image prune -f
echo "Deploy complete: $(date)"
