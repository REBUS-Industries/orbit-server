# Legacy API

The ORBIT server retains Speckle v2 field names and root-level mutations for backward compatibility. **Do not use these in new code.**

## Deprecated mutations

| Legacy (deprecated) | Replacement |
|---|---|
| `commentCreate(input: CommentCreateInput!)` | `commentMutations { create(input: CreateCommentInput!) }` |
| `commentReply(...)` | `commentMutations { reply(...) }` |
| `commentEdit(input: CommentEditInput!)` | `commentMutations { edit(input: EditCommentInput!) }` |
| `commentArchive(streamId, commentId, archived)` | `commentMutations { archive(...) }` |
| `commentView(streamId, commentId)` | `commentMutations { markViewed(...) }` |
| `userCommentThreadActivityBroadcast` | `broadcastViewerUserActivity` |

Deprecation reason in schema: *"Use commentMutations version"*.

## Legacy comment create shape

```graphql
mutation($input: CommentCreateInput!) {
  commentCreate(input: $input)
}
```

`CommentCreateInput` fields:

| Field | Notes |
|---|---|
| `streamId` | Use `projectId` in modern API |
| `resources` | Array of `{ resourceType, resourceId }` |
| `text` | ProseMirror doc (`JSONObject`) |
| `data` | **Required** JSON bag; must include `location: { x, y, z }` for viewer pins |
| `blobIds` | Attachment IDs |
| `screenshot` | Optional base64 PNG |

`ResourceType` enum: `stream`, `commit`, `object`, `comment`.

Example (legacy object pin on a version):

```graphql
mutation {
  commentCreate(input: {
    streamId: "PROJECT_ID"
    resources: [{ resourceType: commit, resourceId: "VERSION_ID" }]
    text: {
      type: "doc"
      content: [{ type: "paragraph", content: [{ type: "text", text: "Legacy pin" }] }]
    }
    data: { location: { x: 0.0, y: 0.0, z: 0.0 } }
    blobIds: []
  })
}
```

Missing `data.location` on legacy creates can break the web viewer for that thread.

## Legacy queries

Root-level `comments` queries and `stream`-scoped comment fields may still exist as deprecated aliases. Prefer:

- `project(id).commentThreads`
- `project(id).model(id).commentThreads`
- `project(id).model(id).version(id).commentThreads`

## Stream / branch / commit naming

GraphQL still accepts deprecated `stream`, `branch`, and `commit` query entry points on some server versions. Map mentally:

```
stream  → project
branch  → model
commit  → version
streamId → projectId
```

## Migration checklist

1. Replace `streamId` with `projectId` in all comment inputs.
2. Replace `resources[]` with `resourceIdString` (copy format from viewer URL or existing `viewerResources`).
3. Replace `data.location` with `viewerState` when pins need full viewer context.
4. Nest mutations under `commentMutations { ... }`.
5. Replace `CommentEditInput.streamId` / `id` with `EditCommentInput.projectId` / `commentId`.
6. Switch subscriptions to `projectCommentsUpdated` with `ViewerUpdateTrackingTarget`.

## Why legacy persists

ORBIT tracks upstream `ghcr.io/rebus-orbit/orbit-server` with minimal patches (`patches/orbit-server/`). Comment schema is unmodified upstream; deprecation is gradual for existing Speckle connectors and automations.
