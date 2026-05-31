# Walking Skeleton

## Target Slice

The skeleton is successful when a developer can run Web + Worker locally against PostgreSQL and observe this flow:

1. Client sends a PDF to `POST /api/pdf-stamp-recognition/jobs`.
2. Web calculates content hash and creates or reuses a processing subject/job.
3. Worker takes the job from PostgreSQL queue.
4. Worker selects an endpoint from `pdf2txt-http-recognizer` endpoint pool.
5. Adapter calls the selected endpoint or a contract-test fake when external service is unavailable.
6. Result payload is stored in the PDF result store.
7. Client can call `GET /api/pdf-stamp-recognition/results/{hash}`.
8. Client can call `GET /api/jobs/{jobId}` for attempt status.
9. Admin read-only view can show the job, attempt, endpoint, normalized status, and diagnostics summary.

## Skeleton Boundaries

Included:

- real PostgreSQL schema;
- real queue claiming logic;
- real temporary file storage abstraction with local implementation;
- real result index/payload tables;
- fakeable processor adapter boundary;
- tests for critical behavior.

Allowed fake:

- `pdf2txt` adapter may use a local contract fake if the external service is unavailable during development.

Not included:

- full Admin UI polish;
- API key UI;
- Docker Compose production hardening;
- mass retry;
- retention policy;
- S3 storage.
