"""
ORBIT PRISM — File Conversion Service
Converts external file formats (DWG, FBX, IFC, OBJ) to ORBIT objects
and pushes them directly to the ORBIT server.

Routes:
    POST /convert/async    — submit a file for conversion, returns job_id
    GET  /jobs/{job_id}    — poll job status and result
"""

from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.middleware.cors import CORSMiddleware
import uvicorn

from app.routers import convert, jobs

app = FastAPI(
    title="ORBIT PRISM",
    description="File conversion pipeline for ORBIT",
    version="1.0.0",
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(convert.router, prefix="/convert", tags=["convert"])
app.include_router(jobs.router,    prefix="/jobs",    tags=["jobs"])


@app.get("/health")
async def health():
    return {"status": "ok", "service": "prism"}


if __name__ == "__main__":
    uvicorn.run("app.main:app", host="0.0.0.0", port=8765, reload=True)
