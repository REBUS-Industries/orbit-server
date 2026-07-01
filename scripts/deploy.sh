#!/usr/bin/env bash
# Manual deploy helper — run on the VM directly
set -euo pipefail

# After syncing to origin/main, re-exec so we run the updated script (bash loads
# the file once at start). Use reset --hard so local edits on the VM (e.g. a
# manual patch attempt) cannot block deploy.
if [[ -z "${ORBIT_DEPLOY_REEXEC:-}" ]]; then
  STACK_DIR="$(dirname "$0")/.."
  cd "$STACK_DIR"
  git fetch origin main
  git reset --hard origin/main
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
# Rebuild when patch inputs change; otherwise reuse the cached image.
# Full rebuild needs a local ghcr.io/rebus-orbit/orbit-server base (no GHCR auth).
# Otherwise try an incremental COPY-only authz patch, or continue with the
# existing patched image so deploy is not blocked.
SERVER_IMAGE="orbit-server-patched:${ORBIT_SERVER_VERSION:-latest}"
BASE_IMAGE="ghcr.io/rebus-orbit/orbit-server:${ORBIT_SERVER_VERSION:-latest}"
PATCH_REV="$(git log -1 --format=%H -- \
  patches/orbit-server/Dockerfile \
  patches/orbit-server/*.js \
  patches/orbit-server/*.cjs)"
STATE_DIR="$(dirname "$STACK_DIR")/.orbit-deploy-state"
STATE_FILE="${STATE_DIR}/server-patch-rev"
PATCH_CTX="${STACK_DIR}/patches/orbit-server"
mkdir -p "${STATE_DIR}"

docker_cp_if_exists() {
  local cid=$1 remote=$2 local=$3
  docker cp "${cid}:${remote}" "${local}" 2>/dev/null
}

find_and_copy_authz() {
  local cid=$1 basename=$2 dest_local=$3
  shift 3
  local remote
  for remote in "$@"; do
    if docker_cp_if_exists "${cid}" "${remote}" "${dest_local}"; then
      printf '%s' "${remote}"
      return 0
    fi
  done
  return 1
}

build_orbit_server() {
  echo "Building ${SERVER_IMAGE} (patches/orbit-server at ${PATCH_REV})"
  if docker image inspect "${BASE_IMAGE}" >/dev/null 2>&1; then
    if docker build \
      --pull=false \
      -f "${PATCH_CTX}/Dockerfile" \
      --build-arg "ORBIT_SERVER_BASE_VERSION=${ORBIT_SERVER_VERSION:-latest}" \
      -t "${SERVER_IMAGE}" \
      "${PATCH_CTX}"; then
      return 0
    fi
    echo "Note: full orbit-server rebuild failed (will try incremental or existing image)"
  else
    echo "Note: no local ${BASE_IMAGE}; skipping full orbit-server rebuild"
  fi

  if docker image inspect "${SERVER_IMAGE}" >/dev/null 2>&1; then
    if apply_incremental_authz_patch; then
      return 0
    fi
    echo "Using existing ${SERVER_IMAGE} without new server patches"
    return 2
  fi

  echo "ERR: no ${SERVER_IMAGE} and could not build one (GHCR login or cached base required)" >&2
  return 1
}

apply_incremental_authz_patch() {
  local tmp cid can_remote saved_remote
  tmp="$(mktemp -d)"

  local -a can_candidates=(
    "/speckle-server/packages/shared/dist/authz/policies/project/savedViews/canCreate.js"
    "/speckle-server/packages/server/node_modules/@speckle/shared/dist/authz/policies/project/savedViews/canCreate.js"
    "/speckle-server/node_modules/@speckle/shared/dist/authz/policies/project/savedViews/canCreate.js"
  )
  local -a saved_candidates=(
    "/speckle-server/packages/shared/dist/authz/fragments/savedViews.js"
    "/speckle-server/packages/server/node_modules/@speckle/shared/dist/authz/fragments/savedViews.js"
    "/speckle-server/node_modules/@speckle/shared/dist/authz/fragments/savedViews.js"
  )

  cid="$(docker create "${SERVER_IMAGE}")"
  can_remote="$(find_and_copy_authz "${cid}" canCreate "${tmp}/canCreate.js" "${can_candidates[@]}")" || can_remote=""
  saved_remote="$(find_and_copy_authz "${cid}" savedViews "${tmp}/savedViews.js" "${saved_candidates[@]}")" || saved_remote=""
  docker rm "${cid}" >/dev/null

  if [[ -z "${can_remote}" || -z "${saved_remote}" ]]; then
    echo "WARN: saved-views authz JS not found in ${SERVER_IMAGE}; skipping authz patch"
    rm -rf "${tmp}"
    return 1
  fi

  echo "Patching saved-views authz from ${can_remote} and ${saved_remote}"
  docker run --rm \
    -v "${tmp}:/work:rw" \
    -v "${PATCH_CTX}/patch-saved-views-authz.cjs:/patch.cjs:ro" \
    node:22-alpine \
    sh -c 'mkdir -p /authz/policies/project/savedViews /authz/fragments && \
      cp /work/canCreate.js /authz/policies/project/savedViews/canCreate.js && \
      cp /work/savedViews.js /authz/fragments/savedViews.js && \
      SPECKLE_ROOT=/authz node /patch.cjs && \
      cp /authz/policies/project/savedViews/canCreate.js /work/canCreate.js && \
      cp /authz/fragments/savedViews.js /work/savedViews.js'

  cat > "${tmp}/Dockerfile" <<EOF
ARG PATCHED_BASE=${SERVER_IMAGE}
FROM \${PATCHED_BASE}
COPY canCreate.js ${can_remote}
COPY savedViews.js ${saved_remote}
EOF

  docker build -f "${tmp}/Dockerfile" -t "${SERVER_IMAGE}" "${tmp}"
  rm -rf "${tmp}"
}

if [[ -f "${STATE_FILE}" ]] && [[ "$(cat "${STATE_FILE}")" == "${PATCH_REV}" ]] \
  && docker image inspect "${SERVER_IMAGE}" >/dev/null 2>&1; then
  echo "Using existing ${SERVER_IMAGE} (patches/orbit-server unchanged at ${PATCH_REV})"
else
  build_result=0
  build_orbit_server || build_result=$?
  if ! docker image inspect "${SERVER_IMAGE}" >/dev/null 2>&1; then
    echo "ERR: ${SERVER_IMAGE} is not available after build" >&2
    exit 1
  fi
  if [[ "${build_result}" -eq 0 ]]; then
    echo "${PATCH_REV}" > "${STATE_FILE}"
  elif [[ "${build_result}" -eq 2 ]]; then
    echo "Note: deploy continues; server patch state unchanged (will retry on next deploy)"
  else
    exit 1
  fi
fi

# ── ORBIT frontend (CUSTOM branded build) ───────────────────────────────────
# Build the ORBIT-branded frontend from the BRANDED fork source (rebus-dev) via
# ./frontend/Dockerfile. This is NOT stock Speckle. First build can take
# 20–40 min. We do NOT pass --pull so cached node/distroless base layers are
# reused even when GHCR is unauthenticated.
docker compose build orbit-frontend

# Recover from interrupted deploys (duplicate/stuck orbit-server container names).
docker compose stop orbit-server orbit-frontend 2>/dev/null || true
docker compose rm -f orbit-server orbit-frontend 2>/dev/null || true

docker compose up -d --remove-orphans --pull never
echo "Deploy complete: $(date)"
