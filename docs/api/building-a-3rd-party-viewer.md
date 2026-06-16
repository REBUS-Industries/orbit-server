# Building a 3rd party viewer

This guide explains how to fetch an ORBIT version's object graph over HTTP/GraphQL and render it in your own application — without using the bundled ORBIT web frontend.

It synthesises the receive pipeline used by PRISM Visualiser, the Rhino/UE5 connectors, and the `orbit-sdk` transport layer. All endpoint shapes were verified against `https://orbit.rebus.industries` (2026-06).

---

## Overview

ORBIT stores 3D data as a **content-addressed object tree**. A **version** (commit) points at the root object's SHA-256 hash. Viewers must:

1. Resolve version metadata (GraphQL).
2. Download every object in the tree (REST, one object per request).
3. Resolve detached references and texture blobs.
4. Convert geometry into your renderer's format.

### When to build your own viewer

| Approach | Best for |
|---|---|
| **Custom viewer (this guide)** | Full UI control, custom selection/highlighting, embedding in a proprietary portal, offline/desktop renderers, game engines |
| **PRISM UE Pixel Streaming** | Highest visual fidelity, lighting, large models on a GPU workstation — see [PRISM Portal Integration](https://github.com/REBUS-Industries/prism/blob/main/docs/PORTAL_INTEGRATION.md) |
| **ORBIT web frontend (iframe)** | Quick embed with comments, pins, and layer sidebar already built — historical option; frontend ships as a prebuilt Docker image only (`orbit-frontend`), not as an npm SDK |

There is **no official `@speckle/viewer` package maintained for ORBIT**. The upstream [Speckle viewer docs](https://docs.speckle.systems/developers/viewer/overview) describe the same wire format conceptually, but ORBIT field names and deployment differ — treat them as reference, not a drop-in SDK.

---

## Prerequisites

### Authentication

All object and version fetches require a valid Bearer token. See [Authentication](authentication).

- **Scripts / server-side viewers:** Personal Access Token (PAT) with at least `streams:read` (legacy scope name; applies to projects).
- **Desktop connectors:** OAuth2 Authorization Code + PKCE — not typical for web viewers.

```bash
curl -s https://orbit.rebus.industries/graphql \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_PAT" \
  -d '{"query":"{ activeUser { id name } }"}'
```

### Project access

The caller must have **read** access on the target project (`projectId`). Private projects require membership or a valid invite. See [Projects, models & versions](projects-models-versions) and [Subscriptions & permissions](subscriptions-permissions).

You need three identifiers to open a version:

| ID | Where to get it |
|---|---|
| `projectId` | Project URL, GraphQL `project(id:)` query, or connector send dialog |
| `modelId` | Model/branch within the project |
| `versionId` | Specific commit on that model |

---

## Receive pipeline (step by step)

The canonical flow mirrors `OrbitReceivePipeline` in PRISM Visualiser and the connector receive path:

```
GraphQL  →  version.referencedObject (root hash)
REST     →  GET root object (+ optional __closure prefetch)
REST     →  BFS over { referencedId } stubs until all objects fetched
REST     →  GET texture blobs referenced as @blob:HASH
Convert  →  walk Collection tree, render Mesh / displayValue fallbacks
```

### Step 1 — Resolve the version

There is **no REST endpoint for version metadata**. Use GraphQL:

```graphql
query Version($projectId: String!, $versionId: String!) {
  project(id: $projectId) {
    version(id: $versionId) {
      id
      referencedObject
      message
      createdAt
      model { id }
      authorUser { id name }
    }
  }
}
```

```bash
curl -s https://orbit.rebus.industries/graphql \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_PAT" \
  -d '{
    "query": "query($projectId: String!, $versionId: String!) { project(id: $projectId) { version(id: $versionId) { id referencedObject model { id } } } }",
    "variables": { "projectId": "PROJECT_ID", "versionId": "VERSION_ID" }
  }'
```

The `referencedObject` field is the **root object hash** — your entry point for REST downloads.

To discover `modelId` and available versions:

```graphql
query($projectId: String!, $modelId: String!) {
  project(id: $projectId) {
    model(id: $modelId) {
      id
      name
      versions(limit: 20) {
        items { id message referencedObject createdAt }
      }
    }
  }
}
```

### Step 2 — Download the root object

Production ORBIT uses the `/single` suffix (same as `orbit-sdk` `ServerTransport`):

```
GET /objects/{projectId}/{objectId}/single
Authorization: Bearer YOUR_PAT
```

```bash
curl -s "https://orbit.rebus.industries/objects/PROJECT_ID/ROOT_OBJECT_HASH/single" \
  -H "Authorization: Bearer YOUR_PAT"
```

Returns the full JSON body for one object. The root typically includes a `__closure` map (see below) listing every descendant id and depth — useful for progress bars and prefetch planning.

> **Note:** Some upstream Speckle docs describe `GET /objects/{projectId}/{objectId}` without `/single`. On ORBIT production, integrators should use the `/single` path. The [Objects (REST)](objects) page documents the upstream variant; this guide reflects live server behaviour.

### Step 3 — Traverse the object graph

Large child objects are **detached**: the parent holds a stub instead of inline JSON:

```json
{ "referencedId": "abc123def456...", "speckle_type": "reference" }
```

Walk the tree breadth-first (or depth-first):

1. Parse each downloaded object JSON.
2. Scan all properties recursively for `referencedId` values.
3. Fetch any id not yet downloaded via `GET /objects/{projectId}/{id}/single`.
4. Repeat until no new ids appear.

PRISM Visualiser deduplicates in-flight requests and caps concurrency at **8 parallel fetches**. The same object id may appear from multiple parents — fetch each id at most once.

**Alternative using `__closure`:** The root object's `__closure` field is a flat `{ "objectId": depth }` map of every descendant. You can prefetch all ids from closure instead of walking references — both approaches arrive at the same object set. Reference-walking is more resilient when closure is missing on older payloads.

### Step 4 — Resolve texture blobs

`Objects.Other.RenderMaterial` bodies reference textures as `@blob:HASH` placeholders (or bare server-assigned ids after upload). Download binaries via:

```
GET /api/stream/{projectId}/blob/{blobId}
Authorization: Bearer YOUR_PAT
```

```bash
curl -s "https://orbit.rebus.industries/api/stream/PROJECT_ID/BLOB_ID" \
  -H "Authorization: Bearer YOUR_PAT" \
  -o texture.png
```

ORBIT blob ids are **10-character server-assigned strings**, not SHA-256 content hashes. Do not integrity-check them against file content — the server id is authoritative.

Collect blob hashes by scanning all `RenderMaterial` objects (detached and inline on meshes) for texture fields: `diffuseTexture`, `baseColorTexture`, `emissiveTexture`, `normalTexture`, etc.

### Step 5 — Convert for your renderer

Once all objects and blobs are local, walk the tree from the root:

- **Collections** → recurse into referenced children (skip detached `RenderMaterial` children — index them separately).
- **`Objects.Geometry.Mesh`** → primary render primitive (see [Rendering contract](#rendering-contract)).
- **Other types with `displayValue`** → render the baked mesh fallbacks (Breps, curves, BIM elements).
- **Unknown types** → log and skip, or render `displayValue` if present.

Apply coordinate/unit scaling from the root object's `units` field (`mm`, `cm`, `m`, `in`, `ft`).

---

## Object model essentials

ORBIT objects are JSON documents compatible with the Speckle serialisation format. Key fields:

| Field | Purpose |
|---|---|
| `id` | SHA-256 content hash (also the URL path segment). May be omitted from the body — stamp it from the request path. |
| `speckle_type` | Fully qualified type discriminator (e.g. `Objects.Geometry.Mesh`). Historical payloads may use `type` instead. |
| `name` | Human label — often a layer path from Rhino (`Layer::SubLayer`). |
| `__closure` | Root only — flat map of `{ descendantId → depth }`. |
| `referencedId` | Detached child stub — fetch the real object separately. |
| `displayValue` | Array of inline meshes or mesh reference stubs — viewer fallback for non-mesh types. |
| `elements` | Collection children (often `@elements` in serialised form) — array of reference stubs. |
| `collectionType` | Semantic hint on collections: `model`, `layer`, `block`, etc. — drives layer-tree labels in the ORBIT frontend. |
| `units` | Source document units on the root collection. |
| `applicationId` | Stable id from the source app (Rhino GUID) — use for round-trip / update matching. |

### Detachment

Objects larger than ~1 KB are stored as separate entries. Parents retain `{ "referencedId": "..." }` tokens. Large meshes and Breps are always detached. Your viewer **must** resolve references — inlining via `childrenDepth` query params is unreliable for full fidelity on production.

### Content hashing

Each object's `id` is computed from its serialised JSON (excluding `id` and `__closure`). Identical geometry deduplicates automatically across sends.

### Proxies (materials, groups, blocks)

Connectors attach **proxy objects** at the root alongside the main tree:

| Proxy type | Purpose |
|---|---|
| `RenderMaterialProxy` | Material definition + list of object ids that use it |
| `ColorProxy` | ARGB colour override + object id list |
| `GroupProxy` | Named selection group |
| `DefinitionProxy` | Block definition geometry for `Instance` placements |

Proxies reference objects by `applicationId`, not content hash. A minimal mesh-only viewer can ignore proxies; a full-featured viewer should apply material and instance transforms.

---

## Rendering contract

These rules match the connector send path and PRISM Visualiser converters. Deviating from them produces missing geometry or wrong materials.

### Collections

Two `speckle_type` values appear in the wild:

- `Speckle.Core.Models.Collections.Collection`
- `Objects.Other.Collections.Collection`

Children live in `elements` (serialised as `@elements`) as reference stubs. Use `collectionType` for sidebar grouping (`layer`, `model`, `block`, …) — it does not change geometry.

### Mesh (`Objects.Geometry.Mesh`)

| Field | Format |
|---|---|
| `vertices` | Flat `[x0,y0,z0, x1,y1,z1, …]` |
| `faces` | Variable-length: `[n, i0..i(n-1), n, i0..., …]` where `n` is vertex count per face |
| `vertexNormals` | Same layout as `vertices` (optional) |
| `textureCoordinates` | Flat `[u0,v0, u1,v1, …]` (optional) |
| `colors` | Packed ARGB int per vertex (optional) |
| `renderMaterial` | Inline body or `{ referencedId, speckle_type: "reference" }` stub |

**Triangulation:** Faces with `n > 3` use fan triangulation around vertex 0 (same rule as Speckle Python SDK and Revit connector). `n == 3` is a single triangle.

### RenderMaterial (`Objects.Other.RenderMaterial`)

PBR fields used by connectors:

| Field | Type | Notes |
|---|---|---|
| `diffuse` | ARGB int | Base colour |
| `emissive` | ARGB int | Emissive colour |
| `opacity` | double | 0–1 |
| `roughness` | double | 0–1 |
| `metalness` | double | 0–1 |
| `*Texture` | string | `@blob:HASH` or bare blob id |

Convert diffuse/emissive from ARGB packed integers. Resolve texture fields through the blob API before binding to your material system.

For immutability rules, manual re-commit workflow, and the **PRISM material swap API** (`POST /api/orbit/material-swap`), see [Materials](materials).

### displayValue fallback

Types like `Objects.Geometry.Brep`, `Objects.Geometry.NurbsCurve`, and higher-level data objects (`Speckle.DataObject`, BIM elements) carry a `displayValue` array of meshes (inline or reference stubs). **Always render `displayValue` when the native type is unsupported** — the native Brep/NURBS encoding is not intended for real-time viewers.

### Common pitfalls

- **Exact `speckle_type` matching** — prefix matching (e.g. all of `Objects.Geometry.*`) mis-classifies future types. Match the full string.
- **Skipping detached materials** — meshes reference materials by `referencedId`; fetch and index materials before binding.
- **Ignoring inline materials** — a mesh may embed the full `renderMaterial` body instead of a stub.
- **Missing blob pre-pass** — texture placeholders are not URLs; they require a separate blob download.
- **Forgetting units** — Rhino sends commonly use `mm`; scale to your scene unit system.

---

## API reference (quick)

### GraphQL

| Operation | Purpose |
|---|---|
| `project(id).model(id).versions` | List versions |
| `project(id).version(id).referencedObject` | Root object hash |
| `project(id).commentThreads(filter: { resourceIdString })` | Comments for a loaded version |

Endpoint: `POST /graphql` — see [GraphQL reference](graphql-reference).

### REST

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/objects/{projectId}/{objectId}/single` | Download one object JSON |
| `POST` | `/objects/{projectId}` | Batch upload (connectors) or batch download (upstream) |
| `GET` | `/api/stream/{projectId}/blob/{blobId}` | Download texture/attachment binary |

See [Objects (REST)](objects) for upload semantics and permissions.

---

## Optional — comments and object pins

If your viewer should show ORBIT Discussions or create object pins, integrate the GraphQL comment API. See [Comments & discussions](comments-discussions).

Key integrator notes:

- **`resourceIdString`** — comma-delimited encoding tying a thread to a model, version, and optionally a specific object. **Not formally documented.** Copy a real value from the ORBIT web UI (network tab when creating a pin) or from `viewerResources` on an existing comment.
- **`viewerState`** — JSON blob capturing camera, selection, isolation state for pins. **No published schema.** Omit for text-only threads; copy from UI-created comments for spatial pins.
- Comment bodies require **ProseMirror JSON** (`content.doc`), not plain strings.

```graphql
query($projectId: String!, $resourceIdString: String!) {
  project(id: $projectId) {
    commentThreads(
      limit: 50
      filter: { resourceIdString: $resourceIdString, loadedVersionsOnly: true }
    ) {
      items { id rawText viewerState author { name } }
    }
  }
}
```

There is **no REST API for comments**.

---

## Reference implementations

| Implementation | Location | Notes |
|---|---|---|
| **PRISM Visualiser receive pipeline** | `PRISM/visualiser/.../OrbitReceivePipeline.cs` | Production C# — BFS traversal, blob pre-pass, glTF export |
| **PRISM HTTP client** | `PRISM/visualiser/.../HttpOrbitApi.cs` | Verified endpoint templates (`/single`, blob path) |
| **Loose object parser** | `PRISM/visualiser/.../OrbitObject.cs` | `referencedId` walking, `speckle_type`/`type` tolerance |
| **Mesh / material converters** | `PRISM/visualiser/.../Converters/FromOrbit/` | Speckle face encoding, PBR materials, displayValue |
| **orbit-sdk ServerTransport** | `orbit-sdk` → `ServerTransport.cs` | Same REST paths; typed serialisation |
| **Rhino connector receive** | `orbit-connectors` receive pipeline | BFS + displayValue fallback + block instances |
| **PRISM admin GLB viewers** | `PRISM/web/src/admin/components/ModelViewer.vue`, `FixtureViewer.vue` | three.js previews for model library / fixtures (GLB, not live ORBIT fetch) |

When in doubt, read `OrbitReceivePipeline.ReceiveAsync` — it is the most complete open-source receive path against live ORBIT.

---

## Alternative integration paths

### PRISM UE Pixel Streaming

For portal embeds that need Unreal-quality rendering without building a geometry pipeline:

1. Call `POST https://prism.rebus.industries/api/visualiser/streams` with `projectId`, `modelId`, optional `versionId`.
2. Embed Epic's Pixel Streaming frontend against the returned `signallingUrl` + TURN credentials.
3. Release with `DELETE /api/visualiser/streams/{runId}`.

Full contract: [PRISM Portal Integration Guide](https://github.com/REBUS-Industries/prism/blob/main/docs/PORTAL_INTEGRATION.md) and `https://prism.rebus.industries/docs`.

Requires a PRISM API key with `visualiser:create_stream` scope — separate from ORBIT PAT auth.

### ORBIT web frontend (iframe)

The ORBIT frontend Docker image (`orbit-frontend`) supports iframe embedding with comments, pins, and the collectionType-aware layer sidebar. It is **not published as source or an npm package** in this repo — only the prebuilt image in `docker-compose.yml`. Use this when you need the full Speckle-style viewer UX without maintaining geometry code.

### Upstream Speckle viewer SDK

The [@speckle/viewer](https://www.npmjs.com/package/@speckle/viewer) npm package targets `app.speckle.systems` deployments. It may work against ORBIT with URL and auth adjustments, but is **unsupported** by REBUS — no compatibility testing, no `@speckle/viewer` guide in ORBIT docs. Refer to [Speckle viewer documentation](https://docs.speckle.systems/developers/viewer/overview) for concepts; validate every endpoint against ORBIT production.

---

## Known gaps

| Gap | Workaround |
|---|---|
| No ORBIT-maintained web viewer SDK | Build receive pipeline (this guide) or use PRISM UE streaming |
| `viewerState` / `resourceIdString` formats undocumented | Copy from UI network traffic; see [Comments & discussions](comments-discussions) |
| `/objects/.../single` vs `/objects/...` path | Use `/single` on ORBIT production |
| `orbit-frontend` not available as npm/source | Prebuilt Docker image only |
| Blob upload API for custom senders | Inspect browser network tab or upstream Speckle file-upload docs |
| `orbit-sdk` repo stale vs vendored SDK in connectors | Connectors vendor a newer SDK; check `orbit-connectors/vendor/SDK` for latest types |

Report discrepancies by comparing live `/graphql` introspection with these docs and opening an issue in `REBUS-Industries/orbit-server`.

---

## Recommended reading order

1. [Authentication](authentication) — obtain a PAT
2. [Projects, models & versions](projects-models-versions) — resolve `projectId`, `modelId`, `versionId`
3. [Objects (REST)](objects) — upload/download semantics (note `/single` path in this guide)
4. **This page** — receive + render pipeline
5. [Comments & discussions](comments-discussions) — optional pins/threads
6. [GraphQL reference](graphql-reference) — full operation catalogue
7. [Limitations](limitations) — constraints and upstream drift

Related architecture (outside `/docs`):

- `architecture/ARCHITECTURE.md` — object model, closure, detachment, send/receive pipelines
- `architecture/systems/connectors.md` — connector receive behaviour
- `architecture/systems/sdk.md` — SDK serialisation rules
