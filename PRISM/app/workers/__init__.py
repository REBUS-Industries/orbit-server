"""
Worker dispatcher — routes jobs to the correct format worker based on file extension.
"""

import os
from app.job_store import job_store, JobStatus
from app.workers import dwg_worker, obj_worker


FORMAT_WORKERS = {
    ".dwg": dwg_worker.process,
    ".obj": obj_worker.process,
    ".stl": obj_worker.process,   # OBJ worker can handle STL via trimesh
    # ".fbx": fbx_worker.process,  # TODO: implement FBX worker
    # ".ifc": ifc_worker.process,  # TODO: implement IFC worker
}


async def dispatch_worker(job_id: str):
    """Entry point called by FastAPI BackgroundTasks."""
    job = job_store.get(job_id)
    if not job:
        return

    params = job["params"]
    ext    = params["format"]
    worker = FORMAT_WORKERS.get(ext)

    if not worker:
        job_store.update(job_id, JobStatus.FAILED, error=f"No worker for format '{ext}'")
        return

    job_store.update(job_id, JobStatus.PROCESSING)
    try:
        result = await worker(job_id, params)
        job_store.update(job_id, JobStatus.COMPLETE, result=result)
    except Exception as e:
        job_store.update(job_id, JobStatus.FAILED, error=str(e))
    finally:
        # Clean up temp file
        try:
            os.unlink(params["file_path"])
        except OSError:
            pass
