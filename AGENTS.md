# orbit-server

ORBIT server stack (API, viewer/frontend, preview, storage, auth) + deploy. This
folder is the canonical `REBUS-Industries/orbit-server` checkout.

**Architecture & ops:** see the `orbit-infra` repo — `systems/orbit.md`,
`OPERATIONS.md`, `VM-SETUP.md`. Session history: `decisions/CHANGELOG.md`.

## Key facts
- Prod = VM 211 (`orbit.rebus.industries`), running ORBIT + PRISM, on SRV01. (VM 212 dev/staging has been retired.)
- Deploy builds `orbit-server-patched` from `patches/orbit-server/*.js` on top of `ghcr.io/rebus-orbit/orbit-server`. The single root `docker-compose.yml` is authoritative.
- `Server/`, `Connectors/`, `SDK/` siblings are STALE duplicates being retired — edit only the canonical checkouts (`orbit-connectors-repo/`, `orbit-sdk-repo/`).

## Scope
WRITE here / REBUS-Industries / VM 211 only. CheekiSkrub + Speckle VMs (201/301) are READ-ONLY.
