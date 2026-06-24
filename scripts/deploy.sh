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
# Regenerate orbit-frontend-patched from the pristine BRANDED base image via
# docker commit. This is the real ORBIT frontend — NEVER a stock-Speckle source
# rebuild. We locate a usable branded base (any local tag first, then a GHCR
# pull when authenticated) and apply the camera/@elements micro-patches; if the
# patches fail we retag the base so the branded UI is still restored.
FE_VER="${ORBIT_FRONTEND_VERSION:-v2.4.9}"
FE_TARGET="orbit-frontend-patched:${FE_VER}"

echo "── Frontend images currently on this host ──"
docker images --format '  {{.Repository}}:{{.Tag}}  {{.ID}}  {{.Size}}' \
  | grep -iE 'orbit-frontend|speckle-frontend|rebus|frontend-patched' || echo "  (none found)"

# Candidate branded base images, in priority order.
FE_CANDIDATES=(
  "ghcr.io/rebus-orbit/orbit-frontend:${FE_VER}"
  "ghcr.io/cheekiskrub/orbit-frontend:${FE_VER}"
  "speckle-frontend-2-rebus:${FE_VER}"
)
FE_BASE=""
for cand in "${FE_CANDIDATES[@]}"; do
  if docker image inspect "$cand" >/dev/null 2>&1; then FE_BASE="$cand"; echo "Found local branded base: $cand"; break; fi
done
if [[ -z "$FE_BASE" ]]; then
  for cand in "ghcr.io/rebus-orbit/orbit-frontend:${FE_VER}" "ghcr.io/cheekiskrub/orbit-frontend:${FE_VER}"; do
    echo "Attempting to pull branded base $cand ..."
    if docker pull "$cand" >/dev/null 2>&1; then FE_BASE="$cand"; echo "Pulled branded base: $cand"; break; fi
  done
fi

if [[ -n "$FE_BASE" ]]; then
  if ORBIT_FRONTEND_VERSION="${FE_VER}" ORBIT_FRONTEND_BASE="${FE_BASE}" ORBIT_FRONTEND_TARGET="${FE_TARGET}" \
       sh patches/orbit-frontend/build-patched.sh; then
    echo "Built branded ${FE_TARGET} from ${FE_BASE} (camera/@elements patches applied)"
  else
    echo "WARN: build-patched.sh failed — retagging pristine branded base ${FE_BASE} as ${FE_TARGET}"
    docker tag "${FE_BASE}" "${FE_TARGET}"
  fi
else
  echo "ERROR: No branded ORBIT frontend base image found locally, and none could be pulled from GHCR." >&2
  echo "       A previous deploy replaced the cached branded image with a build and pruned the old one." >&2
  echo "       To restore the custom ORBIT frontend, do ONE of:" >&2
  echo "         1) Add a GHCR read token to /opt/orbit/server/.env so it can be pulled:" >&2
  echo "              NUGET_TOKEN=<PAT with read:packages>   (optionally GHCR_USER=<github-user>)" >&2
  echo "            then re-run the deploy; or" >&2
  echo "         2) Restore a saved image and tag it ${FE_TARGET}:" >&2
  echo "              docker load -i orbit-frontend-${FE_VER}.tar && docker tag <loaded> ${FE_TARGET}" >&2
  echo "       Refusing to ship a non-branded (stock Speckle) frontend." >&2
  exit 1
fi

docker compose up -d --remove-orphans --pull never
echo "Deploy complete: $(date)"
