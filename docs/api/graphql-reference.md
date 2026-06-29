# GraphQL reference

GraphQL endpoint: `POST {BASE_URL}/graphql`

All examples use the production base URL.

## Server metadata

```graphql
query {
  serverInfo {
    name
    version
    canonicalUrl
    inviteOnly
    authStrategies { id name }
    scopes { name description }
  }
}
```

## Users

```graphql
query {
  activeUser {
    id
    name
    email
    avatar
    role
  }
}
```

```graphql
query($id: String!) {
  otherUser(id: $id) {
    id
    name
    avatar
  }
}
```

## Projects — mutations

| Operation | Path |
|---|---|
| Create | `projectMutations.create` |
| Update | `projectMutations.update` |
| Delete | `projectMutations.delete` |
| Invite user | `projectMutations.invites.create` |

```graphql
mutation($input: ProjectUpdateInput!) {
  projectMutations {
    update(input: $input) {
      id
      name
      allowPublicComments
    }
  }
}
```

## Models — mutations

| Operation | Path |
|---|---|
| Create | `modelMutations.create` |
| Update | `modelMutations.update` |
| Delete | `modelMutations.delete` |

## Versions — mutations

| Operation | Path |
|---|---|
| Create | `versionMutations.create` |
| Update metadata | `versionMutations.update` |

## Comments — mutations (modern)

| Operation | Input type | Returns |
|---|---|---|
| Create thread | `CreateCommentInput` | `Comment` |
| Reply | `CreateCommentReplyInput` | `Comment` |
| Edit | `EditCommentInput` | `Comment` |
| Archive | `ArchiveCommentInput` | `Comment` |
| Mark viewed | `MarkCommentViewedInput` | `Comment` |

See [Comments & discussions](comments-discussions) for full examples.

## Comments — queries

| Scope | Field |
|---|---|
| Project | `project(id).commentThreads(filter)` |
| Project | `project(id).comment(id)` |
| Model | `project(id).model(id).commentThreads` |
| Version | `project(id).model(id).version(id).commentThreads` |

## Object metadata (GraphQL)

Single object metadata (not the full payload):

```graphql
query($projectId: String!, $objectId: String!) {
  project(id: $projectId) {
    object(id: $objectId) {
      id
      speckleType
      createdAt
    }
  }
}
```

Full object JSON still requires REST `GET /objects/{projectId}/{objectId}`.

## Error shape

GraphQL returns HTTP 200 with errors in the body:

```json
{
  "data": null,
  "errors": [
    {
      "message": "You do not have access to the requested resource.",
      "locations": [{ "line": 2, "column": 3 }],
      "path": ["project"]
    }
  ]
}
```

Auth failures may return HTTP 401 on some routes; always check `errors` array.

## fetch helper

```javascript
async function orbitGraphql(token, query, variables = {}) {
  const res = await fetch('https://orbit.rebus.industries/graphql', {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    },
    body: JSON.stringify({ query, variables }),
  });
  const json = await res.json();
  if (json.errors?.length) throw new Error(json.errors[0].message);
  return json.data;
}
```

## WebSocket subscriptions

Connect to `wss://orbit.rebus.industries/graphql` with the `graphql-transport-ws` protocol (same as Speckle). Pass connection params:

```json
{ "Authorization": "Bearer YOUR_PAT" }
```

See [Subscriptions & permissions](subscriptions-permissions).

## Naming cheat sheet

| GraphQL (modern) | Legacy alias (deprecated) |
|---|---|
| `project` | `stream` |
| `projectId` | `streamId` |
| `version` | `commit` |
| `model` | `branch` |
| `versionMutations` | `commitMutations` (if present) |

Deprecated fields remain queryable but should not be used in new integrations.
