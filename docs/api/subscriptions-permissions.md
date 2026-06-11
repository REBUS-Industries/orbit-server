# Subscriptions & permissions

## GraphQL subscriptions

WebSocket URL: `wss://{HOST}/graphql` (same host as HTTP).

Authentication: pass `Authorization: Bearer YOUR_PAT` in `connection_init` payload (graphql-transport-ws / graphql-ws protocol, matching upstream Speckle server).

### Comment updates

```graphql
subscription($target: ViewerUpdateTrackingTarget!) {
  projectCommentsUpdated(target: $target) {
    type
    id
    comment {
      id
      rawText
      archived
      author { id name }
    }
  }
}
```

`ViewerUpdateTrackingTarget` identifies the project and optionally narrows to specific viewer resources. Subscribe when building a custom viewer or dashboard that should refresh discussions without polling.

`ProjectCommentsUpdatedMessage`:

| Field | Description |
|---|---|
| `type` | Event kind (created, updated, deleted, etc.) |
| `id` | Affected comment/thread ID |
| `comment` | Full comment object; `null` if deleted |

### Other subscriptions

Upstream Speckle exposes additional subscriptions (project/model/version updates, viewer activity). Inspect the live schema at `/graphql` for the full list. ORBIT does not disable subscription fields present in the base server image.

---

## Permissions model

ORBIT inherits Speckle's role-based access control.

### Server roles

| Role | Scope |
|---|---|
| `server:user` | Default authenticated user |
| `server:admin` | Full server administration |

With `ADMIN_OVERRIDE_ENABLED=true`, ORBIT patches allow server admins to **list and open all projects** (see `systems/orbit.md`). Comment permissions still respect project roles unless explicitly overridden by upstream admin behaviour.

### Project roles

Set per project (legacy name: stream role):

| Role | Typical capabilities |
|---|---|
| `stream:owner` | Full control, delete project |
| `stream:contributor` | Create models/versions, comment |
| `stream:reviewer` | Read + comment (may vary by config) |

Query your role:

```graphql
query($id: String!) {
  project(id: $id) {
    role
    permissions {
      canRead { allowed reason }
      canUpdate { allowed reason }
    }
  }
}
```

Comment-specific checks appear on `Comment.permissions` for the authenticated user.

### Comment operations — permission summary

| Action | Requirement |
|---|---|
| Read threads | Project read access |
| Create / reply | Project write (contributor+) |
| Edit own comment | Author or elevated role |
| Archive | Author or project owner (see `permissions` on comment) |
| Mark viewed | Authenticated project member |

### Public comments

When `project.allowPublicComments` is `true`, limited commenting may be allowed for users with link/public access. Test with your project's visibility settings before relying on this in integrations.

### Token scopes (PAT)

Create tokens with minimum required scopes. Comment read/write generally needs stream/project read and write scopes (`streams:read`, `streams:write` in legacy scope names — verify against `serverInfo.scopes` on your instance).

### Unauthenticated requests

`serverInfo` and some public project fields may work without a token. All comment mutations require authentication.

---

## Rate limiting

ORBIT production does not expose a separate rate-limit header document. Treat GraphQL as an internal API: batch responsibly, use subscriptions instead of polling comment threads, and cache project metadata where possible.

---

## Multi-region / workspaces

Upstream schema includes workspace and multi-region fields. REBUS ORBIT production is a single-region deployment; workspace features may be partially configured. Check `serverInfo` and project `workspaceId` for your environment.
