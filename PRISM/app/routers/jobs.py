"""
/jobs routes — poll conversion job status.
"""

from fastapi import APIRouter, HTTPException
from app.job_store import job_store

router = APIRouter()


@router.get("/{job_id}")
async def get_job(job_id: str):
    """
    Returns job status and result data.
    Status values: queued | processing | complete | failed
    """
    job = job_store.get(job_id)
    if not job:
        raise HTTPException(status_code=404, detail=f"Job '{job_id}' not found")
    return job
