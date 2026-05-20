# ORBIT Server — patched derivative image.
#
# Thin layer on top of the upstream `ghcr.io/rebus-orbit/orbit-server` image
# that bakes in ORBIT-specific runtime patches.  All patches are compiled JS
# files copied directly over the original `dist/` files — no rebuild needed.
#
# To bump the base version, edit `ORBIT_SERVER_BASE_VERSION` in the parent
# `docker-compose.yml`; the build arg flows through here.

ARG ORBIT_SERVER_BASE_VERSION=latest
FROM ghcr.io/rebus-orbit/orbit-server:${ORBIT_SERVER_BASE_VERSION}

# Patch 1 — getUserStreamsQueryBaseFactory: new `serverAdminBypass` option
#   that skips the StreamAcl-required join filter so the project listing
#   returns every project to a server admin.
COPY streams.js  /speckle-server/packages/server/dist/modules/core/repositories/streams.js

# Patch 2 — projects resolver: detects `server:admin` + ADMIN_OVERRIDE_ENABLED
#   and forwards `serverAdminBypass: true` into the three getUserStreams* calls
#   that back the `activeUser.projects` GraphQL field.
COPY projects.js /speckle-server/packages/server/dist/modules/core/graph/resolvers/projects.js
