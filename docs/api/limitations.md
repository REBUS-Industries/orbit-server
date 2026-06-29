# Limitations

Known constraints for ORBIT API integrators (REBUS production instance, 2026-06).

## Comments

- **GraphQL only** — no REST routes for create/read/update/delete comments.
- **No SDK helpers** — `orbit-sdk` covers projects/models/versions; comments require raw GraphQL.
- **`viewerState` is complex** — no published JSON schema; copy from UI-created comments or accept list-only comments without viewer pins.
- **`resourceIdString` format** — comma-delimited viewer encoding; not fully documented outside the viewer codebase. Inspect network traffic when creating pins in the UI.
- **ProseMirror required** — plain-string `text` is not supported on modern `CreateCommentInput`; use `content.doc`.
- **Blob attachments** — separate upload step; blob API not covered in this docs v1 (use browser devtools or upstream Speckle file-upload docs).
- **Legacy `data` bag** — unstructured JSON on deprecated API; avoid for new work.

## Viewers

- **No `@speckle/viewer` guide** — ORBIT does not ship or support the upstream Speckle viewer npm package. See [Building a 3rd party viewer](building-a-3rd-party-viewer) for the receive pipeline, or PRISM UE streaming for portal embeds.
- **`viewerState` / `resourceIdString`** — also listed under Comments above; required for custom viewers that show pins.

## Documentation & discovery

- **`/docs`** — this site (ORBIT-specific). Previously returned 404 until the `orbit-docs` service was added to the stack.
- **`/explorer`** — not deployed on ORBIT; use `/graphql` (Apollo Sandbox) or this docs site.
- **Apollo Studio** — public Speckle schema reference applies conceptually but field names differ (ORBIT uses `project` not `app.speckle.systems` branding).

## Server patches

ORBIT-specific runtime patches (`patches/orbit-server/`) affect:

- Server admin project listing (`serverAdminBypass`)
- Idempotent invite acceptance

Patches do **not** change comment behaviour. `allowPublicComments` is standard upstream project metadata.

## Deployment

- Docs require `docker compose up -d --build orbit-docs` on VM 211 after pulling repo changes.
- Docs are **not** in the upstream GHCR server image; they ship from this repo's `docs-server` build context.
- External HA proxy (LXC 251/252) needs no change — `/docs` routes through internal Caddy on port 80.

## Auth & access

- Private server — `inviteOnly` may be enabled; automated tests need valid PATs and project membership.
- OAuth app registration is limited to approved connector app IDs for PKCE flows.

## Upstream drift

ORBIT server version bumps (`ORBIT_SERVER_VERSION` in `.env`) can add or deprecate GraphQL fields. Re-run introspection against `/graphql` after major upgrades. This documentation was validated by introspection on `https://orbit.rebus.industries/graphql`.

## Out of scope for this API docs site

- PRISM REST API → `https://prism.rebus.industries/docs`
- Connector plugin APIs (Rhino/UE5)
- Webhook payload formats (see upstream Speckle webhook docs)
- File import service internals

## Reporting gaps

If schema behaviour diverges from this documentation, compare live introspection with `docs/api/` sources and update the markdown in the `orbit-server` repo.
