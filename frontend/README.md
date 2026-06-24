# ORBIT Frontend — source-built image (BRANDED)

Builds `orbit-frontend-patched` from the **ORBIT-branded** Speckle fork source:
`CheekiSkrub/speckle-server-dev` (read-only) at branch **`rebus-dev`**, pinned by
commit in `UPSTREAM_COMMIT` / the `ORBIT_FRONTEND_SOURCE_COMMIT` build arg.

> IMPORTANT: this MUST build from `rebus-dev` (or a `REBUS-v*` tag), which carries
> the ORBIT branding (`orbit_logo.png`, the "ORBIT" wordmark, the Version/Login
> panels) and the custom viewer (`setup.ts`, object-sidebar labels, geometry/
> material converters). Building from the fork's `main` branch produces STOCK
> upstream Speckle with **no ORBIT branding** — that was the cause of the
> "it looks like Speckle again" regression.

## Build (VM or any Docker host)

```sh
cd /opt/orbit/server
docker compose build orbit-frontend
docker compose up -d --no-deps orbit-frontend
```

On deploy, the org self-hosted runner (`self-hosted`, `Linux`, `X64`) SSHs to VM 211
and runs `scripts/deploy.sh`, which builds `orbit-frontend` from this context.
The first build can take 20–40 minutes.

## Source pin

The branded source commit is recorded in `UPSTREAM_COMMIT`. Override at build time:

```sh
ORBIT_FRONTEND_SOURCE_COMMIT=<rebus-dev commit> docker compose build orbit-frontend
```

## Viewer overlays + patches (applied)

`overlays/` and `patches/` are applied on top of the branded source by the
`Dockerfile`, and are verified to apply cleanly against the pinned `rebus-dev`
commit:

- `patches/camera-default.patch` — models open front-facing (azimuth 5.498).
- `tree.ts` overlay — `@elements` layer-collection children render in the model
  tree (and no spurious "elements Array Collection" node). This reproduces the
  prod `v2.4.9` `build-patched.sh` `@elements` behaviour at source level.
- expand-all selection data (`Object.vue` / `Sidebar.vue` / `panelExpand.ts`).
- instance-proxy mesh tree (`tree.ts`).
- datum gizmo toggle (`controls/Right.vue` + `ui.ts` + `DatumGizmoExtension.ts`),
  rendered via the same `ObjectLayers.PROPS` path as `SectionTool` so the gizmo
  actually draws in Speckle's render pipeline.

If you bump `ORBIT_FRONTEND_SOURCE_COMMIT`, re-verify the patches apply (a fork
sync can move the patched lines) before deploying.

## Legacy minified patch path

`../patches/orbit-frontend/build-patched.sh` layers minified-bundle hotfixes onto a
prebuilt GHCR image via `docker commit`. Superseded by this branded source build.
