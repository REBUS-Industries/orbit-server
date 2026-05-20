# ORBIT — Full Rebuild Plan
**Version:** 0.1 | **Date:** 2026-05-19 | **Author:** Dom / REBUS Industries

---

## 1. What ORBIT and PRISM Are

**ORBIT** is our full 3D data platform — the server, viewer, API, and connector ecosystem. It is not a Speckle product. It is our own implementation of an object-model-based 3D data transport and visualisation system, built from scratch and informed by open-source research into how the Speckle ecosystem works. ORBIT runs on our own infrastructure, under our own brand, and is extended on our own roadmap.

**PRISM** is our separate file conversion pipeline — the successor to the 3DConvert service. Where ORBIT handles native geometry (Rhino, eventually Revit, etc.) through direct in-process conversion, PRISM handles file formats that sit outside ORBIT's native connector abilities: DWG, FBX, IFC, OBJ, and similar. PRISM is a standalone service with its own API, and is not part of the core connector pipeline. It exists alongside ORBIT and feeds into it.

The full system comprises:

| Product | Role |
|---|---|
| **ORBIT** | Platform: server, 3D viewer, GraphQL API, object storage, auth |
| **ORBIT SDK** | C# library: object model, serialisation, transport — used by all connectors |
| **ORBIT Rhino Connector** | Rhino 8 plugin: direct in-process geometry conversion via ORBIT SDK |
| **PRISM** | File conversion service: DWG/FBX/IFC/OBJ → ORBIT objects |

---

## 2. Server Layer — What We Have and What Changes

### Base: Speckle Server 2.31.1

Our existing deployment runs on `speckle-server 2.31.1` with three custom Docker images we built on top:

| Image | What we changed |
|---|---|
| `speckle-frontend-2-rebus:v2.3.0` | iFrame embedding unlocked, V3 named views enabled, collectionType sidebar exposed |
| `speckle-server-rebus:v2.1.0` | Patched backend; `FILE_SIZE_LIMIT_MB=1000` |
| `speckle-preview-service-rebus:v2.1.0` | No-op Camera converter so preview thumbnails render correctly for our geometry |

### What the server gives us (that we keep)
- GraphQL API for projects, models, versions, objects, users, permissions
- S3-compatible object storage (MinIO on our stack)
- PostgreSQL for metadata
- Redis/Valkey for pub-sub and queues
- OAuth2 app registration (our connector app IDs: `c0c8e773a3` prod, `c047ac8afa` dev)
- WebSocket subscriptions for live updates
- File import service (for third-party format import)
- Webhook service

### ORBIT Server rename strategy
- Rename the Docker image series from `*-rebus:*` → `orbit-server:*`, `orbit-frontend:*`, `orbit-preview:*`
- New Git repo: `orbit-server` (private) — holds our docker-compose, custom patches, and build pipeline
- Continue tracking upstream Speckle server changes selectively (cherry-pick security patches, avoid merging breaking API changes)
- Address outstanding security issue: rotate `SESSION_SECRET` and remove credentials from `docker-compose.yml` — use `.env` file excluded from Git

### Server API we target from the connector
Our connector will use the **same GraphQL API** that Speckle exposes. Key queries/mutations:

```graphql
# Auth
query { activeUser { id name email } }

# Projects (was "streams" in v2)
query { activeUser { projects { items { id name } } } }
mutation createProject($input: ProjectCreateInput!) { projectMutations { create(input: $input) { id } } }

# Models (was "branches")
query { project(id: $id) { models { items { id name } } } }

# Versions (was "commits")
mutation { modelMutations { create(input: $input) { id } } }

# Objects — bulk upload/download
POST /objects/{streamId}          — upload batch of serialised objects
GET  /objects/{streamId}/{objId}  — download single object + children
```

---

## 3. Git Repository Structure

### Recommended: New GitHub Organisation

Create a new GitHub organisation: **`orbit-platform`** (or `rebus-orbit`).
This cleanly separates ORBIT code from the existing CheekiSkrub personal account and signals that this is a platform product, not a personal project.

### Repository Map

```
orbit-platform/
├── orbit-sdk                   # C# — Core SDK (object model, serialisation, transport)
├── orbit-rhino                 # C# — Rhino 8 connector plugin
├── orbit-server                # Docker — ORBIT server images + compose + deploy configs
├── prism                       # Python — File conversion service (DWG/FBX/IFC/OBJ → ORBIT)
├── orbit-proxy-config          # Config — Caddy + keepalived HA proxy pair
└── orbit-infra                 # Docs — Proxmox, VM setup, operational runbooks
```

**Public vs Private:**
- `orbit-sdk` → eventually public (MIT) — makes it a real open platform
- `orbit-rhino` → private initially (competitive advantage)
- `orbit-server` → private (contains infra specifics)
- `prism` → private
- `orbit-proxy-config` → private
- `orbit-infra` → private

### Branch Strategy (all repos)

```
main          ← always deployable; protected; requires PR + CI pass
develop       ← integration branch; PRs merge here first
feature/*     ← individual features (short-lived)
fix/*         ← bug fixes
release/*     ← release prep (version bump, changelog)
```

Tag format: `v{major}.{minor}.{patch}` — triggers GitHub Actions release pipeline.

### CI/CD — GitHub Actions

```yaml
# On push to develop / feature branches:
- Build
- Unit tests
- Integration tests (where applicable)

# On push to main (after PR merge):
- Build
- All tests
- Package artefacts

# On tag v*.*.*:
- Build Release
- Package (.rhp + installer for connector; Docker images for server)
- Create GitHub Release with artefacts
- Trigger deploy webhook to VMs (via SSH or Webhook receiver on VM)
```

### Deployment trigger (VM side)

Each VM runs a lightweight webhook receiver (e.g. `webhook` or `adnanh/webhook`) that listens for a GitHub Actions `repository_dispatch` event or a deploy token POST. On trigger:

```bash
# On VM 201 (prod) / VM 301 (dev):
docker compose pull && docker compose up -d
```

This replaces any manual SSH-and-pull workflow with a proper CD pipeline.

---

## 4. ORBIT SDK — C# Library

The SDK is the foundation. Everything the connector does, it does via the SDK. No raw HTTP calls in the connector layer.

### Project: `Orbit.Sdk`

Target: `net8.0`
NuGet output: `Orbit.Sdk` + `Orbit.Objects`

### Folder Structure

```
orbit-sdk/
├── src/
│   ├── Orbit.Objects/                    # Geometry schema
│   │   ├── Orbit.Objects.csproj
│   │   ├── Base/
│   │   │   ├── OrbitBase.cs              # Root base class
│   │   │   └── OrbitObject.cs            # DataObject equivalent (has displayValue)
│   │   ├── Geometry/
│   │   │   ├── Mesh.cs
│   │   │   ├── Point.cs
│   │   │   ├── Line.cs
│   │   │   ├── Polyline.cs
│   │   │   ├── NurbsCurve.cs
│   │   │   ├── PolyCurve.cs
│   │   │   ├── Arc.cs
│   │   │   ├── Circle.cs
│   │   │   ├── Ellipse.cs
│   │   │   ├── Box.cs
│   │   │   ├── Plane.cs
│   │   │   ├── Vector.cs
│   │   │   ├── Brep.cs                   # Full NURBS boundary rep
│   │   │   ├── Extrusion.cs
│   │   │   ├── SubD.cs
│   │   │   ├── PointCloud.cs
│   │   │   ├── Hatch.cs
│   │   │   └── Text.cs
│   │   ├── Proxies/
│   │   │   ├── RenderMaterialProxy.cs    # Materials referenced by applicationId
│   │   │   ├── ColorProxy.cs
│   │   │   ├── GroupProxy.cs
│   │   │   └── DefinitionProxy.cs        # Block definitions
│   │   └── Primitives/
│   │       ├── Interval.cs
│   │       ├── Transform.cs              # 4×4 matrix
│   │       └── RenderMaterial.cs
│   │
│   └── Orbit.Sdk/                        # Core SDK
│       ├── Orbit.Sdk.csproj
│       ├── Serialisation/
│       │   ├── OrbitSerializer.cs        # Object → JSON
│       │   ├── OrbitDeserializer.cs      # JSON → Object
│       │   ├── HashingService.cs         # SHA256 content hash → object id
│       │   ├── ClosureBuilder.cs         # Builds __closure flat index
│       │   └── DynamicPropertyBag.cs     # Dynamic properties on OrbitBase
│       ├── Transport/
│       │   ├── IOrbitTransport.cs        # Interface: SaveObject, GetObject, HasObject
│       │   ├── ServerTransport.cs        # HTTP transport to ORBIT server
│       │   └── LocalTransport.cs         # Disk-based (testing / offline cache)
│       ├── Api/
│       │   ├── OrbitClient.cs            # Main API entry point
│       │   ├── GraphQL/
│       │   │   ├── OrbitGraphQLClient.cs # Raw GQL client (HttpClient-based)
│       │   │   └── Queries/              # .graphql query files embedded as resources
│       │   └── Models/
│       │       ├── OrbitProject.cs
│       │       ├── OrbitModel.cs
│       │       ├── OrbitVersion.cs
│       │       └── OrbitUser.cs
│       ├── Auth/
│       │   ├── IOrbitAuthManager.cs
│       │   ├── OrbitOAuthManager.cs      # OAuth2 PKCE flow
│       │   └── TokenStore.cs             # Secure credential persistence
│       └── Pipeline/
│           ├── SendOperation.cs          # Orchestrates extract → serialise → send
│           └── ReceiveOperation.cs       # Orchestrates fetch → deserialise → return
│
├── tests/
│   ├── Orbit.Sdk.Tests/
│   └── Orbit.Objects.Tests/
│
└── Orbit.Sdk.sln
```

### Core Design: `OrbitBase`

```csharp
public class OrbitBase
{
    [JsonProperty("id")]
    public string Id { get; set; }                    // SHA256 of content

    [JsonProperty("applicationId")]
    public string ApplicationId { get; set; }         // Stable source-app ID

    [JsonProperty("speckle_type")]                    // keep for server compat
    public virtual string OrbitType => GetType().FullName;

    [JsonProperty("__closure")]
    public Dictionary<string, int> Closure { get; set; }  // Built during send

    // Dynamic property bag — anything not in schema
    [JsonExtensionData]
    public Dictionary<string, JToken> DynamicProperties { get; set; }
}
```

Key decisions:
- **Content hashing**: `id` is SHA256 of the serialised JSON (excluding `id` and `__closure`). Same object sent twice produces the same id — deduplication is automatic.
- **Detachment**: objects larger than a threshold (configurable, default 1KB) are stored separately and replaced with a reference `{ "referencedId": "abc123" }`. This is the detach pattern.
- **Closures**: built by the serialiser — a flat map of every descendant object id and its depth. Server uses this for efficient bulk fetch.
- **`speckle_type` field**: kept as-is for wire compatibility with the ORBIT server (which is still the Speckle server under the hood). Eventually rename when we fork the server further.

### Transport Layer

```csharp
public interface IOrbitTransport
{
    Task<string> SaveObjectAsync(string objectJson, CancellationToken ct = default);
    Task<string> GetObjectAsync(string objectId, CancellationToken ct = default);
    Task<bool> HasObjectAsync(string objectId, CancellationToken ct = default);
    Task SaveObjectBatchAsync(IEnumerable<(string id, string json)> objects, CancellationToken ct = default);
}
```

`ServerTransport` POSTs to `/objects/{projectId}` in batches (100 objects or 1MB, whichever comes first). GETs individual objects or uses `/objects/{projectId}/{rootId}/single` for selective fetch guided by the closure table.

---

## 5. ORBIT Rhino 8 Connector — Full Structure

### Project: `OrbitConnector.Rhino`

Target: `net8.0-windows`  
Output: `OrbitConnector.Rhino.rhp`  
References: `RhinoCommon.dll`, `Eto.dll`, `RhinoWindows.dll` (all `Private=false`, NOT copied to output — Rhino provides them at runtime)  
NuGet: `Orbit.Sdk` (our own, from local feed or GitHub Packages), `Newtonsoft.Json`

### Folder Structure

```
orbit-rhino/
├── src/
│   └── OrbitConnector.Rhino/
│       ├── OrbitConnector.Rhino.csproj
│       ├── OrbitConnectorPlugin.cs        # Plugin entry point, LoadTime.AtStartup
│       │
│       ├── Commands/
│       │   └── OrbitOpenPanelCommand.cs   # "Orbit" command opens the panel
│       │
│       ├── HostApp/
│       │   ├── RhinoHostAppService.cs     # Doc events, active doc, layer access
│       │   └── RhinoDocumentStore.cs      # CardStore backed by RhinoDoc.Strings
│       │
│       ├── Converters/
│       │   ├── ConversionContext.cs       # Shared state during a conversion pass
│       │   ├── ToOrbit/                   # Rhino geometry → ORBIT objects
│       │   │   ├── IRhinoToOrbitConverter.cs
│       │   │   ├── RhinoMeshConverter.cs
│       │   │   ├── RhinoBrepConverter.cs       # Brep → Orbit.Brep
│       │   │   ├── RhinoCurveConverter.cs      # Dispatches by curve type
│       │   │   ├── RhinoNurbsCurveConverter.cs
│       │   │   ├── RhinoPolylineConverter.cs
│       │   │   ├── RhinoArcConverter.cs
│       │   │   ├── RhinoCircleConverter.cs
│       │   │   ├── RhinoLineConverter.cs
│       │   │   ├── RhinoPointConverter.cs
│       │   │   ├── RhinoTextConverter.cs
│       │   │   ├── RhinoHatchConverter.cs
│       │   │   ├── RhinoPointCloudConverter.cs
│       │   │   ├── RhinoInstanceConverter.cs   # Block instances → Instance + DefinitionProxy
│       │   │   └── RhinoFallbackConverter.cs   # Any geometry → display Mesh
│       │   └── ToRhino/                   # ORBIT objects → Rhino geometry
│       │       ├── IOrbitToRhinoConverter.cs
│       │       ├── OrbitMeshToRhino.cs
│       │       ├── OrbitBrepToRhino.cs
│       │       ├── OrbitCurveToRhino.cs
│       │       ├── OrbitNurbsCurveToRhino.cs
│       │       ├── OrbitLineToRhino.cs
│       │       ├── OrbitPointToRhino.cs
│       │       ├── OrbitArcToRhino.cs
│       │       ├── OrbitCircleToRhino.cs
│       │       ├── OrbitTextToRhino.cs
│       │       ├── OrbitHatchToRhino.cs
│       │       └── OrbitFallbackToRhino.cs    # Render displayValue mesh if type unknown
│       │
│       ├── Pipeline/
│       │   ├── RhinoSendPipeline.cs       # Extracts, converts, sends, creates version
│       │   ├── RhinoReceivePipeline.cs    # Fetches, deserialises, bakes to doc
│       │   └── ProgressReporter.cs        # Reports progress back to UI
│       │
│       ├── Baking/
│       │   ├── RhinoLayerBaker.cs         # Creates/finds layer hierarchy
│       │   ├── RhinoMaterialBaker.cs      # Applies RenderMaterialProxy → Rhino material
│       │   ├── RhinoColorBaker.cs         # Applies ColorProxy → object colour
│       │   ├── RhinoGroupBaker.cs         # Creates Rhino groups from GroupProxy
│       │   └── RhinoInstanceBaker.cs      # Reconstructs blocks from DefinitionProxy
│       │
│       ├── Auth/
│       │   ├── OrbitAuthManager.cs        # OAuth2 PKCE, local HttpListener callback
│       │   └── OrbitTokenStore.cs         # Persists via Rhino plugin settings
│       │
│       ├── UI/
│       │   ├── OrbitEtoPanel.cs           # Main Eto.Forms dockable panel
│       │   ├── Controls/
│       │   │   ├── CardListControl.cs     # Shows send/receive cards
│       │   │   ├── CardConfigControl.cs   # Edit a card
│       │   │   ├── LayerTreeControl.cs    # Layer selector
│       │   │   ├── ProjectPickerControl.cs
│       │   │   └── ModelPickerControl.cs
│       │   └── Theme/
│       │       ├── OrbitTheme.cs          # Dark/light colours
│       │       └── OrbitIcons.cs          # Embedded SVG/PNG icons
│       │
│       ├── Models/
│       │   ├── ConnectorCard.cs           # Send or Receive card definition
│       │   ├── CardStore.cs               # Persists cards in RhinoDoc.Strings
│       │   └── ServerTarget.cs            # Prod / Dev server enum + URLs
│       │
│       ├── Services/
│       │   └── OrbitApiService.cs         # Thin wrapper around Orbit.Sdk.OrbitClient
│       │
│       ├── installer/
│       │   ├── OrbitConnector.iss         # Inno Setup script
│       │   └── Build-Installer.ps1        # PowerShell: MSBuild + Inno → .exe
│       │
│       └── Resources/
│           ├── orbit-icon-16.png
│           ├── orbit-icon-32.png
│           └── orbit-logo.svg
│
├── tests/
│   └── OrbitConnector.Rhino.Tests/
│
└── OrbitConnector.Rhino.sln
```

---

## 6. Conversion Pipeline — Detail

### Send (Rhino → ORBIT Server)

```
1. EXTRACT
   ├── Get selected objects (or all, or by layer filter per card)
   ├── Read layer hierarchy → will become ORBIT Collection structure
   └── Read block definitions → will become DefinitionProxy list

2. CONVERT (per object)
   ├── Dispatch to correct IRhinoToOrbitConverter by GeometryType
   ├── RhinoFallbackConverter runs if primary converter fails or type unsupported
   │   └── Extracts display mesh as Orbit.Mesh with displayValue semantics
   ├── Attach properties: UserStrings, UserDictionary → OrbitBase.DynamicProperties
   └── Record applicationId (Rhino object GUID as string)

3. ASSEMBLE TREE
   ├── Root: OrbitObject (Collection) named after project + model
   ├── One child OrbitObject per Rhino layer (nested = nested collections)
   ├── Geometry objects sit under their layer collection
   ├── Proxies appended at root: RenderMaterialProxy[], ColorProxy[], GroupProxy[], DefinitionProxy[]
   └── Block instances become Instance objects referencing DefinitionProxy.applicationId

4. SERIALISE
   ├── OrbitSerializer walks tree depth-first
   ├── Computes SHA256 id for each object
   ├── Detaches objects > threshold → stored separately, referenced by id
   └── Builds __closure table on root object

5. TRANSPORT
   ├── ServerTransport.HasObjectAsync — skip already-uploaded objects (dedup)
   ├── Batch-upload new objects (POST /objects/{projectId})
   └── Progress reported back to UI after each batch

6. CREATE VERSION
   └── GraphQL mutation: modelMutations.create(input: { modelId, objectId, message, sourceApplication: "OrbitRhino" })
```

### Receive (ORBIT Server → Rhino)

```
1. FETCH VERSION
   └── GraphQL: project.model.version → get rootObjectId

2. READ CLOSURES
   └── GET /objects/{projectId}/{rootObjectId} → parse __closure table
       This gives us a flat list of every child object id

3. FETCH OBJECTS
   ├── Batch GET all object ids in closure (parallelised, 8 concurrent)
   └── Deserialise each to OrbitBase subtype

4. UNPACK GRAPH
   ├── Resolve detached references (referencedId → actual object)
   ├── Build layer tree from Collection hierarchy
   └── Extract proxies (materials, colours, groups, definitions, instances)

5. CONVERT (per object)
   ├── Dispatch to correct IOrbitToRhinoConverter by orbit type
   ├── OrbitFallbackToRhino: if type unknown, try to render displayValue mesh
   └── ReceiveMode: Update (match by applicationId) | Create (always new) | Ignore (skip if exists)

6. BAKE TO DOC
   ├── RhinoLayerBaker: create layer hierarchy (find-or-create, no duplicates)
   ├── RhinoInstanceBaker: register block definitions, place instances
   ├── RhinoMaterialBaker: create/find Rhino materials, assign to objects
   ├── RhinoGroupBaker: create Rhino groups matching GroupProxy
   └── Add objects to doc, assign to layers
```

---

## 7. UI Approach

We keep the **Eto.Forms dockable panel** approach from the existing connector. Eto works cross-platform (Windows + Mac Rhino) and we already understand it.

**Phase 1 (launch):** Pure Eto panel with custom-drawn controls using the ORBIT design language.

**Phase 2 (future):** Hybrid approach — Eto panel hosts a WebView component for the card UI, with a C# `BrowserBridge` class exposing typed methods to JavaScript. This enables a richer UI built in Vue/React while keeping the Eto shell for Rhino integration.

### Panel Navigation

```
OrbitEtoPanel
├── [Auth not set]  → Login screen (server target selector, OAuth button)
└── [Auth set]
    ├── Home        → Card list (send cards + receive cards)
    │   ├── [+ Send]  → Create send card flow
    │   └── [+ Receive] → Create receive card flow
    ├── CardConfig  → Edit card (project, model, layer filter, version pin)
    └── Progress    → Live progress during send/receive
```

### Card Persistence

Cards are stored as JSON in `RhinoDoc.Strings["orbit_connector"]["cards"]` — the same elegant pattern from the existing REBUS connector. Cards travel with the .3dm file. When a doc is opened, cards are loaded and the UI populates automatically.

---

## 8. Auth

OAuth2 with PKCE (same flow as existing, but cleaned up):

1. User clicks "Connect" and selects prod or dev server
2. Connector opens system browser to `https://speckle.rebus.industries/authn/verify/{appId}/{challenge}`
3. Local `HttpListener` on port 29364 waits for redirect callback
4. Access code exchanged for token via `POST /auth/token`
5. Token validated via `activeUser { id name email }` GraphQL query
6. Token stored in Rhino plugin settings keyed by server URL (using stable hash)

App IDs remain the same (already registered on our server): prod `c0c8e773a3`, dev `c047ac8afa`.

---

## 9. Installer

Same approach as existing — Inno Setup, installs to the correct Rhino packages location.

**Install path:** `%APPDATA%\McNeel\Rhinoceros\packages\8.0\OrbitConnector\{version}\OrbitConnector.Rhino.rhp`

**`Build-Installer.ps1`** builds two variants:
- `Release` → production server only
- `Debug` → both prod + dev server targets available in UI

**GitHub Actions** triggers the installer build on any `v*.*.*` tag and attaches the `.exe` to the GitHub Release as a downloadable artefact.

---

## 10. Infrastructure Changes

### Rename services (low priority, do after connector works)

| Current | Target |
|---|---|
| `speckle.rebus.industries` | Keep (or add `orbit.rebus.industries` as alias) |
| `speckle-dev.rebus.industries` | Keep as-is for dev |
| Image: `speckle-frontend-2-rebus:v2.3.0` | `orbit-frontend:v1.0.0` |
| Image: `speckle-server-rebus:v2.1.0` | `orbit-server:v1.0.0` |
| Image: `speckle-preview-service-rebus:v2.1.0` | `orbit-preview:v1.0.0` |

### Outstanding security & reliability (do these now)

1. **Rotate `SESSION_SECRET`** — move to `.env` file, never commit to Git
2. **Rotate MinIO credentials** — update in `docker-compose.yml` env ref
3. **Configure automated backups** — PostgreSQL dump + MinIO bucket sync to a Proxmox backup location (PBS or NFS)
4. **Auto-start `rh_watcher.ps1`** — create Windows Task Scheduler entry on RB-DA2-PC01 to start on login/boot
5. **Fix 3DConvert Docker image** — currently hot-patched; rebuild from clean Dockerfile and push to `orbit-platform` registry
6. **Add `/explorer` Caddy route** — if the object explorer endpoint needs to be exposed externally
7. **Webhook receiver on VMs** — so GitHub Actions can trigger `docker compose pull && up` on tag push

---

## 11. Build Sequence (What to Build First)

This is the recommended order — each phase produces something testable.

### Phase 1 — SDK Core (2-3 days)
- `OrbitBase`, `OrbitObject`, all geometry types in `Orbit.Objects`
- `OrbitSerializer` + `HashingService` (deterministic ids)
- `LocalTransport` (write objects to disk — no server needed yet)
- Unit tests proving round-trip serialisation

### Phase 2 — Server Transport + API Client (1-2 days)
- `ServerTransport` — batch upload/download to/from server
- `OrbitGraphQLClient` — projects, models, versions CRUD
- `OrbitClient` — unified entry point
- Integration tests against dev server

### Phase 3 — Rhino Plugin Shell (1 day)
- `OrbitConnectorPlugin.cs` — plugin loads, panel registers
- `OrbitEtoPanel` — bare-bones panel, login flow, shows user name
- `OrbitTokenStore` + `OrbitAuthManager` — full OAuth round-trip
- CardStore backed by `RhinoDoc.Strings`

### Phase 4 — Send Pipeline (3-5 days)
- All `ToOrbit` converters (mesh first, then curves, then Brep, then instances)
- `RhinoSendPipeline` — wires extraction → conversion → serialise → send
- Progress reporting in UI
- Verify objects appear correctly in ORBIT server viewer

### Phase 5 — Receive Pipeline (3-5 days)
- All `ToRhino` converters
- Layer baker, material baker, instance baker
- `RhinoReceivePipeline`
- Test full round-trip: send from Rhino → view in server → receive back to Rhino

### Phase 6 — Polish + Installer (1-2 days)
- ORBIT branding, icons, theme
- Inno Setup installer
- GitHub Actions CI/CD pipeline
- First tagged release

---

## 12. What We Keep From the REBUS Connector

These patterns were good and should be ported (not copied) into ORBIT:

| Pattern | REBUS source | ORBIT equivalent |
|---|---|---|
| OAuth PKCE flow | `SpeckleAuth.cs` | `OrbitAuthManager.cs` (rewrite, same logic) |
| Card persistence in .3dm | `CardStore.cs` | `CardStore.cs` (direct port, minimal changes) |
| Prod/dev target switching | `TokenStore.ThemeMode` | `ServerTarget` enum in `OrbitTokenStore` |
| Layer tree UI | `RebusEtoPanel` | `LayerTreeControl` in ORBIT panel |
| Inno Setup installer | `RebusConnector.iss` | `OrbitConnector.iss` (update paths/names) |
| Build-Installer.ps1 | `Build-Installer.ps1` | Port directly, update project names |
| Dark/light theme | `RebusEtoPanel` | `OrbitTheme.cs` |

**What we do NOT keep:**
- `ConvertApiClient.cs` (file-based 3DConvert pipeline) — replaced entirely by in-process converters
- `SpeckleGraphQL.cs` raw HTTP calls — replaced by `OrbitGraphQLClient` in SDK
- `RhinoExport.cs` / `RhinoImport.cs` (temp .3dm file round-trip) — deleted entirely

---

## 13. PRISM — File Conversion Service

PRISM is a standalone Python service (successor to the 3DConvert service) that handles file formats ORBIT connectors cannot handle natively. It runs as a separate Docker container on VM 201 alongside the ORBIT stack, exposed at `convert.rebus.industries` (or eventually `prism.rebus.industries`).

### What PRISM handles
- DWG → ORBIT objects (via Rhino headless on RB-DA2-PC01, or via RhinoCompute)
- FBX → ORBIT objects
- IFC → ORBIT objects (eventually — IFC is high value)
- OBJ / STL → ORBIT objects

### What PRISM does NOT handle
- Rhino `.3dm` files converted natively by the ORBIT Rhino Connector
- Any geometry that can be sent directly via a connector

### PRISM architecture

```
PRISM Service (Python/FastAPI)
├── POST /convert/async          — submit file + target format + orbit server URL
├── GET  /jobs/{jobId}           — poll status
└── Internal:
    ├── Job queue (Redis/Valkey — share with ORBIT stack)
    ├── Worker: calls RhinoCompute or rh_watcher.ps1 for DWG
    ├── Converts intermediate geometry → ORBIT object JSON
    └── POSTs resulting objects directly to ORBIT server transport endpoint
```

### PRISM Git repo: `orbit-platform/prism`

```
prism/
├── app/
│   ├── main.py                  # FastAPI app
│   ├── routers/
│   │   ├── convert.py
│   │   └── jobs.py
│   ├── workers/
│   │   ├── dwg_worker.py        # Calls RhinoCompute or rh_watcher
│   │   ├── fbx_worker.py
│   │   └── obj_worker.py
│   ├── converters/
│   │   └── mesh_to_orbit.py     # Geometry → ORBIT JSON schema
│   └── orbit_client.py          # Uploads objects to ORBIT server
├── Dockerfile
├── docker-compose.yml           # For local dev
└── tests/
```

### Relationship between PRISM and ORBIT Rhino Connector

The Rhino Connector does **not** call PRISM. If you're working in Rhino, you use the connector directly — native geometry, no intermediate file. PRISM is used for:

- Receiving DWG/FBX files from consultants with no connector
- Bulk file import workflows (drag-and-drop or API)
- Future: a web UI upload flow on the ORBIT platform

---

## 14. Summary

The full ORBIT platform is five Git repos. The server repo evolves our existing custom Speckle server under a new name. The SDK provides the C# foundation — object model, serialisation, transport — that all connectors build on. The Rhino connector delivers the first user-facing connector product: in-process geometry conversion with full material, layer, and block fidelity. PRISM runs alongside as a specialist file ingestion service for formats that can't be handled natively. The proxy and infra repos hold everything needed to deploy and keep the platform running on our Proxmox cluster.

The old 3DConvert approach — exporting a .3dm and round-tripping it through an API — is retired entirely from the connector pipeline and replaced by PRISM (which does the same job but correctly, converting geometry to ORBIT objects rather than shuttling file formats).
