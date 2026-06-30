# Federating & combining models

ORBIT can show several models — or several **versions** of models — together in a
single viewer scene. This is called **federation**. There is **no server-side
"merge"**: combining models is a *load-time* concept driven by a single string,
the **`resourceIdString`**, that the viewer, deep links, and comments all share.

If you just want to do it in the web app, see the **Combine models in the web
viewer** section below. If you are building an integration, read on.

---

## The resource string (`resourceIdString`)

A `resourceIdString` is a **comma-separated list of resource tokens**. Each token
names one thing to load; listing more than one token federates them.

The same string is used in three places:

| Where | Example |
|---|---|
| Viewer URL | `https://orbit.rebus.industries/projects/{projectId}/models/{resourceIdString}` |
| Comment / pin binding | `resourceIdString` input on `commentMutations.create` (see [Comments & discussions](comments-discussions)) |
| GraphQL resolver | `project.viewerResourcesExtended(resourceIdString: …)` (see the **Resolve a resource string via GraphQL** section below) |

### Token grammar

| Token | Resolves to | Example |
|---|---|---|
| `modelId` | the model's **latest** version | `a1b2c3d4e5` |
| `modelId@versionId` | a **specific** version of a model | `a1b2c3d4e5@9f8e7d6c5b` |
| `$folderName` | **all** models under a folder/group | `$Architecture` |
| `all` | **every** model in the project | `all` |
| `objectId` | a raw object, not tied to a model (32-character id) | `b9c1…` (32 chars) |

Parsing rules (from the viewer's resource parser):

- Tokens are split on `,`; empty tokens are ignored.
- A token containing `@` is a `model@version`; a token starting with `$` is a
  folder; a 32-character token is an object id; `all` is the whole project;
  anything else is a model id (latest version).
- All ids are lower-cased, de-duplicated, and sorted, so
  `B,a` and `a,b,a` both canonicalise to `a,b`.
- For **nested** folders, the `/` separators are URL-encoded as `%2F` when the
  string appears in a URL path — e.g. folder `Site/Building/L1` becomes
  `$Site%2FBuilding%2FL1`.

### Combining = list multiple tokens

```text
modelA,modelB                 # two models, each at its latest version
modelA@ver1,modelB@ver2       # two specific versions, pinned
$MEP                          # every model in the "MEP" folder/group
all                           # every model in the project
modelA@ver1,modelB,$Structure # mix: a pinned version + a latest + a whole folder
```

---

## Combine models in the web viewer

No code required:

- **Folder "View all"** — in the model list, a folder/group row opens a federated
  view of all of its submodels (this builds a `$folderName` resource string for
  you).
- **Hand-crafted URL** — visit
  `https://orbit.rebus.industries/projects/{projectId}/models/{resourceIdString}`
  with a comma-separated list, e.g.
  `…/models/a1b2c3d4e5,f6g7h8i9j0` to federate two models. URL-encode `/` as
  `%2F` inside `$folder` names.

Once loaded, all federated models share one scene, one selection/visibility
state, and one set of [named views and camera controls](building-a-3rd-party-viewer).

---

## Resolve a resource string via GraphQL

Programmatic clients (custom viewers, prefetchers) can expand a `resourceIdString`
into the concrete `{ modelId, versionId, objectId }` triples it refers to with
`project.viewerResourcesExtended`. This is the same call the ORBIT frontend makes
before it starts fetching objects.

```graphql
query($projectId: String!, $resourceIdString: String!) {
  project(id: $projectId) {
    viewerResourcesExtended(resourceIdString: $resourceIdString) {
      resourceIdString
      groups {
        identifier            # the requested token (e.g. "modelA" or "$MEP")
        items {
          modelId
          versionId           # the concrete version chosen (latest, if unspecified)
          objectId            # the root object hash to download
        }
      }
    }
  }
}
```

```bash
curl -s https://orbit.rebus.industries/graphql \
  -H "Authorization: Bearer YOUR_PAT" \
  -H "Content-Type: application/json" \
  -d '{
    "query": "query($p:String!,$r:String!){ project(id:$p){ viewerResourcesExtended(resourceIdString:$r){ resourceIdString groups{ identifier items{ modelId versionId objectId } } } } }",
    "variables": { "p": "PROJECT_ID", "r": "modelA,modelB@ver2,$MEP" }
  }'
```

Each `objectId` is a root object hash — download and traverse it via the REST
objects API exactly as for a single model (see
[Objects (REST)](objects) and [Building a 3rd party viewer](building-a-3rd-party-viewer)).
A federated scene is simply the union of every group's objects.

---

## Binding a comment to a federated view

A comment thread is tied to whatever was loaded via the **same** `resourceIdString`
grammar. To pin a discussion to a combined view, pass the combined string:

```json
{
  "input": {
    "projectId": "PROJECT_ID",
    "resourceIdString": "modelA@ver1,modelB@ver2",
    "content": { "doc": { "type": "doc", "content": [] } }
  }
}
```

With `loadedVersionsOnly: true` on `commentThreads(filter: …)`, only threads whose
models/versions are part of the currently loaded string are returned. See
[Comments & discussions](comments-discussions).

---

## There is no server-side "merge"

Combining models does **not** create a new model or modify stored data:

- Models and versions are **immutable, content-addressed** object graphs (see
  [Objects (REST)](objects) and [Materials](materials)). Nothing is rewritten when
  you federate.
- Federation lives entirely in the **resource string** — it is a view/load
  instruction, not a mutation. There is no `combineModels`/`federate` GraphQL
  mutation or REST endpoint.

If you need a **persisted** single model that contains several others (rather than
an ad-hoc federated view), assemble it yourself:

1. Build a new root `Speckle.Core.Models.Collections.Collection` whose `@elements`
   reference the other models' root objects (or copy their elements).
2. Upload the new object graph via the REST objects API
   (`POST /objects/{projectId}`).
3. Create a **new version** pointing at the new root hash with
   `versionMutations.create` (see [Projects, models & versions](projects-models-versions)).

ORBIT provides no helper that does this automatically — it is ordinary
object-graph authoring.

---

## Quick reference

| Goal | Do this |
|---|---|
| Open two models together | URL `…/models/modelA,modelB` |
| Pin specific versions | `modelA@ver1,modelB@ver2` |
| Open a whole folder | `$FolderName` (encode `/` as `%2F`) |
| Open everything in a project | `all` |
| Resolve a string to objects | `project.viewerResourcesExtended(resourceIdString:)` |
| Persist a combined model | New root Collection → upload objects → new version |
