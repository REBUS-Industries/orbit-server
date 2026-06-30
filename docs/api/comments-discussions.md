# Comments & discussions

ORBIT **Discussions** in the web UI are **comment threads** exposed via GraphQL. There is no REST surface for comments.

Two API generations coexist:

| | Modern (use this) | Legacy (deprecated) |
|---|---|---|
| Create | `commentMutations.create` | `commentCreate` |
| Reply | `commentMutations.reply` | `commentReply` |
| Edit | `commentMutations.edit` | `commentEdit` |
| Archive | `commentMutations.archive` | `commentArchive` |
| Mark viewed | `commentMutations.markViewed` | `commentView` |
| Resource binding | `resourceIdString` | `resources[]` + `ResourceType` enum |
| Viewer pin data | `viewerState` (JSON) | `data.location` + `data` object |
| Project ID field | `projectId` | `streamId` |

Schema verified against `https://orbit.rebus.industries/graphql` (introspection, 2026-06).

---

## Concepts

### Comment thread

A **thread** is a top-level `Comment`. Replies share the same thread via `parent` / `threadId`. The UI "Discussions" panel lists threads; opening one shows `replies`.

### Resource attachment

Every thread is attached to one or more **resources**:

- **Project**-level discussion
- **Model** or **version** discussion
- **Object pin** — comment anchored to a specific object in the viewer

Modern API uses a single **`resourceIdString`** — the same comma-delimited format as viewer URLs (e.g. model + version + object path). See [Federating & combining models](federating-models) for the full grammar.

Legacy API uses `resources: [{ resourceType, resourceId }]` where `resourceType` is one of: `stream`, `commit`, `object`, `comment`.

### Viewer state (pins)

Object pins and camera-linked comments need **`viewerState`** (modern) or **`data.location`** (legacy). Without spatial data, comments may appear in lists but **won't render correctly in the viewer** (and can cause viewer issues if `data.location` is entirely missing on legacy creates).

`viewerState` is a `JSONObject` (SerializedViewerState) capturing camera, selection, isolation, etc. The web UI generates this automatically. Headless integrators should copy structure from an existing comment or omit `viewerState` only for non-viewer (text-only) threads.

### Text content

Comment bodies use **ProseMirror JSON** (`CommentContentInput.doc`), not plain strings. Example minimal doc:

```json
{
  "type": "doc",
  "content": [
    {
      "type": "paragraph",
      "content": [{ "type": "text", "text": "Hello from the API" }]
    }
  ]
}
```

Legacy `commentCreate` accepts `text` as either a plain string or ProseMirror doc depending on version; prefer `content.doc` on modern mutations.

### Public comments

Projects expose `allowPublicComments`. When enabled, limited public access may comment without full team membership (depends on project visibility).

---

## Query threads

### Project-wide

```graphql
query($projectId: String!, $cursor: String) {
  project(id: $projectId) {
    allowPublicComments
    commentThreads(limit: 25, cursor: $cursor) {
      totalCount
      cursor
      items {
        id
        rawText
        archived
        createdAt
        updatedAt
        viewedAt
        author { id name avatar }
        viewerState
        viewerResources
        resources { resourceId resourceType }
        replies(limit: 5) {
          totalCount
          items { id rawText author { name } createdAt }
        }
      }
    }
  }
}
```

### Filter by viewer resource string

```graphql
query($projectId: String!, $resourceIdString: String!) {
  project(id: $projectId) {
    commentThreads(
      limit: 50
      filter: {
        resourceIdString: $resourceIdString
        loadedVersionsOnly: true
        includeArchived: false
      }
    ) {
      items { id rawText archived author { name } }
    }
  }
}
```

`ProjectCommentsFilter` fields:

| Field | Purpose |
|---|---|
| `resourceIdString` | Comma-delimited viewer resource string |
| `loadedVersionsOnly` | When true, only threads for versions referenced in the string |
| `includeArchived` | Include resolved/archived threads |

### Model or version scope

```graphql
query($projectId: String!, $modelId: String!) {
  project(id: $projectId) {
    model(id: $modelId) {
      commentThreads(limit: 25) {
        items { id rawText createdAt }
      }
    }
  }
}
```

```graphql
query($projectId: String!, $modelId: String!, $versionId: String!) {
  project(id: $projectId) {
    model(id: $modelId) {
      version(id: $versionId) {
        commentThreads(limit: 25) {
          items { id rawText createdAt }
        }
      }
    }
  }
}
```

### Single thread by ID

```graphql
query($projectId: String!, $commentId: String!) {
  project(id: $projectId) {
    comment(id: $commentId) {
      id
      rawText
      text
      archived
      viewerState
      author { id name }
      replies(limit: 100) {
        items { id rawText author { name } createdAt }
      }
    }
  }
}
```

---

## Mutations (modern)

All modern comment mutations live under `commentMutations`:

```graphql
mutation {
  commentMutations {
    create(input: { ... })
    reply(input: { ... })
    edit(input: { ... })
    archive(input: { ... })
    markViewed(input: { ... })
  }
}
```

### Create thread

```graphql
mutation($input: CreateCommentInput!) {
  commentMutations {
    create(input: $input) {
      id
      rawText
      createdAt
      viewerResources
    }
  }
}
```

```json
{
  "input": {
    "projectId": "PROJECT_ID",
    "resourceIdString": "MODEL_ID@VERSION_ID",
    "content": {
      "doc": {
        "type": "doc",
        "content": [{
          "type": "paragraph",
          "content": [{ "type": "text", "text": "Review this facade" }]
        }]
      },
      "blobIds": []
    },
    "viewerState": null,
    "screenshot": null
  }
}
```

**Object pin** — set `resourceIdString` to include the object path (same format the viewer uses when deep-linking to a selected object). Obtain a real string by creating a pin in the UI and copying from the network request or `viewerResources` on an existing comment.

### Reply

```graphql
mutation($input: CreateCommentReplyInput!) {
  commentMutations {
    reply(input: $input) {
      id
      rawText
      parent { id }
    }
  }
}
```

```json
{
  "input": {
    "projectId": "PROJECT_ID",
    "threadId": "PARENT_COMMENT_ID",
    "content": {
      "doc": {
        "type": "doc",
        "content": [{
          "type": "paragraph",
          "content": [{ "type": "text", "text": "Agreed — fix by Friday" }]
        }]
      }
    }
  }
}
```

### Edit

Author-only (plus admins per permissions).

```graphql
mutation($input: EditCommentInput!) {
  commentMutations {
    edit(input: $input) {
      id
      rawText
      updatedAt
    }
  }
}
```

### Archive (resolve)

```graphql
mutation($input: ArchiveCommentInput!) {
  commentMutations {
    archive(input: $input) {
      id
      archived
    }
  }
}
```

```json
{
  "input": {
    "projectId": "PROJECT_ID",
    "commentId": "COMMENT_ID",
    "archived": true
  }
}
```

### Mark viewed

Updates `viewedAt` for the authenticated user on a thread.

```graphql
mutation($input: MarkCommentViewedInput!) {
  commentMutations {
    markViewed(input: $input) {
      id
      viewedAt
    }
  }
}
```

---

## Subscriptions

Live comment updates use `projectCommentsUpdated` — see [Subscriptions & permissions](subscriptions-permissions).

---

## Comment type reference

Key fields on `Comment`:

| Field | Description |
|---|---|
| `id` | Thread or reply ID |
| `author` / `authorId` | Creator |
| `text` | ProseMirror JSON document |
| `rawText` | Plain-text preview |
| `archived` | Resolved/hidden when true |
| `resources` | Legacy resource links |
| `viewerResources` | Modern viewer resource identifiers |
| `viewerState` | SerializedViewerState for pins/camera |
| `replies(limit, cursor)` | Paginated replies |
| `parent` | Parent thread if this is a reply |
| `viewedAt` | Last viewed by current user (auth required) |
| `screenshot` | Optional base64 PNG thumbnail |
| `permissions` | Can-edit / can-archive flags for current user |

---

## curl example (create thread)

```bash
curl -s https://orbit.rebus.industries/graphql \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer YOUR_PAT" \
  -d '{
    "query": "mutation($input: CreateCommentInput!) { commentMutations { create(input: $input) { id rawText } } }",
    "variables": {
      "input": {
        "projectId": "PROJECT_ID",
        "resourceIdString": "MODEL_ID@VERSION_ID",
        "content": {
          "doc": {
            "type": "doc",
            "content": [{
              "type": "paragraph",
              "content": [{ "type": "text", "text": "API test comment" }]
            }]
          }
        }
      }
    }
  }'
```

---

## SDK

`orbit-sdk` has **no comment helpers**. Use `OrbitGraphQLClient` with custom query strings or call GraphQL from your language of choice.

---

## Operational notes

- Comments are stored in PostgreSQL; no ORBIT-specific server patches affect comment behaviour.
- `allowPublicComments` is a project-level setting on `Project`.
- Prefer modern `commentMutations` — legacy root mutations remain but emit deprecation warnings in schema.
