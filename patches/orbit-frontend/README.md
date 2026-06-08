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

---

# Pending patch recipe — Decision C: parent model row federates submodels

> Status: NOT YET APPLIED. This is a recipe for an operator on a Docker-capable
> host (e.g. VM 211) to finish, because the exact minified literals must be
> extracted from the deployed bundle. No functional change has been made to
> `build-patched.sh` for this yet.

## Intent

Nested models already exist (the hierarchy is the slash-delimited model name,
e.g. `test/sub1/sub2`), and the FE2 model-list folder **"View all"** button
already opens the federated `$<fullName>` view of all descendant submodels.

Decision C makes the **parent/group row's own name + preview links** federate
too: clicking a parent that `hasChildren` should open all its submodels, not
just that single model. This mirrors the existing `viewAllUrl` the "View all"
button uses (prefix `$`, encode `/` as `%2F`).

## Source-level change (what the minified patch must reproduce)

In `packages/frontend-2/components/project/page/models/StructureItem.vue` the
two primary links currently bind to `modelLink` (the single-model route). The
change adds a `primaryLink` computed and points both `:to` bindings at it:

```ts
// alongside the existing viewAllUrl / modelLink computeds:
const primaryLink = computed(() =>
  hasChildren.value ? viewAllUrl.value : modelLink.value
)
```

```diff
- <NuxtLink :to="modelLink || undefined">   <!-- name link -->
+ <NuxtLink :to="primaryLink || undefined">
- <NuxtLink :to="modelLink || ''">          <!-- preview / thumbnail link -->
+ <NuxtLink :to="primaryLink || ''">
```

`viewAllUrl` already exists in the component and is built as
`modelRoute(projectId, ('$' + fullName).replace(/\//g, '%2F'))`.

## Step 1 — extract + locate the literals (on a Docker host)

The render code lives in a content-hashed lazy chunk (chunk filenames keep the
component name). Pull `_nuxt` from the base image and locate the construct; the
`%2F` federation encoder is a near-unique anchor for `viewAllUrl`:

```sh
VER=v2.4.9
docker create --name fe-x ghcr.io/rebus-orbit/orbit-frontend:$VER >/dev/null
docker cp fe-x:/speckle-server/public/_nuxt ./_nuxt && docker rm fe-x >/dev/null

ls _nuxt | grep -i StructureItem                       # the StructureItem chunk
grep -o '.\{160\}%2F.\{60\}' _nuxt/StructureItem*.js    # mangled ref for viewAllUrl
grep -o 'to:[^,}]\{0,40\}'   _nuxt/StructureItem*.js    # the two to: bindings (modelLink)
grep -o '.\{40\}hasChildren.\{40\}' _nuxt/StructureItem*.js  # if name survives; else trace the ref
```

From that output identify the mangled refs for `viewAllUrl` (call it `V`),
`modelLink` (`M`), and `hasChildren` (`H`), and the exact two `to:` binding
substrings.

## Step 2 — add a `patches[]` entry to `build-patched.sh`

Append one entry per distinct `to:` binding to the `patches` array (illustrative
shape — REPLACE `M`/`V`/`H` and the exact surrounding bytes with the verified
literals from Step 1):

```js
// Patch N: parent (hasChildren) model rows federate submodels via "$<fullName>"
{ old: 'to:M.value||void 0', new: 'to:(H.value?V.value:M.value)||void 0' },
{ old: 'to:M.value||""',     new: 'to:(H.value?V.value:M.value)||""'     },
```

The build script enforces correctness for you: it `find`s the chunk that
contains each `old` literal and **exits non-zero if the literal isn't found**,
verifies the chunk's `etag` against the existing manifest before editing,
regenerates `.br`/`.gz`, and rewrites the manifest `etag`+`size` (so a
size-changing patch is fine — it need not be length-neutral). If an `old`
literal is wrong or stale, the build fails loudly rather than shipping a
mis-patch.

## Step 3 — build + deploy (on VM 211)

```sh
cd /opt/orbit/server
ORBIT_FRONTEND_VERSION=v2.4.9 sh patches/orbit-frontend/build-patched.sh
docker compose up -d --no-deps orbit-frontend
```

Then update the "Patches applied" comment in `build-patched.sh` and the top of
this README to list Decision C as applied, and commit those changes.

## Caveat: re-extract on every frontend version bump

This is a **minified-bundle** patch keyed on build-specific mangled identifiers.
On any `orbit-frontend` version bump the literals change, the `old` strings stop
matching, and the build script fails loud — at which point the literals must be
**re-extracted** (repeat Step 1) and the `patches[]` entries updated.

The **durable alternative** is to make Decision C a *source* change
(`primaryLink` computed + the two `:to` rewrites above) compiled into a
**source-built `orbit-frontend` image in REBUS-Industries**, rather than a
bundle patch. That removes the per-version fragility entirely and is the
recommended long-term home for this change. The minified patch above is the
quick, in-place option until that source build exists.