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

# Patch 3 — serverInvites resolver: make the project-invite `use` mutation
#   idempotent. Apollo fires multiple useProjectInvite calls per "Accept"
#   click; the first deletes the invite row + adds the user to stream_acl,
#   the duplicates land on the now-empty row and throw InviteNotFoundError
#   "Attempted to finalize nonexistant invite". The patch swallows that
#   specific error when the user does already have effective access to
#   the project (explicit member, or server:admin with ADMIN_OVERRIDE_ENABLED).
COPY serverInvites.js /speckle-server/packages/server/dist/modules/serverinvites/graph/resolvers/serverInvites.js

# Patch 4 — @speckle/shared authz: allow Saved Views on non-workspace projects
#   (self-hosted unlock; no-op if the base image already includes allowUnworkspaced).
COPY patch-saved-views-authz.cjs /tmp/patch-saved-views-authz.cjs
RUN node /tmp/patch-saved-views-authz.cjs && rm /tmp/patch-saved-views-authz.cjs
