"""
DWG conversion worker.
Sends the .dwg file to RhinoCompute (or rh_watcher.ps1 on RB-DA2-PC01)
for conversion to mesh geometry, then uploads resulting ORBIT objects
to the ORBIT server.
"""

import os
import httpx
from app.orbit_client import OrbitUploader

RHINOCOMPUTE_URL = os.getenv("RHINOCOMPUTE_URL", "http://compute.rebus.industries")


async def process(job_id: str, params: dict) -> dict:
    """
    Convert a DWG file to ORBIT objects via RhinoCompute.

    TODO: Implement full conversion pipeline:
    1. POST file to RhinoCompute /grasshopper endpoint with a DWG-import definition
    2. Parse returned geometry (meshes)
    3. Convert meshes to ORBIT Mesh objects (JSON)
    4. Upload to ORBIT server via OrbitUploader
    5. Create version on the target model
    """
    file_path = params["file_path"]

    # Stub: log and return placeholder
    # Replace this with actual RhinoCompute call
    print(f"[DWG Worker] Processing: {params['file_name']} (job {job_id})")
    print(f"  Target: {params['orbit_server_url']} / project={params['project_id']}")
    print(f"  NOTE: Full DWG conversion via RhinoCompute — implementation pending")

    return {
        "message": "DWG conversion stub — RhinoCompute integration pending",
        "file": params["file_name"],
    }
