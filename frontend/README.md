# ORBIT Frontend — source-built image

Builds `orbit-frontend-patched` from the Speckle FE2 source (read-only upstream
`CheekiSkrub/speckle-server-dev` at a pinned commit) plus ORBIT overlay patches in
`overlays/`.

## Features in this overlay

1. **Expand all selection data** — the right-hand "Selected" panel can expand
   object arrays (e.g. `@instanceDefinitionProxies object array (4)`) and provides
   Expand all / Collapse controls in the sidebar header.
2. **Instance proxy meshes in layer tree** — WorldTree children under instance
   proxies (transform → instanced meshes) appear in the left Models panel and are
   individually selectable.
3. **Datum gimbal toggle** — viewer control (Axis3D icon, right toolbar) shows a
   read-only transform gizmo at the selection datum (instance transform matrix or
   selection bbox centre).

## Build (VM or any Docker host)

```sh
cd /opt/orbit/server
docker compose build orbit-frontend
docker compose up -d --no-deps orbit-frontend
```

On deploy, CI runs `docker compose build --pull orbit-server orbit-frontend` after
`git pull` (see `.github/workflows/deploy.yml`).

## Upstream pin

Commit hash is recorded in `UPSTREAM_COMMIT`. Override at build time:

```sh
SPECKLE_UPSTREAM_COMMIT=d3854d45a23649b50d3a2ce3c4868de211769bda docker compose build orbit-frontend
```

## Legacy minified patch path

`patches/orbit-frontend/build-patched.sh` remains for hotfixes on prebuilt GHCR
images when a full source rebuild is not practical. New viewer features should land
here in `frontend/overlays/` instead.
