# Authentication

All authenticated ORBIT API calls require a valid token in the `Authorization` header.

## Personal Access Token (PAT)

Recommended for scripts, server-side integrations, and API exploration.

1. Sign in to ORBIT (prod or dev).
2. Open **Profile → Access tokens** (or `/profile/tokens`).
3. Create a token with the scopes you need (typically `streams:read`, `streams:write`, `profile:read`, `profile:email`).
4. Pass it on every request:

```
Authorization: Bearer YOUR_PAT
```

PATs do not expire unless you revoke them. Store them in secrets management — never commit tokens to git.

## OAuth2 (connectors & desktop apps)

Rhino and UE5 connectors use **OAuth2 Authorization Code + PKCE** via `OrbitAuthManager`. Registered OAuth app IDs:

| Environment | App ID |
|---|---|
| Production | `c0c8e773a3` |
| Development | `c047ac8afa` |

Flow summary:

1. Connector opens `{SERVER_URL}/authn/verify/{appId}` in the system browser.
2. User signs in and approves access.
3. Connector exchanges the code for an access token at `/auth/token`.
4. Token is sent as `Authorization: Bearer …` on GraphQL and REST calls.

See `systems/connectors.md` in orbit-infra for connector-side details.

## Session cookies (web UI)

The ORBIT web frontend uses cookie-based sessions after browser login. Third-party integrations should use PAT or OAuth tokens, not scraped cookies.

## Unauthenticated access

Some queries work without auth (e.g. `serverInfo`, public project metadata when visibility allows). Comment mutations and most project data require authentication and project membership.

## Example: verify token

```graphql
query {
  activeUser {
    id
    name
    email
  }
}
```

```bash
curl -s https://orbit.rebus.industries/graphql \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_PAT" \
  -d '{"query":"{ activeUser { id name email } }"}'
```

## Scopes

Available scopes are listed on `serverInfo.scopes`. Comment operations require project read access; creating threads requires project write/create permission on the target project.

## PRISM / service tokens

PRISM and other internal services authenticate to ORBIT using bearer tokens configured in their environment (`ORBIT_TOKEN` / similar). Same header format applies.
