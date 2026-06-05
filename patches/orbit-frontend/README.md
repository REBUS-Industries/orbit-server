# ORBIT Frontend — patched derivative image

Layers a single tweak onto `ghcr.io/rebus-orbit/orbit-frontend`: the **default
viewer camera** is rotated ~180° in azimuth so models open facing the **front**
instead of the rear 3/4 corner.

## What changes

The Speckle viewer's `CameraController.default()` calls
`SmoothOrbitControls.setOrbit(2.356, 0.955)` (azimuth ≈ 135°, the rear 3/4
isometric). We rewrite the azimuth to `5.498` rad (≡ −0.785 rad, the opposite /
front-facing corner). The polar/elevation (`.955`) is unchanged. `5.498` is the
same byte length as `2.356`, so the identity bundle size is preserved.

## Why a script (`build-patched.sh`) instead of a Dockerfile

The base image's layers have been **garbage-collected from GHCR**, so BuildKit
can no longer rebuild `FROM ghcr.io/rebus-orbit/orbit-frontend:<ver>` (export
fails with `could not fetch content descriptor … not found`). The layers still
exist in the VM's **local** Docker image store (the running container uses
them), so we derive the patched image with `docker commit` instead.

The viewer code is a content-hashed client chunk (`_nuxt/entry.*.js`), but nitro
serves `/_nuxt` from a build-time manifest in
`server/chunks/nitro/nitro.mjs` and prefers the precompressed `.br`/`.gz`
sibling. So the script rewrites the `.js`, regenerates `.br`/`.gz` (using the
image's own node — zlib brotli), and updates all three manifest `etag`+`size`
entries (etag = `"<sizeHex>-<sha1_base64[:27]>"`, the standard `etag` package
format; the algorithm is verified against the existing manifest before any
edit).

## Deploy

```sh
cd /opt/orbit/server
ORBIT_FRONTEND_VERSION=v2.4.9 sh patches/orbit-frontend/build-patched.sh
docker compose up -d --no-deps orbit-frontend
```

The `orbit-frontend` service in `docker-compose.yml` references the result:

```yaml
  orbit-frontend:
    image: orbit-frontend-patched:${ORBIT_FRONTEND_VERSION:-latest}
    pull_policy: never
```

On a frontend version bump, edit `ORBIT_FRONTEND_VERSION` in `.env`, then re-run
the build script and `up -d --no-deps orbit-frontend`.

## Caveat: cached clients

Because the chunk filename hash is unchanged but its bytes changed, and Nuxt
serves `_nuxt/*` with `Cache-Control: immutable`, browsers that already cached
the old chunk keep the old camera until a hard refresh or the next real frontend
version bump (which changes the hash). New sessions get the fix immediately.

## Revert

Restore the `orbit-frontend` service to `image: ghcr.io/rebus-orbit/orbit-frontend:${ORBIT_FRONTEND_VERSION}`
(remove `pull_policy: never`) and `docker compose up -d --no-deps orbit-frontend`.