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
- fixture custom display name (`lib/object-sidebar/helpers.ts`): the selected-object
  label prefers a PRISM fixture's pretty `displayName` over the canonical `name`
  (rule `displayName ?? name`), reading `displayName` / `properties.displayName` /
  `metadata.displayName`. Falls back to `name` for non-fixtures and old models.
  See prism-fixtures-service `docs/handoffs/DISPLAY_NAME_ORBIT.md`.
- `patches/named-views-view3d.patch` — restore the ORBIT connector's **named
  views** (Rhino named views, sent as `Objects.BuiltElements.View.View3D` inline
  under the root collection's `views`) in the viewer's **Camera Controls** menu.
  The branded `setup.ts` only built `metadata.views` for the newer V3
  `Objects.Other.Camera` shape; this patch extends the same root-object
  (`/objects/{stream}/{id}/single`) fetch to also build `SpeckleView`s from
  `View3D` entries (using their exact inline `origin`/`target`), and de-dupes
  `metadata.views` by id so root-fetched coordinates win over any in-tree copy
  whose coords were stripped during load. No connector/SDK change or model
  re-send is required — the camera menu UI (`camera/Menu.vue`) and
  `viewer.getViews()` already support View3D; the gap was reliable population.

If you bump `ORBIT_FRONTEND_SOURCE_COMMIT`, re-verify the patches apply (a fork
sync can move the patched lines) before deploying. `named-views-view3d.patch`
anchors on the `rootViews` loop and the `views.value = [...]` assignment in
`lib/viewer/composables/setup.ts`; if a sync moves those, re-extract the patch.

### ORBIT brand theme

`overlays/packages/tailwind-theme/src/plugin.ts` overrides Speckle's theme tokens
with the ORBIT palette (from the ORBIT portal): orange primary `#E06238`
(hover `#CF4C20`), orange focus/outline, and portal-matched dark backgrounds
(`--foundation-page #0D0D0D`, `--foundation #141414`). `overlays/.../core/composables/theme.ts`
defaults the app to the **dark** theme (light only when explicitly chosen), to
match the portal. Speckle has no colour theme in its source — these tokens are the
single source of brand colour, so adjust them here to retune ORBIT colours.

## Legacy minified patch path

`../patches/orbit-frontend/build-patched.sh` layers minified-bundle hotfixes onto a
prebuilt GHCR image via `docker commit`. Superseded by this branded source build.
