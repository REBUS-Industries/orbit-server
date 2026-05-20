# ORBIT Platform

**ORBIT** is a self-hosted 3D data platform for architectural and engineering workflows, built and operated by REBUS Industries. It provides object-based geometry exchange, a web viewer, version-controlled model data, and a file conversion pipeline — running entirely on REBUS infrastructure with no third-party cloud dependency.

---

## Repositories

| Repo | Language | Purpose |
|---|---|---|
| [ORBIT-SDK](https://github.com/REBUS-ORBIT/ORBIT-SDK) | C# / .NET 8 | Core object model, serialisation, transport, GraphQL API client |
| [ORBIT-Connectors](https://github.com/REBUS-ORBIT/ORBIT-Connectors) | C# / .NET 8 | Host-application plugins — Rhino 8 connector (more planned) |
| [ORBIT-Server](https://github.com/REBUS-ORBIT/ORBIT-Server) | Docker | Server stack — API, viewer, object storage, auth |
| [ORBIT-PRISM](https://github.com/REBUS-ORBIT/prism) | Python 3.12 | File conversion pipeline — DWG, FBX, IFC, OBJ → ORBIT objects |

---

## Products

### ORBIT
The full data platform. Provides:
- GraphQL API for projects, models, versions, and objects
- 3D web viewer with iFrame embedding support
- Object storage (S3-compatible via MinIO)
- OAuth2 authentication
- Webhook and file import services

Live endpoints:
- `speckle.rebus.industries` — production
- `speckle-dev.rebus.industries` — development / staging

### PRISM
Specialist file conversion pipeline. Converts file formats that ORBIT connectors cannot handle natively (DWG, FBX, IFC, OBJ) into ORBIT objects and pushes them directly to the server.

Live endpoint: `convert.rebus.industries`

---

## Documentation in this folder

| File | Contents |
|---|---|
| `README.md` | This file — platform overview and repo index |
| `ARCHITECTURE.md` | Full technical architecture — object model, serialisation, transport, pipeline design |
| `ROADMAP.md` | All planned work broken down by component and phase, with current status |
| `DEV-SETUP.md` | Developer environment setup for all four repos |

---

## Infrastructure

Hosted on a 3-node Proxmox cluster. Full infrastructure documentation is in the `Server/` folder. See [ARCHITECTURE.md](ARCHITECTURE.md) for the topology diagram.

---

## Status

Active development. The SDK and connector scaffolds are in place. See [ROADMAP.md](ROADMAP.md) for current build status and priorities.
