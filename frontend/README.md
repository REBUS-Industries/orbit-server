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

## Parked: viewer feature overlays

`overlays/` and `patches/` hold work-in-progress viewer features (expand-all
selection data, instance-proxy mesh tree, datum gimbal). They are **not applied**
by the current `Dockerfile` — they were written against stock `main` and the datum
gizmo is not yet working. They will be re-derived against the branded `rebus-dev`
source and re-enabled in a separate, verified PR. Do not wire them back into the
build until then.

## Legacy minified patch path

`../patches/orbit-frontend/build-patched.sh` layers minified-bundle hotfixes onto a
prebuilt GHCR image via `docker commit`. Superseded by this branded source build.
