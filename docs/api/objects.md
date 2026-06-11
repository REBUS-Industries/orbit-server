# Objects (REST)

ORBIT stores serialized 3D objects in S3-compatible storage (MinIO). Metadata lives in PostgreSQL; bulk payloads use REST, not GraphQL.

## Upload (batch)

```
POST /objects/{projectId}
Authorization: Bearer YOUR_PAT
Content-Type: application/json
```

Body: JSON array of serialized objects (Speckle/ORBIT object format with `id`, `speckle_type`, `totalChildrenCount`, etc.).

```bash
curl -s -X POST "https://orbit.rebus.industries/objects/PROJECT_ID" \
  -H "Authorization: Bearer YOUR_PAT" \
  -H "Content-Type: application/json" \
  -d @objects-batch.json
```

Response includes inserted object IDs. Upload all detached children before creating a version that references the root hash.

Connectors and PRISM handle batching, closure computation, and deduplication internally. Manual uploads must preserve the `{ referencedId }` pattern for large child objects.

## Download (single object)

```
GET /objects/{projectId}/{objectId}
Authorization: Bearer YOUR_PAT
```

Returns the full JSON for one object. Query parameters (upstream Speckle behaviour):

| Param | Purpose |
|---|---|
| `childrenDepth` | How many levels of children to inline |
| `allSlugs` | Include all child slugs in response |

```bash
curl -s "https://orbit.rebus.industries/objects/PROJECT_ID/OBJECT_HASH" \
  -H "Authorization: Bearer YOUR_PAT"
```

## Download (multiple)

```
POST /objects/{projectId}
```

With a body listing object IDs to fetch (batch download). Same path as upload but with a read-oriented payload — see upstream Speckle REST docs for the exact batch-get shape your client uses.

## Permissions

Caller must have at least **read** access on the project (`projectId` / legacy `streamId`).

## Relationship to versions

1. Upload object tree via REST.
2. Create a **version** via GraphQL pointing at the root object's `id` (SHA-256 content hash).
3. Viewer and comments attach to versions, models, projects, or individual objects via `resourceIdString`.

## Blob attachments (comments)

Comment attachments use separate blob upload flows (`blobIds` in comment content). Attachments are referenced by ID in `CommentContentInput.blobIds`. The web UI uploads blobs before creating the comment; integrators must follow the same blob API (`/api/fileupload/` family — inspect network tab or schema for your server version).

## No GraphQL object upload

There is no GraphQL mutation to upload raw geometry. Use REST `/objects/*` or a connector/SDK transport layer.
