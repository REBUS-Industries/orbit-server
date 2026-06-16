# ORBIT API overview

ORBIT is a self-hosted 3D data platform (Speckle Server–derived). Most integrations use **GraphQL** at a single endpoint. Bulk object upload/download uses **REST**.

## Base URLs

| Environment | URL |
|---|---|
| Production | `https://orbit.rebus.industries` |
| Development | `https://orbit-dev.rebus.industries` |

## Endpoints

| Path | Protocol | Purpose |
|---|---|---|
| `POST /graphql` | HTTP + WebSocket | Queries, mutations, subscriptions |
| `POST /objects/{projectId}` | REST | Upload serialized objects (batch) |
| `GET /objects/{projectId}/{objectId}` | REST | Download a single object (+ children via query params) |
| `GET /api/*` | REST | Auxiliary APIs (tokens, invites, etc.) |
| `GET /auth/*` | REST | OAuth2 / session auth flows |
| `GET /docs/` | HTTP | This documentation site |

There is **no REST API for comments**. Discussions and object pins are **GraphQL-only**.

## Terminology

ORBIT uses modern names in the GraphQL schema. Legacy Speckle names still appear in older docs and deprecated fields:

| Modern (preferred) | Legacy |
|---|---|
| Project | Stream |
| Model | Branch |
| Version | Commit |
| `projectId` | `streamId` |

## Quick start

```bash
curl -s https://orbit.rebus.industries/graphql \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_PAT" \
  -d '{"query":"{ activeUser { id name email } }"}'
```

```javascript
const res = await fetch('https://orbit.rebus.industries/graphql', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    Authorization: `Bearer ${token}`,
  },
  body: JSON.stringify({
    query: `{ project(id: "YOUR_PROJECT_ID") { id name } }`,
  }),
});
const { data, errors } = await res.json();
```

## Interactive schema exploration

Apollo Sandbox is available at `/graphql` (GET). Authenticate with a Personal Access Token in the **Headers** panel:

```json
{ "Authorization": "Bearer YOUR_PAT" }
```

There is no separate `/explorer` route on ORBIT production.

## SDK support

The [`orbit-sdk`](https://github.com/REBUS-Industries/orbit-sdk) package provides a generic GraphQL client and typed helpers for projects, models, and versions. **There are no SDK helpers for comments** — call GraphQL directly.

## Documentation sections

- [Authentication](authentication) — PAT, OAuth2, Bearer header
- [Projects, models & versions](projects-models-versions) — core data hierarchy
- [Objects (REST)](objects) — upload/download pipeline
- [Building a 3rd party viewer](building-a-3rd-party-viewer) — fetch, traverse, and render ORBIT geometry
- [Comments & discussions](comments-discussions) — threads, object pins, viewer state
- [GraphQL reference](graphql-reference) — operation catalogue with examples
- [Subscriptions & permissions](subscriptions-permissions) — live updates and ACL model
- [Legacy API](legacy-api) — deprecated mutations and field names
- [Limitations](limitations) — known gaps and constraints

## Related services

- **PRISM** (file conversion + visualiser): `https://prism.rebus.industries/docs` — separate REST/OpenAPI surface.
- **Connectors** (Rhino, UE5): OAuth2 PKCE via registered app IDs — see architecture `systems/connectors.md`.
