# ORBIT Preview Service — source-built image

Builds `orbit-preview-patched` from the same ORBIT-branded Speckle fork and viewer
patches as `./frontend/Dockerfile`.

## Why

Preview thumbnails are rendered headlessly by `packages/preview-service`, which bundles
the same `@speckle/viewer` converter as the web viewer. Block placements shipped as
`InstanceProxy` with geometry in detached `@instanceDefinitionGeometry` require the
ORBIT viewer patches to render:

- `viewer-displayable-lookup.patch` — normalise single-object `displayValue`
- `viewer-instanced-line-transform.patch` — bake transforms for instanced lines/curves
  in scaled blocks (e.g. mm→m insert scale 0.001)
- `viewer-instance-definition-exclusion.patch` — index detached definition geometry
  for instancing instead of expecting members in the world tree

The legacy GHCR `ghcr.io/rebus-orbit/orbit-preview` image does not include these
patches, so instanced blocks such as "Steel Mast" can be missing from commit preview
PNGs even though the live viewer (built from `./frontend`) renders them correctly.

## Build

```sh
cd /opt/orbit/server
docker compose build orbit-preview
docker compose up -d --no-deps orbit-preview
```

First build can take 15–30 minutes (viewer + preview-frontend compile).

## Cache bust

Existing preview PNGs are cached. After deploying a new preview image, delete cached
previews for affected versions or re-send the model to regenerate thumbnails.
