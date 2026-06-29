# Materials

ORBIT stores surface appearance as **`Objects.Other.RenderMaterial`** objects attached to geometry — typically inline on `Objects.Geometry.Mesh` as a `renderMaterial` property, or detached via `{ "referencedId": "...", "speckle_type": "reference" }` stubs on larger payloads.

Materials are **content-addressed** like every other ORBIT object: the material body participates in the parent mesh hash when inline, and has its own object id when detached. There is **no in-place mutation API** on ORBIT server. Changing a material requires editing the object graph, uploading new/changed objects via REST, and creating a **new version** that points at the new root hash.

For PBR texture maps, connectors upload image bytes to ORBIT blob storage (`POST /api/stream/{projectId}/blob`) and reference the server-assigned blob id on the `RenderMaterial` (or `@blob:SHA256` placeholders during conversion — see [Building a 3rd party viewer](building-a-3rd-party-viewer)).

---

## RenderMaterial fields

| Field | Type | Notes |
|---|---|---|
| `name` | string | Display label |
| `diffuse` | ARGB int | Base colour as **unsigned** ARGB packed into a JSON number (e.g. opaque white = `4294967295`) |
| `emissive` | ARGB int | Emissive colour (same encoding) |
| `opacity` | double | 0–1 |
| `roughness` | double | 0–1 |
| `metalness` | double | 0–1 |
| `emissiveIntensity` | double | Optional |
| `baseColorTexture` / `diffuseTexture` | string | ORBIT blob id or `@blob:HASH` placeholder |
| `normalTexture` | string | Normal map blob id |
| `roughnessTexture` | string | Roughness map blob id |
| `metalnessTexture` | string | Metalness map blob id |
| `emissiveTexture` / `pbrEmissionTexture` | string | Emissive map blob id |
| `opacityTexture` | string | Opacity / alpha map blob id |
| `diffuseTextureRepeat` / `diffuseTextureOffset` | `[x, y]` | UV tiling / offset |

See the connector SDK type `Orbit.Objects.Other.RenderMaterial` for the full field list.

### Proxies

Rhino and other connectors may also emit **`RenderMaterialProxy`** objects at the root: a material definition plus a list of `applicationId` values for objects that use it. Proxies reference source-app ids, not content hashes. A minimal viewer can ignore proxies if every mesh carries an inline `renderMaterial`.

---

## Immutability and versioning

1. Download the version's object graph (root hash from GraphQL → REST `/objects/{projectId}/{id}/single`).
2. Locate the target mesh by **`applicationId`** (Rhino GUID) or ORBIT **object id**.
3. Build or edit the `renderMaterial` body (scalar PBR + blob refs for textures).
4. Re-compute content hashes for every changed object and ancestors up to the root (object ids are MD5 of serialised JSON excluding `id` and `__closure`).
5. Upload only **new** object bodies via `POST /objects/{projectId}`.
6. Upload any new texture binaries via `POST /api/stream/{projectId}/blob`.
7. Create a new version via GraphQL `versionMutations.create` pointing at the new root object id.

Steps 1–7 are what the PRISM material-swap API automates (see below). Integrators can also perform the workflow manually or via the `orbit-sdk` serialiser.

---

## PRISM material swap API

PRISM maintains a separate **PBR materials library** (albedo, normal, roughness, metallic, etc.) in its own database. The swap endpoint maps a PRISM material onto an ORBIT mesh and commits a new ORBIT version.

**Endpoint**

```
POST https://prism.rebus.industries/api/orbit/material-swap
Authorization: Bearer <ORBIT PAT or PRISM API key>
Content-Type: application/json
```

### Authentication

| Caller | Requirement |
|---|---|
| ORBIT bearer token | Valid ORBIT user with **write** access on the target project |
| PRISM admin session | Full access (server uses configured ORBIT credentials when no bearer is present) |
| PRISM API key | Scope **`materials:write`** |

### Request body

```json
{
  "projectId": "ORBIT_PROJECT_ID",
  "modelId": "ORBIT_MODEL_ID",
  "versionId": "OPTIONAL_VERSION_ID",
  "prismMaterialId": "PRISM_MATERIAL_UUID",
  "applicationId": "RHINO_OBJECT_GUID",
  "orbitTarget": "prod",
  "message": "Optional commit message"
}
```

Identify the mesh with **`applicationId`** (preferred for connector round-trip) **or** **`objectId`** (ORBIT content hash of the mesh). Omit `versionId` to use the latest version on the model.

| Field | Required | Description |
|---|---|---|
| `projectId` | yes | ORBIT project id |
| `modelId` | yes | ORBIT model id |
| `prismMaterialId` | yes | UUID from `GET /api/materials` on PRISM |
| `applicationId` or `objectId` | one required | Target mesh |
| `versionId` | no | Defaults to latest commit |
| `orbitTarget` | no | `prod` (default) or `dev` |
| `message` | no | Version commit message |

### Response (success)

```json
{
  "ok": true,
  "target": "prod",
  "projectId": "...",
  "modelId": "...",
  "previousVersionId": "...",
  "newVersionId": "...",
  "newRootObjectId": "...",
  "meshObjectId": "...",
  "meshObjectIdAfter": "...",
  "prismMaterialId": "...",
  "prismMaterialName": "Concrete Panel",
  "uploadedObjectCount": 3,
  "uploadedBlobCount": 4,
  "idRemap": { "oldMeshId": "newMeshId" }
}
```

### Workflow (server-side)

1. Resolve version → root object hash (ORBIT GraphQL).
2. Download the full object graph (REST).
3. Load PRISM material slots + PBR parameters.
4. Read PRISM texture files from disk → upload to ORBIT blob API → map slots to `RenderMaterial` texture fields.
5. Set inline `renderMaterial` on the target mesh.
6. Re-hash changed objects and upload new bodies.
7. `versionMutations.create` with `sourceApplication: "PRISM"`.

### PRISM → ORBIT slot mapping

| PRISM slot | ORBIT field |
|---|---|
| `albedo` | `baseColorTexture`, `diffuseTexture` |
| `normal` | `normalTexture` |
| `roughness` | `roughnessTexture` |
| `metallic` | `metalnessTexture` |
| `emissive` | `emissiveTexture`, `pbrEmissionTexture` |
| `opacity` | `opacityTexture` |

Scalar parameters (`baseColor`, `roughness`, `metallic`, `opacity`, emissive colour/intensity, UV tiling/offset) map directly onto the corresponding `RenderMaterial` fields.

### Example

```bash
curl -s -X POST "https://prism.rebus.industries/api/orbit/material-swap" \
  -H "Authorization: Bearer YOUR_ORBIT_PAT" \
  -H "Content-Type: application/json" \
  -d '{
    "projectId": "PROJECT_ID",
    "modelId": "MODEL_ID",
    "prismMaterialId": "00000000-0000-4000-8000-000000000001",
    "applicationId": "RHINO_MESH_GUID"
  }'
```

PRISM materials API docs: [PRISM OpenAPI](https://prism.rebus.industries/docs) (`/api/materials`).

---

## Manual integrator workflow

When not using PRISM, follow the immutability steps above:

```bash
# 1. Resolve version
curl -s https://orbit.rebus.industries/graphql \
  -H "Authorization: Bearer YOUR_PAT" \
  -H "Content-Type: application/json" \
  -d '{"query":"query($p:String!,$v:String!){ project(id:$p){ version(id:$v){ referencedObject model { id } } } }","variables":{"p":"PROJECT_ID","v":"VERSION_ID"}}'

# 2. Download objects (repeat for every referencedId)
curl -s "https://orbit.rebus.industries/objects/PROJECT_ID/ROOT_HASH/single" \
  -H "Authorization: Bearer YOUR_PAT"

# 3. Upload changed objects
curl -s -X POST "https://orbit.rebus.industries/objects/PROJECT_ID" \
  -H "Authorization: Bearer YOUR_PAT" \
  -H "Content-Type: application/json" \
  -d @changed-objects.json

# 4. Create version
curl -s https://orbit.rebus.industries/graphql \
  -H "Authorization: Bearer YOUR_PAT" \
  -H "Content-Type: application/json" \
  -d '{"query":"mutation($i:CreateVersionInput!){ versionMutations { create(input:$i){ id referencedObject } } }","variables":{"i":{"projectId":"PROJECT_ID","modelId":"MODEL_ID","objectId":"NEW_ROOT_HASH","message":"Material update","sourceApplication":"MyApp","totalChildrenCount":42}}}'
```

Use the `orbit-sdk` `OrbitSerializer` for correct detachment, closure, and id computation when editing complex trees.

---

## Related documentation

- [Objects (REST)](objects) — upload/download pipeline
- [Projects, models & versions](projects-models-versions) — `versionMutations.create`
- [Building a 3rd party viewer](building-a-3rd-party-viewer) — `RenderMaterial` rendering contract, blob download
- [PRISM materials store](https://github.com/REBUS-Industries/prism/blob/main/docs/EXTERNAL_MATERIALS.md) — library management (separate from ORBIT persistence)
