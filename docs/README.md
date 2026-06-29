# ORBIT API documentation

Static markdown served at `/docs` by the `orbit-docs` container in the ORBIT Docker stack.

## Layout

```
docs/api/           Markdown source (committed to orbit-server repo)
docs-server/        Lightweight Fastify + markdown-it renderer
```

## Local preview

```bash
cd docs-server
npm install
DOCS_DIR=../docs/api PUBLIC_BASE_URL=http://localhost:3080 node server.mjs
# open http://localhost:3080/docs/
```

## Production deploy

The docs ship as part of the ORBIT server stack on VM 211:

1. Commit changes to `docs/api/` and `docs-server/`.
2. On the VM (`/opt/orbit/server`), `git pull origin main`.
3. Rebuild and restart: `docker compose up -d --build orbit-docs`.
4. Internal Caddy routes `/docs*` to `orbit-docs:3080` (see root `Caddyfile`).

The external HA proxy forwards all non-backend paths to port 80 on the VM, so no proxy change is required for `/docs`.

## Updating content

Edit markdown under `docs/api/`. Add new pages to the `NAV` array in `docs-server/server.mjs`. Redeploy `orbit-docs` to pick up changes.
