"""
ORBIT server client for PRISM.
Handles object upload (batch POST) and version creation (GraphQL mutation).
Mirrors the behaviour of Orbit.Sdk.Transport.ServerTransport in C#.
"""

import hashlib
import json
import httpx


def compute_id(obj: dict) -> str:
    """Compute SHA-256 content hash for an ORBIT object (excluding 'id' field)."""
    obj_copy = {k: v for k, v in obj.items() if k != "id"}
    serialised = json.dumps(obj_copy, sort_keys=True, separators=(",", ":"))
    return hashlib.sha256(serialised.encode()).hexdigest()


class OrbitUploader:
    BATCH_SIZE = 100
    MAX_BYTES  = 1_000_000

    def __init__(self, server_url: str, auth_token: str, project_id: str):
        self.server_url = server_url.rstrip("/")
        self.project_id = project_id
        self.headers = {
            "Authorization": f"Bearer {auth_token}",
            "Content-Type":  "application/json",
        }

    async def upload(self, root: dict) -> str:
        """Serialise root object, compute ids, upload all objects. Returns root id."""
        objects = {}
        self._collect(root, objects)

        # Batch upload
        batch = []
        batch_bytes = 0
        async with httpx.AsyncClient(headers=self.headers, timeout=60) as client:
            for obj_id, obj_json in objects.items():
                if len(batch) >= self.BATCH_SIZE or batch_bytes + len(obj_json) > self.MAX_BYTES:
                    await self._flush(client, batch)
                    batch = []
                    batch_bytes = 0
                batch.append(obj_json)
                batch_bytes += len(obj_json)
            if batch:
                await self._flush(client, batch)

        return root.get("id", "")

    async def _flush(self, client: httpx.AsyncClient, batch: list[str]):
        payload = "[" + ",".join(batch) + "]"
        url = f"{self.server_url}/objects/{self.project_id}"
        r = await client.post(url, content=payload)
        r.raise_for_status()

    def _collect(self, obj: dict, store: dict):
        """Recursively compute ids and collect all objects."""
        obj_id = compute_id(obj)
        obj["id"] = obj_id
        store[obj_id] = json.dumps(obj, separators=(",", ":"))

        for v in obj.values():
            if isinstance(v, dict) and "speckle_type" in v:
                self._collect(v, store)
            elif isinstance(v, list):
                for item in v:
                    if isinstance(item, dict) and "speckle_type" in item:
                        self._collect(item, store)

    async def create_version(self, model_id: str, root_id: str, message: str) -> str:
        """Create a version on the target model. Returns version id."""
        mutation = """
        mutation($input: CreateVersionInput!) {
            modelMutations { create(input: $input) { id } }
        }"""
        variables = {"input": {
            "projectId": self.project_id,
            "modelId":   model_id,
            "objectId":  root_id,
            "message":   message,
            "sourceApplication": "PRISM",
        }}
        async with httpx.AsyncClient(headers=self.headers, timeout=30) as client:
            r = await client.post(
                f"{self.server_url}/graphql",
                json={"query": mutation, "variables": variables}
            )
            r.raise_for_status()
            data = r.json()
            return data["data"]["modelMutations"]["create"]["id"]
