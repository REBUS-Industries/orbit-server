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

### `serverInvites.js` — idempotent project-invite acceptance

The Speckle frontend fires multiple `useProjectInvite` GraphQL mutations
per "Accept invite" click (Apollo refetch racing with the user click).
The first call deletes the `server_invites` row and adds the user to
`stream_acl`; the duplicates land on the now-empty row and throw
`InviteNotFoundError("Attempted to finalize nonexistant invite")`. The
duplicate error then surfaces as the user-visible "Couldn't process
project invite" toast even though the membership grant succeeded.

Both `ProjectInviteMutations.use` and `streamInviteUse` resolvers are
wrapped: when `InviteNotFoundError` fires AND the calling user already
has effective access to the project (explicit `stream_acl` member, or
`server:admin` while `ADMIN_OVERRIDE_ENABLED=true`), the error is
swallowed and the mutation returns `true`. Any other failure mode
(truly bogus token, mismatched email, downstream `processInvite`
failure) re-throws normally so legitimate problems still surface.

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

### `patch-saved-views-authz.cjs` — Saved Views on self-hosted (no workspace)

Speckle Cloud gates **Saved Views** (the viewer panel for saving camera,
visibility, section-box, etc.) behind workspace plan features. ORBIT projects
are not in a workspace, so the API must pass `allowUnworkspaced: true` when
checking `WorkspacePlanFeatures.SavedViews` in `@speckle/shared` authz.

The rebus-dev fork source already includes this tweak; the patched server
image applies it at build time to the compiled JS under
`packages/shared/dist/authz/…` if the base `ghcr.io/rebus-orbit/orbit-server`
image does not. The script is a **no-op** when `allowUnworkspaced: true` is
already present.

Also required (set in `docker-compose.yml` / `.env`):

- `FF_SAVED_VIEWS_ENABLED=true` on **orbit-server**
- `NUXT_PUBLIC_FF_SAVED_VIEWS_ENABLED=true` on **orbit-frontend** (and
  `FF_SAVED_VIEWS_ENABLED=true` at frontend **build** time — see
  `frontend/Dockerfile`)

Do **not** enable `FF_WORKSPACES_MODULE_ENABLED` on self-hosted ORBIT — it
historically broke login. Saved Views does not require the workspaces module.

### `Dockerfile.incremental` — patch without re-pulling the GHCR base

When `patches/orbit-server` changes but the VM cannot authenticate to GHCR,
`scripts/deploy.sh` tries to locate saved-views authz JS under
`packages/shared/dist/…` or `node_modules/@speckle/shared/dist/…`, patch with
`node:22-alpine` on the host, and build a COPY-only layer (no `RUN`). If authz
files are missing or GHCR is unavailable, deploy continues with the existing
`orbit-server-patched` image rather than failing.

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
