# Sub-models, folders & combining models

ORBIT projects contain **models**. Each model holds an immutable chain of **versions**
(object graphs uploaded by connectors or API clients). This page explains how to organise
models into a hierarchy (sub-models / folders) and how to **combine** geometry from
several models — in the viewer, in a connector, or as a new persisted model.

For the low-level `resourceIdString` grammar used by URLs and comments, see
[Federating & combining models](federating-models). For GraphQL CRUD, see
[Projects, models & versions](projects-models-versions).

---

## Sub-models and folders

ORBIT encodes a **parent/child hierarchy in the model name** using `/` separators — the
same convention Speckle uses for nested branches.

```text
Site
Site/Building
Site/Building/Level 1
Site/Building/Level 1/MEP
Site/Building/Level 1/Structure
```

The web UI, connectors, and federation resolver all treat:

- `Site/Building/Level 1` as a **child** of `Site/Building`
- `Site/Building` as a **child** of `Site`
- `Site` as a **top-level** model in the project

There is no separate “folder” entity on the server. Folders are **derived** from slash
names.

### What you see in the web UI

On a project's **Models** page, models appear in a tree:

| Row type | Meaning |
|---|---|
| **Leaf model** (document icon) | Has its own versions — click to open that model in the viewer |
| **Folder / group** (folder icon) | Has **sub-models** under the same name prefix. May or may not have its own versions |
| **View all** | Opens a **federated** viewer session with every descendant sub-model loaded together (each at its latest version) |

Clicking a parent row that has children also opens the federated view of its subtree
(same as **View all**).

### Create a top-level model

**Web UI**

1. Open the project → **Models**.
2. Choose **New model** (or the equivalent create action on the models page).
3. Enter a name **without** leading or trailing slashes, e.g. `Site` or `Main`.

**Rhino / Vectorworks connector**

1. Open a send or receive card and pick the project.
2. In the model tree, click **+** at the root (tooltip: *New top-level model*).
3. Enter the name and confirm **Create**.

**GraphQL**

```graphql
mutation($input: CreateModelInput!) {
  modelMutations {
    create(input: $input) { id name }
  }
}
```

```json
{ "input": { "projectId": "PROJECT_ID", "name": "Site" } }
```

### Create a sub-model

A sub-model is a model whose name is **`{parent}/{child}`** — only the **last** segment
is the new name you type; the parent path is fixed.

**Web UI**

1. On the project's **Models** page, locate the parent row (or create the parent first).
2. Create a new model whose name includes the parent path, e.g. `Site/Building/Level 1`.
   Some UIs expose this as “create under” the parent; otherwise type the full
   slash-separated path in the name field.

**Rhino / Vectorworks connector**

1. In the card's model tree, expand the parent node.
2. Click **+** on that row (*Add submodel under …*).
3. Type only the **new segment** (e.g. `Level 1`); the connector composes
   `Site/Building/Level 1` for you.

**GraphQL**

```json
{ "input": { "projectId": "PROJECT_ID", "name": "Site/Building/Level 1" } }
```

You do **not** need to create the parent model first. As soon as
`Site/Building/Level 1` exists, the UI shows `Site` and `Site/Building` as implicit
folder nodes. You **may** also create an empty parent explicitly (same `CreateModel`
mutation with name `Site/Building`) if you want a container before any child sends.

### Model naming rules

These rules apply in the web UI, connectors, and API:

| Rule | Example |
|---|---|
| Use `/` only as a hierarchy separator | `A/B/C` ✓ |
| No leading or trailing `/` | `/A` ✗, `A/` ✗ |
| No empty segments (`//`) | `A//B` ✗ |
| No commas | `A,B` ✗ |
| No backslashes | `A\B` ✗ |
| Do not start with `#` or `$` | `#layer` ✗ — `$` is reserved for federation tokens in URLs |

### Send and receive with sub-models

**Sending** — point the connector at the **leaf** model you want to update, e.g.
`Site/Building/Level 1/MEP`. Each sub-model has its own version history.

**Receiving (Rhino / UE5)** — when you pick a model that **has sub-models**, the
connector automatically imports the **whole subtree** (parent prefix match: the model
itself plus every name starting with `parent/`). Each sub-model is placed under a
layer/path prefix so trees stay separated (Rhino uses `::` in layer names).

**Viewing in the browser** — open one leaf model, or use **View all** / the parent row
to federate the entire subtree in one viewer (see below).

---

## Combining models (overview)

“Combining” can mean two different things in ORBIT:

| Goal | Mechanism | Creates new stored model? |
|---|---|---|
| View several models together | **Federation** (`resourceIdString`) | No — load-time only |
| Store merged geometry in one model | **New send / new version** | Yes — new object graph + version |

There is **no** web button that merges model A and model B into a new model on the
server without uploading new geometry. Federation is not a merge.

---

## Combine in the viewer (federation)

Use federation when you want to **look at** multiple models (or an entire folder) in
one scene without changing stored data.

### Open a folder's sub-models

On the Models page, click **View all** on a folder row, or click the parent row itself.
ORBIT loads every model under that prefix, each at its **latest** version.

This uses a `$folderName` resource token internally, e.g. `$Site/Building` (with `/`
URL-encoded as `%2F` in paths).

### Open specific models together

Build a viewer URL with a comma-separated list of model ids:

```text
https://orbit.rebus.industries/projects/{projectId}/models/{modelA},{modelB}
```

Pin specific versions:

```text
…/models/{modelA}@{versionId},{modelB}@{versionId}
```

Mix models and folders:

```text
…/models/{modelA},$Site%2FBuilding,{modelC}@abc123…
```

See [Federating & combining models](federating-models) for the full token grammar and
GraphQL resolver (`viewerResourcesExtended`).

**Coordinate space note:** federated models appear in the **same world coordinates**
they were authored in. If two models were both modelled around the origin, they will
overlap — same as importing them into one Rhino file without moving them.

---

## Merge into a new persisted model

To produce **one model** whose versions contain combined geometry (not just a
federated view), you must **upload a new object graph** and create a **new version** on
that model (or a newly created model).

### Option A — Combine in the host app, then send (recommended)

Typical workflow for Rhino / UE5 users:

1. **Create** a target model (top-level or sub-model) — e.g. `Site/Combined`.
2. In the host application, **import or reference** the geometry you want merged
   (receive from ORBIT into the same document, or copy in-place).
3. Position layers / levels as needed so the combined scene is correct.
4. **Send** to the target model. The connector uploads one root object graph; ORBIT
   stores it as a new version on `Site/Combined`.

The Rhino connector can also **merge fixture and non-fixture roots in one send** when
both are present in the same export (one version carries PRISM fixtures plus truss,
scenery, etc.).

### Option B — Receive subtree, edit, re-send to a new model

1. Receive a parent model (connector pulls all sub-models into one scene).
2. Save / consolidate geometry in the host app.
3. Send to a **new** model name (e.g. `Site/Published`).

This is useful when you want a snapshot that no longer tracks individual sub-model
versions separately.

### Option C — Build the object graph via API

For automated pipelines:

1. Download root objects for each source model (REST objects API).
2. Construct a new root `Collection` whose `@elements` reference or embed the
   combined content (respecting ORBIT object id / closure rules).
3. `POST /objects/{projectId}` with the new graph.
4. `versionMutations.create` pointing at the new root hash on your target model.

ORBIT does not ship a server-side “merge models” mutation — this is ordinary object
authoring. See [Objects (REST)](objects) and
[Projects, models & versions](projects-models-versions).

---

## Quick reference

| I want to… | Do this |
|---|---|
| Organise work under a site/building | Create models named `Site/Building/Discipline` |
| Add a child model under an existing parent | Create `parent/newChild` (connector **+** or full path in UI/API) |
| View all disciplines together | **View all** on the folder row, or open `$Parent/Path` federation URL |
| View two specific models together | Viewer URL `…/models/id1,id2` |
| Store merged geometry permanently | Send combined scene to a new model (Option A/B), or upload via API (Option C) |
| Import entire subtree in Rhino/UE | Select the **parent** model in receive — connector auto-detects descendants |

---

## Related

- [Federating & combining models](federating-models) — `resourceIdString` tokens, GraphQL resolver, comment binding
- [Projects, models & versions](projects-models-versions) — create/list models and versions
- [Objects (REST)](objects) — upload pipeline for merged roots
- Connectors — sub-model tree UI and parent receive: `orbit-connectors` repo,
  `docs/SUBMODEL_IMPORT_HANDOFF.md`
