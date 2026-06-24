# ORBIT VM Setup — VM 211 (Prod) & VM 212 (Dev)

Clone VM 201 twice to create clean ORBIT deployment targets.
VM 201 remains untouched as the legacy Speckle stack.

| VM ID | Role | IP | Hostname |
|---|---|---|---|
| 211 | ORBIT PROD + PRISM | 10.0.200.211 | orbit-prod |
| 212 | ORBIT DEV + PRISM | 10.0.200.212 | orbit-dev |

---

## Step 1 — Clone in Proxmox

Run on any Proxmox node (or via the web UI: Datacenter → VM 201 → Clone).

```bash
# SSH into a Proxmox node
ssh root@10.0.1.101

# Full clone VM 201 → VM 211 (PROD)
qm clone 201 211 \
  --name orbit-prod \
  --full true \
  --storage local-lvm

# Full clone VM 201 → VM 212 (DEV)
qm clone 201 212 \
  --name orbit-dev \
  --full true \
  --storage local-lvm
```

> **Full clone** copies all disk data independently. Do not use linked clones — they share the base disk with VM 201 and would be affected by changes to it.

---

## Step 2 — Set VM resources

Adjust RAM/CPU if needed before first boot. Recommended:

```bash
# Prod (VM 211) — match or exceed VM 201
qm set 211 --memory 8192 --cores 4

# Dev (VM 212) — lighter
qm set 212 --memory 4096 --cores 2
```

---

## Step 3 — Start and connect

```bash
qm start 211
qm start 212

# Wait ~30 seconds, then get a console
qm terminal 211
```

Or use the Proxmox web UI: VM 211 → Console.

---

## Step 4 — Change IP address

The clone still has VM 201's IP (10.0.200.11). Fix this first — the VM is not reachable on the network until you do.

### On VM 211 (via console):

```bash
# Find the network config file
ls /etc/netplan/

# Edit it (filename may vary)
nano /etc/netplan/00-installer-config.yaml
```

Change the IP:
```yaml
network:
  ethernets:
    ens18:          # interface name may differ — check with: ip link show
      addresses:
        - 10.0.200.211/24     # ← was 10.0.200.11
      routes:
        - to: default
          via: 10.0.200.1
      nameservers:
        addresses: [10.0.200.1, 8.8.8.8]
  version: 2
```

Apply:
```bash
netplan apply
```

Verify:
```bash
ip addr show
ping 10.0.1.101   # should reach Proxmox node
```

### On VM 212 (via console):

Same steps — set IP to `10.0.200.212/24`.

---

## Step 5 — Change hostname

```bash
# On VM 211:
hostnamectl set-hostname orbit-prod
echo "orbit-prod" > /etc/hostname
sed -i 's/10.0.200.11/10.0.200.211/g' /etc/hosts
sed -i 's/speckle-prod\|vm-201/orbit-prod/g' /etc/hosts

# On VM 212:
hostnamectl set-hostname orbit-dev
echo "orbit-dev" > /etc/hostname
sed -i 's/10.0.200.11/10.0.200.212/g' /etc/hosts
sed -i 's/speckle-prod\|vm-201/orbit-dev/g' /etc/hosts
```

---

## Step 6 — Add deploy SSH key

Add the REBUS deploy public key so GitHub Actions can SSH in:

```bash
mkdir -p ~/.ssh
cat >> ~/.ssh/authorized_keys << 'EOF'
ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGP9k0Dr/uSaALO1GcAnYLIMjt8YzHWalTcdAMLyjz+H dom@rebus-vms-2026-04-27
EOF
chmod 700 ~/.ssh
chmod 600 ~/.ssh/authorized_keys
```

Test from your machine:
```bash
ssh -i ~/.ssh/id_ed25519_rebus dom@10.0.200.211
ssh -i ~/.ssh/id_ed25519_rebus dom@10.0.200.212
```

---

## Step 7 — Stop and remove old Docker stack

The clone contains VM 201's running Speckle containers and data. Clear it out.

```bash
# Stop old containers
cd /opt/speckle   # or wherever VM 201's compose lives
docker compose down

# Remove all old volumes (this deletes all Speckle data on this clone — intended)
docker compose down -v

# Remove old images to free space
docker image prune -af
```

---

## Step 8 — Install Git (if not present)

```bash
apt-get update && apt-get install -y git
```

---

## Step 9 — Clone ORBIT-Server repo

```bash
mkdir -p /opt/orbit
cd /opt/orbit

git clone https://github.com/REBUS-ORBIT/ORBIT-Server.git server
cd server
```

---

## Step 10 — Configure .env

```bash
cp .env.example .env
nano .env
```

### VM 211 (.env for PROD):
```env
SERVER_NAME=ORBIT
SERVER_URL=https://orbit.rebus.industries
SESSION_SECRET=<generate: openssl rand -hex 32>
POSTGRES_DB=orbit
POSTGRES_USER=orbit
POSTGRES_PASSWORD=<strong password>
MINIO_USER=orbitadmin
MINIO_PASSWORD=<strong password>
FILE_SIZE_LIMIT_MB=1000
RHINOCOMPUTE_URL=http://compute.rebus.industries
ORBIT_SERVER_VERSION=latest
ORBIT_FRONTEND_VERSION=latest
ORBIT_PREVIEW_VERSION=latest
ORBIT_PRISM_VERSION=latest
```

### VM 212 (.env for DEV):
```env
SERVER_NAME=ORBIT DEV
SERVER_URL=https://orbit-dev.rebus.industries
SESSION_SECRET=<generate: openssl rand -hex 32>
POSTGRES_DB=orbit
POSTGRES_USER=orbit
POSTGRES_PASSWORD=<strong password — different from prod>
MINIO_USER=orbitadmin
MINIO_PASSWORD=<strong password — different from prod>
FILE_SIZE_LIMIT_MB=1000
RHINOCOMPUTE_URL=http://compute.rebus.industries
ORBIT_SERVER_VERSION=latest
ORBIT_FRONTEND_VERSION=latest
ORBIT_PREVIEW_VERSION=latest
ORBIT_PRISM_VERSION=latest
```

Generate a secure SESSION_SECRET on each VM:
```bash
openssl rand -hex 32
```

---

## Step 11 — Start the stack

```bash
cd /opt/orbit/server
docker compose pull
docker compose up -d
```

Check all containers are healthy:
```bash
docker compose ps
docker compose logs orbit-server --tail 50
```

---

## Step 12 — Auto-deploy

Deploy uses the **existing REBUS-Industries org self-hosted runner** (`self-hosted`, `Linux`,
`X64` — e.g. RB-DA2-Runner1). Same runner pattern as `orbit-connectors` prod workflows.

The workflow (`.github/workflows/deploy.yml`) SSHs to VM 211 via `PROD_VM_*` secrets and
runs `docker compose build/up` in `/opt/orbit/server`. No additional runner setup required.

Manual deploy on the VM (run as the user that owns `/opt/orbit/server`, usually `orbit`):

```bash
ssh orbit@10.0.200.211
cd /opt/orbit/server && ./scripts/deploy.sh
```

If you SSH in as another user (e.g. `dom`), use:

```bash
sudo -u orbit bash /opt/orbit/server/scripts/deploy.sh
```

The first frontend source build may take 20–40 minutes (`docker compose build orbit-frontend`).

```bash
chmod +x /opt/orbit/server/scripts/deploy.sh
```

---

## Step 13 — Update GitHub Secrets

In the ORBIT-Server repo → Settings → Secrets → Actions, update or add:

| Secret | Value |
|---|---|
| `PROD_VM_HOST` | `10.0.200.211` |
| `PROD_VM_USER` | `dom` |
| `PROD_VM_SSH_KEY` | Full PEM of `id_ed25519_rebus` (fingerprint `SHA256:tLLwaEfPgk23cBEdPAYVUNDvX6dxbqVlU1TeEe90Md8`, comment `dom@rebus-vms-2026-04-27`) |
| `DEV_VM_HOST` | `10.0.200.212` |
| `DEV_VM_USER` | `dom` |
| `DEV_VM_SSH_KEY` | Same private key as prod |

Paste the **entire** private key file (including `-----BEGIN/END OPENSSH PRIVATE KEY-----` lines)
into each secret. The matching public key must be in `dom`’s `~/.ssh/authorized_keys` on both VMs
(see Step 6).

For GHCR image pulls/builds on the VM, add `GHCR_TOKEN=<PAT with read:packages>` to `/opt/orbit/server/.env`
(or use `NUGET_TOKEN` — same PAT). `scripts/deploy.sh` runs `docker login ghcr.io` when either is set.

---

## Step 14 — Update external Caddy proxy

Add the new domain → IP routes to the HA Caddy pair (LXC 251 / 252).
See `caddy-proxy-additions.conf` in this folder for the exact blocks to add.

Reload Caddy on both proxies after updating:
```bash
systemctl reload caddy
```

---

## Verification checklist

- [ ] `ping 10.0.200.211` from your machine responds
- [ ] `ping 10.0.200.212` from your machine responds
- [ ] SSH works: `ssh -i ~/.ssh/id_ed25519_rebus dom@10.0.200.211`
- [ ] `docker compose ps` on VM 211 shows all containers healthy
- [ ] `docker compose ps` on VM 212 shows all containers healthy
- [ ] `curl http://10.0.200.211/api/v1/info` returns ORBIT server info
- [ ] https://orbit.rebus.industries loads in browser (after Caddy update)
- [ ] https://orbit-dev.rebus.industries loads in browser
- [ ] Push to `main` triggers deploy on org runner `[self-hosted, Linux, X64]` (or manual `./scripts/deploy.sh` on VM 211)
