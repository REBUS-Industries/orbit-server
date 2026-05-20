# ORBIT — Roadmap

Status indicators: ✅ Done · 🔨 Scaffolded (builds, not complete) · 🔲 Planned · ⚠️ Blocked

---

## ORBIT-SDK

### Phase 1 — Object Model & Serialisation
| Task | Status | Notes |
|---|---|---|
| `OrbitBase` — id, applicationId, speckle_type, closure, dynamic properties | ✅ | |
| `OrbitObject` — name, displayValue, elements, units | ✅ | |
| `Mesh` — vertices, faces (variable-length), normals, textureCoords, colors | ✅ | |
| `Point`, `Line`, `Polyline` | ✅ | |
| `Arc`, `Circle`, `Plane` | ✅ | |
| `NurbsCurve` — degree, points, weights, knots | ✅ | |
| `PolyCurve` — mixed segment types | ✅ | |
| `Brep` — encoded + displayValue mesh | ✅ | |
| `Surface` — NURBS control point grid | ✅ | |
| `Instance` — definitionId + 4×4 transform | ✅ | |
| `PointCloud` — flat points, colors, normals | ✅ | |
| `Text`, `Hatch`, `Ellipse`, `Box`, `Extrusion`, `SubD` | 🔲 | Add as Rhino converter work begins |
| `RenderMaterialProxy`, `ColorProxy`, `GroupProxy`, `DefinitionProxy` | ✅ | |
| `Vector3d`, `Transform`, `Interval` | ✅ | |
| `OrbitSerializer` — SHA-256 content hashing | 🔨 | Written, needs unit test run |
| `OrbitSerializer` — detachment (objects > threshold stored separately) | 🔨 | |
| `OrbitSerializer` — closure table builder | 🔨 | |
| `OrbitDeserializer` — type registry, dispatch by speckle_type | 🔨 | |
| `OrbitDeserializer` — reference resolution (referencedId → object) | 🔨 | |
| `OrbitJsonSettings` — camelCase, null-ignore, no type names | ✅ | |
| Unit tests — serialisation round-trip | 🔨 | SerialisationTests.cs scaffolded |
| Unit tests — deterministic hash (same content → same id) | 🔨 | |
| Unit tests — detachment threshold | 🔲 | |

### Phase 2 — Transport & API Client
| Task | Status | Notes |
|---|---|---|
| `IOrbitTransport` interface | ✅ | |
| `LocalTransport` — disk-based, one file per object | ✅ | |
| `ServerTransport` — batch POST to `/objects/{id}` | 🔨 | Written, needs integration test |
| `ServerTransport` — deduplication via HEAD before upload | 🔨 | |
| `ServerTransport` — progress reporting | ✅ | |
| `OrbitGraphQLClient` — HttpClient-based, path navigation | 🔨 | |
| `OrbitQueries` — activeUser, projects, models, versions | ✅ | |
| `OrbitClient` — typed entry point wrapping GraphQL client | 🔨 | |
| Integration tests against DEV server | 🔲 | Needs dev server running |
| GitHub Packages NuGet publish (CI on tag) | 🔨 | Workflow written, not run |

---

## ORBIT-Connectors (Rhino 8)

### Phase 3 — Plugin Shell & Auth
| Task | Status | Notes |
|---|---|---|
| `OrbitConnectorPlugin` — LoadTime.AtStartup, panel registration | 🔨 | Written |
| `OrbitOpenPanelCommand` — "Orbit" command | 🔨 | Written |
| `OrbitEtoPanel` — basic Eto dockable panel, dark theme | 🔨 | Stub written |
| `OrbitAuthManager` — OAuth2 PKCE, HttpListener callback | 🔨 | Written |
| `OrbitTokenStore` — token persistence in Rhino plugin settings | 🔨 | Written |
| Plugin loads in Rhino 8 without errors | 🔲 | First build test needed |
| Panel opens when "Orbit" command run | 🔲 | |
| OAuth flow completes against prod server | 🔲 | |
| Logged-in user name shown in panel | 🔲 | |

### Phase 4 — Card UX
| Task | Status | Notes |
|---|---|---|
| `ConnectorCard` model — type, project, model, layer filter, history | ✅ | |
| `CardStore` — persists cards in RhinoDoc.Strings, travels with .3dm | ✅ | |
| `ServerConfig` — prod/dev URLs and OAuth app IDs | ✅ | |
| Card list control — shows all send/receive cards in panel | 🔲 | |
| Add Send card flow — project picker → model picker → layer filter | 🔲 | |
| Add Receive card flow — project picker → model picker → version pin | 🔲 | |
| Card config control — edit existing card | 🔲 | |
| Layer tree control — browse and select Rhino layers | 🔲 | |
| Project picker — lists ORBIT projects from server | 🔲 | |
| Model picker — lists models for selected project | 🔲 | |
| Cards reload automatically when .3dm opened | 🔲 | |

### Phase 5 — Send Pipeline
| Task | Status | Notes |
|---|---|---|
| `ConversionContext` — shared state, units, proxy collections | ✅ | |
| `IRhinoToOrbitConverter` interface | ✅ | |
| `RhinoMeshConverter` — vertices, faces, normals, vertex colours | ✅ | |
| `RhinoBrepConverter` — Brep + display mesh always attached | ✅ | |
| `RhinoFallbackConverter` — Mesh.CreateFromBrep for any geometry | ✅ | |
| `RhinoCurveConverter` — dispatch by curve type | 🔲 | |
| `RhinoNurbsCurveConverter` | 🔲 | |
| `RhinoPolylineConverter` | 🔲 | |
| `RhinoLineConverter` | 🔲 | |
| `RhinoArcConverter` | 🔲 | |
| `RhinoCircleConverter` | 🔲 | |
| `RhinoPointConverter` | 🔲 | |
| `RhinoTextConverter` | 🔲 | |
| `RhinoHatchConverter` | 🔲 | |
| `RhinoInstanceConverter` — Instance + DefinitionProxy | 🔲 | |
| `RhinoMaterialConverter` — RenderMaterialProxy from Rhino materials | 🔲 | |
| `RhinoColorConverter` — ColorProxy from object/layer colours | 🔲 | |
| `RhinoGroupConverter` — GroupProxy from Rhino groups | 🔲 | |
| `RhinoSendPipeline` — extract → convert → assemble → serialise → upload | 🔨 | Written, not tested end-to-end |
| Progress reporting to panel UI during send | 🔲 | |
| End-to-end test: send Rhino mesh → verify in ORBIT viewer | 🔲 | |
| End-to-end test: send Brep → verify display mesh in viewer | 🔲 | |
| End-to-end test: send blocks → verify instance placement | 🔲 | |

### Phase 6 — Receive Pipeline
| Task | Status | Notes |
|---|---|---|
| `IOrbitToRhinoConverter` interface | 🔲 | |
| `OrbitMeshToRhino` | 🔲 | |
| `OrbitBrepToRhino` | 🔲 | |
| `OrbitNurbsCurveToRhino` | 🔲 | |
| `OrbitPolylineToRhino` | 🔲 | |
| `OrbitLineToRhino` | 🔲 | |
| `OrbitArcToRhino` | 🔲 | |
| `OrbitCircleToRhino` | 🔲 | |
| `OrbitPointToRhino` | 🔲 | |
| `OrbitTextToRhino` | 🔲 | |
| `OrbitFallbackToRhino` — render displayValue mesh for unknown types | 🔲 | |
| `RhinoLayerBaker` — find-or-create layer hierarchy | 🔲 | |
| `RhinoMaterialBaker` — RenderMaterialProxy → Rhino material | 🔲 | |
| `RhinoColorBaker` — ColorProxy → object colour attribute | 🔲 | |
| `RhinoGroupBaker` — GroupProxy → Rhino group | 🔲 | |
| `RhinoInstanceBaker` — DefinitionProxy → BlockDefinition, Instance → placed block | 🔲 | |
| `RhinoReceivePipeline` — fetch → deserialise → convert → bake | 🔲 | |
| ReceiveMode: Update (by applicationId), Create (always new), Ignore | 🔲 | |
| Progress reporting to panel UI during receive | 🔲 | |
| End-to-end test: receive what was sent, geometry matches | 🔲 | |

### Phase 7 — Polish & Release
| Task | Status | Notes |
|---|---|---|
| ORBIT branding — icons (16px, 32px, SVG logo) | 🔲 | |
| Dark/light theme — `OrbitTheme.cs` with colour tokens | 🔲 | |
| Progress panel — live status during send/receive | 🔲 | |
| Error handling — user-facing error messages in panel | 🔲 | |
| Version history in receive card — pick from past versions | 🔲 | |
| Inno Setup installer — installs to correct Rhino packages path | 🔨 | Script written |
| `Build-Installer.ps1` — MSBuild + Inno Setup | 🔨 | Script written |
| GitHub Actions CI — build + test on PR | 🔨 | Workflow written |
| GitHub Actions release — build `.rhp` + installer on tag | 🔨 | Workflow written, not run |
| First tagged release v1.0.0 | 🔲 | |

---

## ORBIT-Server

### Docker Stack
| Task | Status | Notes |
|---|---|---|
| `docker-compose.yml` — all services with env vars from `.env` | ✅ | |
| `.env.example` — full variable reference | ✅ | |
| Internal `Caddyfile` — routes within Docker stack | ✅ | |
| Rename Docker images from `speckle-*-rebus` → `orbit-*` | 🔲 | Rebuild from patched source |
| Push orbit-server, orbit-frontend, orbit-preview to GHCR | 🔲 | |
| Rotate `SESSION_SECRET` — move to `.env`, never in compose | 🔲 | **Security — do first** |
| Rotate MinIO credentials | 🔲 | **Security** |
| Configure automated PostgreSQL backups | 🔲 | Proxmox PBS or NFS target |
| Configure MinIO bucket replication / backup | 🔲 | |
| GitHub Actions deploy workflow — SSH on tag push | ✅ | `deploy.yml` written |
| Add `PROD_VM_SSH_KEY`, `PROD_VM_HOST`, `PROD_VM_USER` to repo secrets | 🔲 | Manual step |
| Add DEV VM secrets | 🔲 | |
| Deploy webhook receiver on VMs (`/opt/orbit-server/scripts/deploy.sh`) | 🔲 | |
| Set up `orbit-server` deploy path on VM 201 | 🔲 | `git clone` → `/opt/orbit-server` |
| Auto-start `rh_watcher.ps1` on RB-DA2-PC01 via Task Scheduler | 🔲 | Currently manual |

### External Proxy (HA Caddy pair)
| Task | Status | Notes |
|---|---|---|
| Update external Caddyfile — serve `orbit.rebus.industries` (new URL alias) | 🔲 | Optional |
| Verify `X-Frame-Options` removal still works after stack rename | 🔲 | iFrame embedding |
| Verify CSP `frame-ancestors` header still correct | 🔲 | |

---

## ORBIT-PRISM

### Core Service
| Task | Status | Notes |
|---|---|---|
| FastAPI app — `/convert/async`, `/jobs/{id}`, `/health` | ✅ | |
| Job store (in-memory) | ✅ | Replace with Redis for multi-instance |
| Worker dispatcher — routes by file extension | ✅ | |
| `OrbitUploader` — batch object upload + version creation | ✅ | |
| OBJ / STL worker — trimesh → ORBIT Mesh JSON | ✅ | Functional |
| DWG worker — stub, dispatches to RhinoCompute | 🔨 | RhinoCompute integration pending |
| FBX worker | 🔲 | Assess trimesh or Blender CLI |
| IFC worker | 🔲 | Assess IfcOpenShell |
| `Dockerfile` — Python 3.12-slim | ✅ | |
| GitHub Actions — test + Docker build + push to GHCR on tag | ✅ | Workflow written |
| Deploy to VM 201 alongside ORBIT stack | 🔲 | Add to server docker-compose |
| Add PRISM service to `ORBIT-Server/docker-compose.yml` | 🔲 | |
| Replace in-memory job store with Redis | 🔲 | Use existing Valkey container |
| DWG worker — full RhinoCompute integration | 🔲 | Needs RhinoCompute Grasshopper def for DWG import |
| End-to-end test: upload OBJ → verify in ORBIT viewer | 🔲 | |
| End-to-end test: upload DWG → verify mesh geometry | 🔲 | |

---

## Priority Order (what to do next)

**Immediate — unblock development:**
1. `dotnet build ORBIT-SDK.sln` — fix any compile errors
2. Run serialisation unit tests — verify hash determinism
3. `dotnet build ORBIT-Connectors.sln` with `ORBIT_SDK_LOCAL=1` — plugin must load in Rhino
4. Rotate `SESSION_SECRET` and MinIO credentials on server — security fix

**Short term — first working send:**
5. Wire `RhinoCurveConverter` (dispatches to Line/Arc/Circle/NurbsCurve/Polyline)
6. Wire `RhinoInstanceConverter` + `RhinoMaterialConverter`
7. Complete `RhinoSendPipeline` — test with simple Rhino scene
8. Verify objects appear correctly in ORBIT web viewer

**Medium term — full round-trip:**
9. All `ToRhino` converters
10. All bakers (layer, material, group, instance)
11. `RhinoReceivePipeline` end-to-end

**Parallel — server & PRISM:**
12. Rebuild and rename Docker images → push to GHCR
13. Deploy PRISM to VM 201 (add to server docker-compose)
14. DWG worker — RhinoCompute integration
