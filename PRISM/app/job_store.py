"""
In-memory job store. For production, replace with Redis-backed persistence
so jobs survive service restarts and are visible across multiple PRISM instances.
"""

import threading
from datetime import datetime, timezone
from enum import Enum
from typing import Optional


class JobStatus(str, Enum):
    QUEUED     = "queued"
    PROCESSING = "processing"
    COMPLETE   = "complete"
    FAILED     = "failed"


class JobStore:
    def __init__(self):
        self._jobs: dict = {}
        self._lock = threading.Lock()

    def create(self, job_id: str, params: dict):
        with self._lock:
            self._jobs[job_id] = {
                "job_id":     job_id,
                "status":     JobStatus.QUEUED,
                "params":     params,
                "created_at": datetime.now(timezone.utc).isoformat(),
                "updated_at": datetime.now(timezone.utc).isoformat(),
                "result":     None,
                "error":      None,
            }

    def get(self, job_id: str) -> Optional[dict]:
        with self._lock:
            return self._jobs.get(job_id)

    def update(self, job_id: str, status: JobStatus, result=None, error: str = None):
        with self._lock:
            if job_id in self._jobs:
                self._jobs[job_id]["status"]     = status
                self._jobs[job_id]["updated_at"] = datetime.now(timezone.utc).isoformat()
                if result is not None:
                    self._jobs[job_id]["result"] = result
                if error is not None:
                    self._jobs[job_id]["error"]  = error


# Singleton
job_store = JobStore()
