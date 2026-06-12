# ORBIT — Operations Runbook

This document is the primary reference for deploying, maintaining, and troubleshooting the ORBIT platform. It is written for use by both humans and AI agents operating autonomously.

**Read this before touching any running system.**

---

## Service Inventory

### VMs

| VM ID | Role | Internal IP | Hostname | Status |
|---|---|---|---|---|
| 201 | Legacy Speckle (keep running) | 10.0.200.11 | speckle-prod | Live — do not touch |
| 211 | ORBIT PROD + PRISM | 10.0.200.211 | orbit-prod | Needs provisioning |
| 212 | PRISM DEV | 10.0.200.212 | orbit-dev | Needs provisioning |

### External Proxy LXCs (HA Caddy pair)

| LXC ID | IP | Purpose |
|---|---|---|
| 251 | 10.0.1.51 | Primary external Caddy proxy |
| 252 | 10.0.1.52 | Secondary external Caddy proxy |

### Public Endpoints

| URL | Routes to | Purpose |
|---|---|---|
| `orbit.rebus.industries` | VM 211 | ORBIT production |
| `prism.rebus.industries` | VM 211:8765 | PRISM API (prod) |
| `prism-dev.rebus.industries` | VM 212:8765 | PRISM API (dev) |
| `speckle.rebus.industries` | VM 201 | Legacy — leave alone |

### Services per VM (Docker stack)

| Service | Image | Port (internal) | Health endpoint |
|---|---|---|---|
| `orbit-server` | `ghcr.io/cheekiskrub/orbit-server` | 3000 | `GET /readiness` |
| `orbit-frontend` | `ghcr.io/cheekiskrub/orbit-frontend` | 80 | — |
| `orbit-preview` | `ghcr.io/cheekiskrub/orbit-preview` | — | — |
| `postgres` | `postgres:16.9-alpine` | 5432 | `pg_isready` |
| `redis` | `valkey/valkey:8-alpine` | 6379 | `valkey-cli ping` |
| `minio` | `minio/minio` | 9000 (API), 9001 (console) | — |
| `webhook-service` | `speckle/speckle-webhook-service:2` | — | — |
| `fileimport-service` | `speckle/speckle-fileimport-service:2` | — | — |
| `prism` | `ghcr.io/rebus-orbit/orbit-prism` | 8765 | `GET /health` |

All traffic enters via the external Caddy pair (LXC 251/252). The internal `Caddyfile` routes within the Docker network; TLS terminates at the external proxy.

---

## Credentials and Access

### What you need before any deploy

1. **SSH private key** for VM 211 / VM 212 — stored as `PROD_VM_SSH_KEY` / `DEV_VM_SSH_KEY` in GitHub Secrets. Do not commit to Git. The key file is at `Infra/id_ed25519_rebus` locally (never pushed).
2. **GHCR read access** — images are pulled from `ghcr.io/rebus-orbit/*` and `ghcr.io/cheekiskrub/*`. No auth needed for public images; if private, Docker login uses `NUGET_TOKEN` (same PAT, read:packages scope).
3. **`.env` file** — must be present at `/opt/orbit/server/.env` on the VM before first start. Generated from `.env.example` — see Environment Setup below.

### GitHub Secrets already configured

| Repo | Secret | Value |
|---|---|---|
| orbit-server | `PROD_VM_HOST` | `10.0.200.211` |
| orbit-server | `PROD_VM_USER` | deploy user on VM 211 |
| orbit-server | `PROD_VM_SSH_KEY` | Private key PEM |
| orbit-server | `DEV_VM_HOST` | `10.0.200.212` |
| orbit-server | `DEV_VM_USER` | deploy user on VM 212 |
| orbit-server | `DEV_VM_SSH_KEY` | Private key PEM |
| orbit-sdk | `NUGET_TOKEN` | PAT with read/write:packages |
| orbit-connectors | `NUGET_TOKEN` | PAT with read:packages |

---

## Environment Setup (`.env`)

Before first boot, the `.env` file must be created from `.env.example`. **Never commit `.env` to Git.**

```bash
# On the VM, in the stack directory
cp .env.example .env
nano .env
```

Required values to fill in:

| Variable | How to generate | Example |
|---|---|---|
| `SESSION_SECRET` | `openssl rand -hex 32` | 64-char hex string |
| `POSTGRES_PASSWORD` | `openssl rand -base64 24` | strong random string |
| `MINIO_PASSWORD` | `openssl rand -base64 24` | strong random string |
| `SERVER_URL` | Fixed per VM | `https://orbit.rebus.industries` |

Leave email variables blank to disable email — the stack will still start.

**PRISM environment variables** (add to `.env`):
```
RHINOCOMPUTE_URL=http://compute.rebus.industries
ORBIT_SERVER_URL=http://orbit-server:3000
```

---

## First-Time Deploy (Fresh VM)

Run these steps after completing VM-SETUP.md (VM provisioned, Docker installed, SSH working).

### Step 1 — SSH into the VM

```bash
# Prod
ssh -i id_ed25519_rebus orbit@10.0.200.211

# Dev
ssh -i id_ed25519_rebus orbit@10.0.200.212
```

### Step 2 — Create deploy directory and clone

```bash
sudo mkdir -p /opt/orbit/server
sudo chown orbit:orbit /opt/orbit/server

# Authenticate with GitHub (uses NUGET_TOKEN PAT)
git clone https://REBUS-ORBIT:<NUGET_TOKEN>@github.com/REBUS-ORBIT/orbit-server.git \
  /opt/orbit/server
```

### Step 3 — Create and populate `.env`

```bash
cd /opt/orbit/server
cp .env.example .env

# Generate secrets
SESSION_SECRET=$(openssl rand -hex 32)
PG_PASS=$(openssl rand -base64 24 | tr -dc 'a-zA-Z0-9' | head -c 32)
MINIO_PASS=$(openssl rand -base64 24 | tr -dc 'a-zA-Z0-9' | head -c 32)

sed -i "s/CHANGE_ME_generate_with_openssl_rand_hex_32/${SESSION_SECRET}/" .env
sed -i "s/POSTGRES_PASSWORD=CHANGE_ME/POSTGRES_PASSWORD=${PG_PASS}/" .env
sed -i "s/MINIO_PASSWORD=CHANGE_ME/MINIO_PASSWORD=${MINIO_PASS}/" .env

# For DEV VM, also change the SERVER_URL line:
# sed -i "s|orbit.rebus.industries|orbit-dev.rebus.industries|g" .env
```

### Step 4 — Pull images and start

```bash
cd /opt/orbit/server
docker compose pull
docker compose up -d
```

### Step 5 — Verify (see Health Checks below)

---

## Ongoing Deploys

### Via GitHub Actions (preferred)

Triggered automatically on a `v*.*.*` tag push, or manually:

1. Go to `github.com/REBUS-ORBIT/orbit-server` → Actions → **Deploy ORBIT Server**
2. Click **Run workflow**
3. Choose target: `prod`, `dev`, or `both`

The workflow SSHes to the VM, runs `git pull`, `docker compose pull`, `docker compose up -d`, then prints `docker compose ps`.

### Via SSH (manual / emergency)

```bash
ssh -i id_ed25519_rebus orbit@10.0.200.211
cd /opt/orbit/server
bash scripts/deploy.sh
```

### Pinning a specific image version

Add version variables to `.env` before pulling:

```bash
echo "ORBIT_SERVER_VERSION=v1.2.0" >> .env
echo "ORBIT_FRONTEND_VERSION=v1.2.0" >> .env
echo "ORBIT_PRISM_VERSION=v1.2.0" >> .env
docker compose pull
docker compose up -d
```

Remove the version pin to revert to `latest`.

---

## Health Checks

Run these after any deploy to confirm everything started correctly.

### 1. Container status

```bash
ssh orbit@10.0.200.211 "cd /opt/orbit/server && docker compose ps"
```

All containers should show `Up` or `Up (healthy)`. Any `Exit` or `Restarting` state means a problem.

### 2. Backend API

```bash
# Should return 200 and a JSON body with status: "ok"
curl -s https://orbit.rebus.industries/api/v1/serverinfo | python3 -m json.tool

# Or hit readiness directly (internal)
ssh orbit@10.0.200.211 "curl -s http://localhost:3000/readiness"
```

### 3. Frontend

```bash
# Should return 200 HTML
curl -s -o /dev/null -w "%{http_code}" https://orbit.rebus.industries
```

### 4. PRISM

```bash
# Should return {"status":"ok","version":"..."}
curl -s https://prism.rebus.industries/health
```

### 5. GraphQL

```bash
curl -s -X POST https://orbit.rebus.industries/graphql \
  -H "Content-Type: application/json" \
  -d '{"query":"{serverInfo{name version}}"}' | python3 -m json.tool
```

Expected response contains `serverInfo.name: "ORBIT"`.

---

## Viewing Logs

```bash
ssh orbit@10.0.200.211
cd /opt/orbit/server

# All services (follow)
docker compose logs -f

# Specific service
docker compose logs -f orbit-server
docker compose logs -f prism
docker compose logs -f postgres

# Last 200 lines, no follow
docker compose logs --tail=200 orbit-server
```

Key things to look for in `orbit-server` logs:
- `Speckle Server booted` — clean start
- `DATABASE` errors — Postgres connection failure; check `.env` credentials
- `REDIS` errors — Redis not ready; usually resolves after ~10 seconds on first boot
- `S3` errors — MinIO not ready or wrong credentials

---

## Rollback

### Roll back to the previous image tag

```bash
ssh orbit@10.0.200.211
cd /opt/orbit/server

# Find what was running before
docker compose images

# Pin to the previous known-good tag in .env
echo "ORBIT_SERVER_VERSION=v1.1.0" >> .env
docker compose pull orbit-server
docker compose up -d orbit-server
```

### Roll back the entire stack to a previous git commit

```bash
ssh orbit@10.0.200.211
cd /opt/orbit/server

git log --oneline -10              # find the last good commit
git checkout <commit-hash>
docker compose pull
docker compose up -d
```

To return to tracking main: `git checkout main && git pull`

### Emergency stop (take down without losing data)

```bash
docker compose down    # stops containers, keeps volumes
```

Data lives in Docker named volumes (`postgres_data`, `minio_data`, `redis_data`) and survives a `docker compose down`. To wipe everything: `docker compose down -v` — **this deletes all data**.

---

## Updating the External Caddy Proxy

Changes to the external proxy (LXC 251/252) are needed when adding new domains or changing routing. The config snippet is at `caddy/caddy-proxy-additions.conf` in this repo.

```bash
# SSH into each proxy LXC in turn
ssh root@10.0.1.51

# Edit the Caddyfile
nano /etc/caddy/Caddyfile

# Validate before reloading
caddy validate --config /etc/caddy/Caddyfile

# Reload (no downtime)
systemctl reload caddy
```

Repeat on LXC 252. Both must match — if they diverge, routing becomes inconsistent between requests.

---

## PRISM Operations

PRISM runs as the `prism` service inside the same Docker stack as ORBIT.

### Check conversion job status

```bash
# List recent jobs
curl -s https://prism.rebus.industries/jobs | python3 -m json.tool

# Check a specific job
curl -s https://prism.rebus.industries/jobs/<job-id> | python3 -m json.tool
```

### Submit a test conversion

```bash
curl -s -X POST https://prism.rebus.industries/convert/async \
  -F "file=@/tmp/test.obj" \
  -F "project_id=<orbit-project-id>" \
  -F "model_name=test-upload" | python3 -m json.tool
```

### Restart PRISM only (without restarting ORBIT)

```bash
ssh orbit@10.0.200.211
cd /opt/orbit/server
docker compose restart prism
docker compose logs -f prism
```

---

## Common Issues

### Stack won't start — `POSTGRES_PASSWORD must be set`

`.env` is missing or has `CHANGE_ME` values. See Environment Setup above.

### `orbit-server` restarts repeatedly

Check logs: `docker compose logs --tail=50 orbit-server`. Usually Postgres or Redis not ready on first boot. Wait 30 seconds and check again — `depends_on: condition: service_healthy` should handle this, but on a slow disk it can lose the race.

### Frontend loads but shows API error

`SERVER_URL` in `.env` is wrong or the backend isn't healthy. Check:
```bash
docker compose ps orbit-server
curl http://localhost:3000/readiness
```

### PRISM returns 502

The `prism` container may have crashed. Check: `docker compose logs prism`. Most common cause is missing `RHINOCOMPUTE_URL` or a Python import error on startup.

### `docker compose pull` fails for `orbit-*` images

The ORBIT-branded images (`ghcr.io/cheekiskrub/orbit-*`) have not been built yet — this is a known pending task. Until they exist, the stack cannot start. See ROADMAP.md Phase Server — rebuild and rename Docker images.

### SSH connection refused

VM not running, or key mismatch. Check Proxmox: `qm status 211`. If stopped: `qm start 211`. If running but unreachable, check the IP in netplan and the Proxmox firewall.

---

## Known Pending Work

These are blockers that must be resolved before ORBIT can serve production traffic:

1. **Provision VMs 211 and 212** — Proxmox clone from VM 201. Follow VM-SETUP.md.
2. **Rebuild Docker images** — `orbit-server`, `orbit-frontend`, `orbit-preview` must be rebuilt from the patched Speckle 2.31.1 source and pushed to `ghcr.io/rebus-orbit/*`. The compose file currently points to `ghcr.io/cheekiskrub/*` (temporary).
3. **Rotate SESSION_SECRET and MinIO password** — The values set during first deploy are the permanent ones. Store them somewhere secure (password manager or Proxmox secrets).
4. **Apply external Caddy config** — Add entries from `caddy/caddy-proxy-additions.conf` to LXC 251 and 252.
5. **PRISM deploy workflow** — PRISM has a build-and-push CI workflow but no SSH deploy step. Add a deploy job to `prism/.github/workflows/build.yml` mirroring the orbit-server deploy pattern.

---

## Repository Map

| Repo | GitHub URL | Deploy method |
|---|---|---|
| orbit-sdk | github.com/REBUS-ORBIT/orbit-sdk | NuGet publish on tag |
| orbit-connectors | github.com/REBUS-ORBIT/orbit-connectors | `.rhp` artifact on tag |
| orbit-server | github.com/REBUS-ORBIT/orbit-server | SSH deploy on tag / manual |
| prism | github.com/REBUS-ORBIT/prism | Docker push on tag (deploy workflow pending) |
| orbit-infra | github.com/REBUS-ORBIT/orbit-infra | Docs only — no deploy |
