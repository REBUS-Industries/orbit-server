# ORBIT Server — Runtime Patches

This directory builds a thin Docker image that layers ORBIT-specific
compiled-JS patches on top of the upstream `ghcr.io/rebus-orbit/orbit-server`
image. The patched image is built locally on the deploy VM via the
`build:` directive in the parent `docker-compose.yml`, so changes here
survive `docker compose pull` and don't require a separate registry.

## How it works

`docker-compose.yml` `orbit-server` service:

```yaml
orbit-server:
  build:
    context: ./patches/orbit-server
    args:
      ORBIT_SERVER_BASE_VERSION: ${ORBIT_SERVER_VERSION:-latest}
  image: orbit-server-patched:${ORBIT_SERVER_VERSION:-latest}
```

On `docker compose up -d`:
1. Compose pulls `ghcr.io/rebus-orbit/orbit-server:${ORBIT_SERVER_VERSION}`
2. Compose builds a derivative image, `COPY`-ing the patched files over the
   originals in `/speckle-server/packages/server/dist/...`
3. The container starts from the derivative tag

To pick up a new upstream version, just bump `ORBIT_SERVER_VERSION` in
`.env` and re-run `docker compose up -d --build orbit-server`. If a patch
needs updating to track upstream changes, regenerate it from the new base
image (see workflow below).

## Active patches

### `streams.js` + `projects.js` — server-admin sees all projects

When `ADMIN_OVERRIDE_ENABLED=true`, server admins can already open any
project URL directly, but the upstream `activeUser.projects` GraphQL
resolver still filters the *Projects list* by explicit `StreamAcl` role,
so admins only see projects they were invited to.

This pair of patches:

- `dist/modules/core/repositories/streams.js` —
  `getUserStreamsQueryBaseFactory` accepts a new `serverAdminBypass`
  option that skips the `whereNotNull(StreamAcl.col.role)` filter and
  the implicit-access workspace logic. All other filters (search,
  workspace, `withRoles`, `streamIdWhitelist`) still apply.

- `dist/modules/core/graph/resolvers/projects.js` —
  `activeUser.projects` (and its `Count` siblings) compute
  `serverAdminBypass = (ctx.role === Roles.Server.Admin) && adminOverrideEnabled()`
  and forward it to the three `getUserStreams*` calls.

Net effect: with `ADMIN_OVERRIDE_ENABLED=true`, server admins see every
project on the server in their Projects list. Non-admin users behave
exactly as before.

## Regenerating a patch after an upstream bump

```bash
# On the VM (or any host with Docker):
TMP=$(mktemp -d)
docker run --rm --entrypoint cat \
  ghcr.io/rebus-orbit/orbit-server:NEW_VERSION \
  /speckle-server/packages/server/dist/modules/core/repositories/streams.js \
  > "$TMP/streams.js"
# ...edit $TMP/streams.js with the same patch...
cp "$TMP/streams.js" patches/orbit-server/streams.js
git commit -m "patches: re-apply serverAdminBypass on NEW_VERSION"
```

Always diff against the previous version of the file in this repo before
shipping — upstream may have refactored neighbouring lines.
