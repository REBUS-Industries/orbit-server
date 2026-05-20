# ORBIT — Developer Setup

## Prerequisites

| Tool | Version | Purpose |
|---|---|---|
| .NET SDK | 8.0+ | SDK and connector build |
| Rhino | 8 (latest) | Connector testing |
| Visual Studio 2022 | 17.8+ | Recommended IDE for C# |
| Docker Desktop | any recent | Local ORBIT server |
| Python | 3.12 | PRISM development |
| Git | any | Version control |

---

## Repository Layout

Clone all four repos into the same parent folder. The connector's local dev mode (`ORBIT_SDK_LOCAL=1`) looks for the SDK at `../SDK/` relative to the Connectors folder.

```
ORBIT/
├── SDK/            ← ORBIT-SDK repo
├── Connectors/     ← ORBIT-Connectors repo
├── Server/         ← ORBIT-Server repo
└── PRISM/          ← ORBIT-PRISM repo
```

---

## ORBIT-SDK

### First build

```bash
cd SDK
dotnet restore ORBIT-SDK.sln
dotnet build ORBIT-SDK.sln
dotnet test ORBIT-SDK.sln
```

### Publishing NuGet packages locally (for Connectors dev)

```bash
dotnet pack src/Orbit.Objects/Orbit.Objects.csproj -c Release -o ./nupkgs
dotnet pack src/Orbit.Sdk/Orbit.Sdk.csproj -c Release -o ./nupkgs
```

The Connectors `nuget.config` already points to `../SDK/nupkgs` as a local feed.

---

## ORBIT-Connectors

### First build

```powershell
cd Connectors

# Use local SDK project references (no NuGet publish needed)
$env:ORBIT_SDK_LOCAL = "1"

dotnet restore ORBIT-Connectors.sln
dotnet build ORBIT-Connectors.sln
```

The `.rhp` file is written alongside the `.dll` in:
```
src/OrbitConnector.Rhino/bin/Debug/net8.0-windows/OrbitConnector.Rhino.rhp
```

### Installing into Rhino for testing

Copy the `.rhp` and the SDK `.dll` files to:
```
%APPDATA%\McNeel\Rhinoceros\packages\8.0\OrbitConnector\1.0.0\
```

Or drag-and-drop the `.rhp` directly into a running Rhino window.

Restart Rhino, then run the `Orbit` command to open the panel.

### Building the installer

```powershell
cd src/OrbitConnector.Rhino/installer
.\Build-Installer.ps1 -Version "1.0.0"
```

Requires [Inno Setup 6](https://jrsoftware.org/isdl.php) installed at the default path.

### SDK reference modes

| Mode | When to use | How to activate |
|---|---|---|
| Local project refs | Active SDK development | `$env:ORBIT_SDK_LOCAL = "1"` |
| NuGet packages | CI, released builds | Default (no env var) |

Set `OrbitSdkLocal=true` permanently in `Directory.Build.props` during extended SDK work.

---

## ORBIT-Server (local Docker stack)

### First run

```bash
cd Server
cp .env.example .env
# Edit .env — fill in passwords, set SERVER_URL=http://localhost:80

docker compose up -d
```

The stack starts on port 80. Access:
- Viewer: http://localhost
- GraphQL playground: http://localhost/graphql

### Stopping

```bash
docker compose down
```

Data persists in Docker named volumes. To wipe all data:

```bash
docker compose down -v
```

### Updating images

```bash
docker compose pull
docker compose up -d --remove-orphans
```

---

## ORBIT-PRISM

### Setup

```bash
cd PRISM
python -m venv .venv
source .venv/bin/activate        # Windows: .venv\Scripts\activate
pip install -r requirements.txt
```

### Running locally

```bash
uvicorn app.main:app --reload --port 8765
```

API docs available at http://localhost:8765/docs

### Running tests

```bash
pytest tests/ -v
```

### Docker build

```bash
docker build -t orbit-prism:local .
docker run -p 8765:8765 orbit-prism:local
```

---

## Environment Variables

### Connector (Rhino plugin settings)
Stored in Rhino's plugin settings — no `.env` file needed. The panel handles auth and target selection.

### Server
All variables documented in `Server/.env.example`. Copy to `Server/.env` and populate before running.

### PRISM
```
RHINOCOMPUTE_URL=http://compute.rebus.industries   # or local dev instance
PORT=8765
```

---

## CI / GitHub Actions

### GitHub Secrets to configure

**ORBIT-Server repo:**

| Secret | Value |
|---|---|
| `PROD_VM_HOST` | `10.0.200.11` |
| `PROD_VM_USER` | deploy user on VM 201 (e.g. `orbit`) |
| `PROD_VM_SSH_KEY` | Private SSH key (PEM) for that user |
| `DEV_VM_HOST` | `10.0.200.112` |
| `DEV_VM_USER` | deploy user on VM 301 |
| `DEV_VM_SSH_KEY` | Private SSH key for DEV VM |

**ORBIT-SDK repo (for NuGet publish):**

| Secret | Value |
|---|---|
| `GITHUB_TOKEN` | Auto-provided by GitHub Actions — no setup needed |

**ORBIT-Connectors repo (for reading SDK packages):**

Add to your local environment or CI:
```
NUGET_TOKEN=<PAT with read:packages scope on REBUS-ORBIT org>
```

---

## Git Workflow

```
main      ← protected, always deployable
develop   ← integration; PRs merge here first
feature/* ← new features (short-lived)
fix/*     ← bug fixes
release/* ← release prep (version bump, changelog)
```

### Making a release

```bash
git checkout main
git merge develop
git tag v1.0.0
git push origin v1.0.0
```

Pushing a `v*.*.*` tag triggers CI to:
- **SDK**: pack + publish NuGet to GitHub Packages
- **Connectors**: build `.rhp` + Inno Setup installer → GitHub Release
- **Server**: SSH deploy to prod VM
- **PRISM**: build + push Docker image to GHCR → SSH deploy to prod VM
