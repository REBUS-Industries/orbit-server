# ORBIT — Technical Architecture

## 1. System Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│  CLIENT SIDE                                                        │
│                                                                     │
│  ┌──────────────────┐    ┌──────────────────────────────────────┐  │
│  │  ORBIT SDK       │    │  ORBIT Rhino Connector               │  │
│  │  (Orbit.Objects  │◄───│  OrbitConnector.Rhino.rhp            │  │
│  │   Orbit.Sdk)     │    │  net8.0-windows                      │  │
│  └──────┬───────────┘    └──────────────────────────────────────┘  │
│         │ HTTP (objects + GraphQL)                                  │
└─────────┼───────────────────────────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────────────────────────┐
│  SERVER SIDE  (VM 201 prod / VM 301 dev — Docker Compose)          │
│                                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────────┐   │
│  │ orbit-server │  │orbit-frontend│  │ orbit-preview          │   │
│  │ GraphQL API  │  │ 3D Viewer    │  │ Thumbnail generator    │   │
│  │ Auth / OAuth │  │ iFrame embed │  │ (no-op Camera fix)     │   │
│  └──────┬───────┘  └──────────────┘  └────────────────────────┘   │
│         │                                                           │
│  ┌──────▼───────┐  ┌──────────────┐  ┌────────────────────────┐   │
│  │  PostgreSQL  │  │  MinIO (S3)  │  │  Valkey (Redis)        │   │
│  │  Metadata    │  │  Objects     │  │  Pub-sub / queues      │   │
│  └──────────────┘  └──────────────┘  └────────────────────────┘   │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  PRISM  (port 8765 — convert.rebus.industries)               │  │
│  │  FastAPI  •  Job queue  •  trimesh / RhinoCompute workers    │  │
│  └──────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
          ▲
          │  VRRP VIP 10.0.200.250
┌─────────┴───────────────────────────────────────────────────────────┐
│  HA CADDY PROXY PAIR                                                │
│  Proxy1 (LXC 251, MASTER)   Proxy2 (LXC 252, BACKUP)              │
│  TLS termination, CSP headers, X-Frame-Options removal             │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 2. ORBIT Object Model

Every piece of data in ORBIT is an `OrbitBase` object. The object graph forms a tree: a root `OrbitObject` (Collection) contains nested `OrbitObject` children (layers), which contain geometry leaves.

### Base types

```
OrbitBase
├── id              — SHA-256 content hash (deterministic, set by serialiser)
├── applicationId   — stable source-app identifier (Rhino GUID, etc.)
├── speckle_type    — fully qualified type name (wire compat with ORBIT server)
├── __closure       — flat map of descendant { id → depth } (root only)
└── DynamicProperties — arbitrary key/value bag (UserStrings, UserDictionary, etc.)

OrbitObject : OrbitBase
├── name
├── displayValue    — List<OrbitBase> mesh fallbacks for viewer
├── elements        — List<OrbitBase> children (sub-collections, geometry)
├── sourceApplication
└── units
```

### Geometry types (all in `Orbit.Objects.Geometry`)

| Type | Key fields | Notes |
|---|---|---|
| `Point` | x, y, z, units | |
| `Line` | start, end, domain, units | |
| `Polyline` | value (flat double[]), closed, domain | |
| `Arc` | radius, startAngle, endAngle, plane, displayValue | displayValue = Polyline |
| `Circle` | radius, plane, displayValue | |
| `NurbsCurve` | degree, points, weights, knots, rational, displayValue | displayValue = Polyline |
| `PolyCurve` | segments (mixed curve types), closed, displayValue | |
| `Plane` | origin, normal, xdir, ydir | |
| `Mesh` | vertices (flat), faces (variable-length), vertexNormals, textureCoordinates, colors | Primary display primitive |
| `Brep` | encoded, surfaces, curve3D, **displayValue (Mesh[])** | Always has display mesh |
| `Surface` | degreeU/V, pointData, knotsU/V, closedU/V | Sub-component of Brep |
| `Instance` | definitionId, transform (4×4) | Block placement |
| `PointCloud` | points (flat), colors, normals | |

### Proxy types (all in `Orbit.Objects.Proxies`)

Proxies are stored at the root of the version object tree — not nested inside geometry. They reference objects by `applicationId`.

| Proxy | Purpose |
|---|---|
| `RenderMaterialProxy` | Material definition + list of objectIds that use it |
| `ColorProxy` | ARGB colour + list of objectIds |
| `GroupProxy` | Named group + list of objectIds |
| `DefinitionProxy` | Block definition geometry + base point; referenced by `Instance.definitionId` |

### Primitives

`Vector3d`, `Transform` (4×4 column-major matrix), `Interval`

---

## 3. Serialisation

The `OrbitSerializer` in `Orbit.Sdk` converts an object tree to a flat dictionary of `{ id → json }`.

**Content hashing:** Each object's `id` is a SHA-256 hash of its serialised JSON (excluding the `id` and `__closure` fields). Two objects with identical content always produce the same id — enabling automatic deduplication across sends.

**Detachment:** Objects whose serialised size exceeds `DetachThresholdBytes` (default 1 KB) are extracted from their parent and stored as separate entries. The parent retains a reference token `{ "referencedId": "abc123" }`. Large meshes and Brep objects are always detached.

**Closure table:** The root object gets a `__closure` field — a flat map of every descendant object id to its depth in the tree. The server uses this during receive to identify all objects belonging to a version in one query, rather than walking the tree recursively.

**Wire format note:** The field `speckle_type` is kept as-is for compatibility with the ORBIT server (which is built on Speckle infrastructure). When we fork the server further, this will be renamed to `orbit_type`.

---

## 4. Transport Layer

```
IOrbitTransport
├── SaveObjectAsync(id, json)
├── SaveObjectBatchAsync(objects, progress)    ← batches up to 100 objects or 1 MB
├── GetObjectAsync(id) → json
└── HasObjectAsync(id) → bool                  ← used for deduplication before upload
```

Implementations:
- `ServerTransport` — HTTP to ORBIT server (`POST /objects/{projectId}`, `GET /objects/{projectId}/{id}/single`)
- `LocalTransport` — disk-based, one file per object; used for testing and offline development

---

## 5. Send Pipeline (Rhino → ORBIT)

```
1. EXTRACT
   RhinoDoc objects → filtered by ConnectorCard (All / ByLayer / Selection)
   Block definitions → collected for DefinitionProxy

2. CONVERT  (per RhinoObject)
   Dispatch to IRhinoToOrbitConverter by GeometryBase type:
     Mesh     → RhinoMeshConverter
     Brep     → RhinoBrepConverter  (+ display mesh always attached)
     Curve    → RhinoCurveConverter (dispatches to NurbsCurve/Arc/Circle/Line/Polyline)
     Text     → RhinoTextConverter
     Hatch    → RhinoHatchConverter
     Instance → RhinoInstanceConverter → Instance + DefinitionProxy
     (unknown)→ RhinoFallbackConverter → Mesh.CreateFromBrep display fallback
   
   Each converter attaches:
     applicationId = RhinoObject.Id (Guid)
     properties    = UserStrings + UserDictionary → DynamicProperties
     units         = from RhinoDoc.ModelUnitSystem

3. ASSEMBLE TREE
   Root OrbitObject (named: project/model)
     └── OrbitObject per Rhino layer (FullPath as name)
           └── Geometry objects
   Proxies at root: RenderMaterialProxy[], ColorProxy[], GroupProxy[], DefinitionProxy[]

4. SERIALISE
   OrbitSerializer.SerialiseAsync(root)
   → flat Dictionary<id, json>
   → root.__closure populated

5. DEDUP + UPLOAD
   For each object: ServerTransport.HasObjectAsync(id)
   → skip if already on server (deduplication by content hash)
   → batch upload remainder via SaveObjectBatchAsync

6. CREATE VERSION
   OrbitClient.CreateVersionAsync(projectId, modelId, root.id, message)
```

---

## 6. Receive Pipeline (ORBIT → Rhino)

```
1. FETCH VERSION
   OrbitClient → project.model.version → referencedObject (root id)

2. READ CLOSURE
   GET /objects/{projectId}/{rootId}
   Parse __closure → flat list of all object ids + depths

3. FETCH OBJECTS
   Batch GET all ids (8 concurrent requests)
   OrbitDeserializer.Deserialise(json) → typed OrbitBase subtype

4. RESOLVE REFERENCES
   Walk object graph, resolve { referencedId } tokens → actual objects

5. CONVERT  (per ORBIT object)
   Dispatch to IOrbitToRhinoConverter:
     Orbit.Mesh          → OrbitMeshToRhino
     Orbit.Brep          → OrbitBrepToRhino
     Orbit.NurbsCurve    → OrbitNurbsCurveToRhino
     Orbit.Instance      → OrbitInstanceToRhino  (reconstructs block)
     (unknown type)      → OrbitFallbackToRhino  → render displayValue mesh
   
   ReceiveMode:
     Update  — match existing Rhino object by applicationId, replace geometry
     Create  — always add new objects
     Ignore  — skip if applicationId already exists in doc

6. BAKE
   RhinoLayerBaker     → find-or-create layer hierarchy from OrbitObject.name paths
   RhinoMaterialBaker  → create/find Rhino render materials from RenderMaterialProxy
   RhinoColorBaker     → apply ColorProxy → object colour attribute
   RhinoGroupBaker     → create Rhino groups from GroupProxy
   RhinoInstanceBaker  → register DefinitionProxy as Rhino BlockDefinition, place Instance
```

---

## 7. PRISM Pipeline (File → ORBIT)

PRISM handles formats that connectors cannot convert natively.

```
Client (or web UI)
  └── POST /convert/async  (file + orbit_server_url + project_id + model_id)
        └── Job queued → background worker dispatched by format
              └── .obj / .stl  → ObjWorker (trimesh → ORBIT Mesh JSON → upload)
                  .dwg         → DwgWorker (→ RhinoCompute → mesh → upload)
                  .fbx         → FbxWorker (TODO)
                  .ifc         → IfcWorker (TODO)
              └── OrbitUploader.upload(root) → POST /objects/{projectId}
              └── OrbitUploader.create_version(model_id, root_id)

Client polls: GET /jobs/{job_id} → { status: queued|processing|complete|failed }
```

PRISM does **not** use the Rhino Connector. If a user is in Rhino, they use the connector directly. PRISM is for: bulk file ingestion from consultants, web UI upload workflows, and formats with no native connector.

---

## 8. Authentication

OAuth2 PKCE flow, implemented in `OrbitAuthManager`:

```
1. Connector opens browser:
   https://speckle.rebus.industries/authn/verify/{appId}/{challenge}

2. HttpListener on localhost:29364 waits for callback
   → receives access_code in query string

3. POST /auth/token  { appId, appSecret=verifier, accessCode, challenge }
   → receives { token }

4. Validate: GraphQL activeUser { id name email }

5. Token stored in Rhino plugin settings (keyed by SHA-256 of server URL)
```

OAuth app IDs (registered on ORBIT server):
- Prod: `c0c8e773a3`
- Dev:  `c047ac8afa`

---

## 9. Card Persistence

Connector cards (send/receive configurations) are stored as JSON in `RhinoDoc.Strings`:
- Section: `orbit_connector`
- Entry: `cards`

Cards travel with the `.3dm` file. When a colleague opens the file, their ORBIT panel automatically shows the same send/receive cards — no manual reconfiguration needed.

---

## 10. Infrastructure

### Proxmox Cluster

```
SRV01  10.0.1.101  ─┐
SRV02  10.0.1.102  ─┼─ 3-node cluster
SRV03  10.0.1.103  ─┘
                        │
              VRRP VIP 10.0.200.250
              HA Caddy proxy pair:
              LXC 251  10.0.200.251  MASTER  priority 200
              LXC 252  10.0.200.252  BACKUP  priority 100
              (keepalived tracks caddy.service; failover on failure)
```

### VMs

| VM | IP | Role |
|---|---|---|
| 201 | 10.0.200.11 | ORBIT PROD stack + PRISM |
| 301 | 10.0.200.112 | ORBIT DEV stack |
| 203 | 10.0.200.13 | Erugo file sharing |
| 204 | 10.0.200.14 | RhinoCompute (Windows Server 2025, IIS) |
| RB-DA2-PC01 | 10.0.10.201 | DWG conversion workstation (Rhino 8 headless, rh_watcher.ps1) |

### CI/CD

On `git tag v*.*.*` push to any repo:
- SDK: pack NuGet → publish to GitHub Packages
- Connectors: build `.rhp` → create GitHub Release with installer
- Server: SSH to VM → `docker compose pull && up -d`
- PRISM: build Docker image → push to GHCR → SSH deploy to VM
