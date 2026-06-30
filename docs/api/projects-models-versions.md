# Projects, models & versions

ORBIT organises 3D data in a three-level hierarchy. All CRUD for metadata is via GraphQL.

```
Project (was "stream")
 └── Model (was "branch")
      └── Version (was "commit") → references root Object ID
```

To load several models or versions together in one viewer scene, see [Federating & combining models](federating-models).

## List your projects

```graphql
query {
  activeUser {
    projects(limit: 50) {
      items {
        id
        name
        description
        updatedAt
        role
        allowPublicComments
      }
      cursor
    }
  }
}
```

## Get a project

```graphql
query($id: String!) {
  project(id: $id) {
    id
    name
    description
    visibility
    allowPublicComments
    role
    createdAt
    updatedAt
  }
}
```

## Create a project

Requires authenticated user with permission to create projects.

```graphql
mutation($input: ProjectCreateInput!) {
  projectMutations {
    create(input: $input) {
      id
      name
    }
  }
}
```

Variables:

```json
{
  "input": {
    "name": "My project",
    "description": "Optional description",
    "visibility": "PRIVATE"
  }
}
```

## Models

List models in a project:

```graphql
query($projectId: String!) {
  project(id: $projectId) {
    models(limit: 50) {
      items {
        id
        name
        updatedAt
      }
    }
  }
}
```

Create a model:

```graphql
mutation($input: CreateModelInput!) {
  modelMutations {
    create(input: $input) {
      id
      name
    }
  }
}
```

```json
{ "input": { "projectId": "PROJECT_ID", "name": "main" } }
```

## Versions

A **version** (commit) points at a root object hash uploaded via the REST objects API.

List versions:

```graphql
query($projectId: String!, $modelId: String!) {
  project(id: $projectId) {
    model(id: $modelId) {
      versions(limit: 20) {
        items {
          id
          message
          referencedObject
          sourceApplication
          createdAt
          author { id name }
        }
      }
    }
  }
}
```

Create a version (after uploading the root object):

```graphql
mutation($input: CreateVersionInput!) {
  versionMutations {
    create(input: $input) {
      id
      referencedObject
      createdAt
    }
  }
}
```

```json
{
  "input": {
    "projectId": "PROJECT_ID",
    "modelId": "MODEL_ID",
    "objectId": "ROOT_OBJECT_HASH",
    "message": "Sent from Rhino",
    "sourceApplication": "OrbitRhino",
    "totalChildrenCount": 42
  }
}
```

## Pagination

List fields return `CommentCollection`-style wrappers: `{ items, cursor, totalCount }`. Pass `cursor` from the previous page as the next request's cursor argument.

## Project settings relevant to comments

- `allowPublicComments` — when `true`, users with link access may comment without full project membership (subject to visibility rules).

## Server admin note

When `ADMIN_OVERRIDE_ENABLED=true` on the server, users with `server:admin` role see all projects in listings (ORBIT-specific patch). This does not change comment permissions.
