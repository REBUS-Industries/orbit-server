"""
OBJ / STL conversion worker.
Uses trimesh to load the file, converts to ORBIT Mesh objects,
and uploads to the ORBIT server.
"""

import trimesh
import json
from app.orbit_client import OrbitUploader


async def process(job_id: str, params: dict) -> dict:
    """Convert OBJ or STL to ORBIT objects and upload."""
    file_path = params["file_path"]

    # Load geometry with trimesh
    scene = trimesh.load(file_path, force="scene")

    orbit_meshes = []
    for name, mesh in scene.geometry.items():
        if not isinstance(mesh, trimesh.Trimesh):
            continue

        vertices = mesh.vertices.flatten().tolist()
        # ORBIT face encoding: n, i0..i(n-1) per face (all triangles here)
        faces = []
        for face in mesh.faces:
            faces.extend([3, int(face[0]), int(face[1]), int(face[2])])

        normals = mesh.vertex_normals.flatten().tolist()

        orbit_mesh = {
            "speckle_type": "Orbit.Objects.Geometry.Mesh",
            "applicationId": name,
            "vertices": vertices,
            "faces":    faces,
            "vertexNormals": normals,
            "units": "m",
        }
        orbit_meshes.append(orbit_mesh)

    if not orbit_meshes:
        raise ValueError("No mesh geometry found in file")

    # Build root object
    root = {
        "speckle_type": "Orbit.Objects.Base.OrbitObject",
        "name": params["model_name"],
        "sourceApplication": "PRISM",
        "elements": orbit_meshes,
    }

    # Upload to ORBIT server
    uploader = OrbitUploader(
        server_url  = params["orbit_server_url"],
        auth_token  = params["orbit_token"],
        project_id  = params["project_id"],
    )
    root_id = await uploader.upload(root)
    version_id = await uploader.create_version(
        model_id    = params["model_id"],
        root_id     = root_id,
        message     = f"Imported via PRISM from {params['file_name']}",
    )

    return {
        "root_object_id": root_id,
        "version_id":     version_id,
        "mesh_count":     len(orbit_meshes),
    }
