"""
/convert routes — accept file uploads and queue conversion jobs.
"""

import uuid
import tempfile
import os
from fastapi import APIRouter, UploadFile, File, Form, HTTPException, BackgroundTasks
from app.job_store import job_store, JobStatus
from app.workers import dispatch_worker

router = APIRouter()

SUPPORTED_FORMATS = {".dwg", ".fbx", ".obj", ".stl", ".ifc"}


@router.post("/async")
async def submit_conversion(
    background_tasks: BackgroundTasks,
    file:             UploadFile = File(...),
    orbit_server_url: str        = Form(...),
    orbit_token:      str        = Form(...),
    project_id:       str        = Form(...),
    model_id:         str        = Form(...),
    model_name:       str        = Form(None),
):
    """
    Accept a file upload, validate its format, save to temp storage,
    and enqueue a background conversion job.

    Returns a job_id for polling via GET /jobs/{job_id}.
    """
    ext = os.path.splitext(file.filename or "")[1].lower()
    if ext not in SUPPORTED_FORMATS:
        raise HTTPException(
            status_code=422,
            detail=f"Unsupported format '{ext}'. Supported: {', '.join(SUPPORTED_FORMATS)}"
        )

    # Save uploaded file to temp location
    tmp = tempfile.NamedTemporaryFile(suffix=ext, delete=False)
    content = await file.read()
    tmp.write(content)
    tmp.close()

    job_id = str(uuid.uuid4())
    job_store.create(job_id, {
        "file_path":       tmp.name,
        "file_name":       file.filename,
        "format":          ext,
        "orbit_server_url": orbit_server_url,
        "orbit_token":     orbit_token,
        "project_id":      project_id,
        "model_id":        model_id,
        "model_name":      model_name or file.filename,
    })

    background_tasks.add_task(dispatch_worker, job_id)

    return {"job_id": job_id, "status": "queued"}
