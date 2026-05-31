# Phase 1 Plan: Walking Skeleton PDF Processing

**Status:** Planned  
**Mode:** MVP vertical slice  
**Created:** 2026-05-31  
**Source:** Obsidian notes in `ESServer`

## Objective

Create the first executable end-to-end path for PDF stamp recognition:

```text
Public API -> PostgreSQL queue -> Worker -> pdf2txt adapter -> result retrieval -> минимальная админская видимость
```

This phase should prove the core architecture before expanding admin/security/delivery surfaces.

## Acceptance Criteria

- `POST /api/pdf-stamp-recognition/jobs` accepts a PDF and returns `200` for cached result or `202` for queued/active processing.
- `GET /api/pdf-stamp-recognition/results/{hash}` returns `200`, `202`, or `404` per agreed contract.
- `GET /api/jobs/{jobId}` returns attempt status without raw external errors.
- PostgreSQL queue supports safe concurrent worker claiming.
- Worker can process a queued PDF job through the processor adapter boundary.
- Endpoint selection supports endpoint pool MVP with `least in-flight`.
- Attempt history stores endpoint, duration, normalized error, retryable flag, and correlationId.
- Result index and PDF result payload are stored separately.
- Temporary input file is cleaned after terminal state.
- Minimal admin read-only surface shows jobs, attempts, processor status, and diagnostics summary.
- Tests cover domain state transitions, deduplication, retry classification, queue claiming, endpoint selection, and API response mapping.

## Work Plan

### 1. Solution Skeleton

- Create .NET 9 solution structure:
  - `src/Apps/CenteralES.Web`
  - `src/Apps/CenteralES.Worker`
  - `src/Modules/Processing`
  - `src/Modules/PdfStampRecognition`
  - `src/Modules/Storage`
  - `src/Modules/Admin`
  - `src/Shared/CenteralES.Domain`
  - `src/Shared/CenteralES.Application`
  - `src/Shared/CenteralES.Infrastructure`
  - `tests/Unit`
  - `tests/Integration`
- Add test project setup before implementation work.
- Add architecture guard tests where practical.

### 2. pdf2txt Contract Discovery

- Try to reach `https://pdf2txt.selectel.dt1520.ru/recognize_json/`.
- If available, send a known valid small PDF sample and capture successful JSON shape.
- Send a zero-byte file and capture HTTP status/body.
- Decide whether zero-byte request can be used as `InvalidInputProbe`.
- Update `ESServer` and `.planning` if observed behavior changes assumptions.
- If unavailable, record discovery as blocked external fact and implement adapter against a fakeable contract boundary.

### 3. Domain and Storage Model

- Implement `ProcessingSubject`, `ProcessingJob`, attempt history, normalized errors, and result index concepts.
- Implement content hash calculation service.
- Implement temporary file storage abstraction and local implementation.
- Implement PDF result payload store.
- Write unit tests for state transitions and terminal cleanup decisions.

### 4. PostgreSQL Queue

- Create schema scripts/migrations without Entity Framework.
- Implement enqueue, deduplicate, claim, heartbeat/update, complete, fail, retry-schedule operations.
- Use safe concurrent claim semantics equivalent to `FOR UPDATE SKIP LOCKED`.
- Write integration tests against PostgreSQL where feasible.

### 5. Public API

- Implement:
  - `POST /api/pdf-stamp-recognition/jobs`
  - `GET /api/pdf-stamp-recognition/results/{hash}`
  - `GET /api/jobs/{jobId}`
- Implement agreed status and error mapping.
- Enforce upload size defaults in configuration.
- Do not expose raw processor errors through Public API.

### 6. Worker and Processor Adapter

- Implement Worker loop with cancellation and heartbeat.
- Implement processor registry baseline for `pdf2txt-http-recognizer`.
- Implement endpoint pool settings:
  - `endpointPool`
  - `poolConcurrencyLimit`
  - `endpointConcurrencyLimit`
  - timeout settings
- Implement endpoint selector: least in-flight among enabled and healthy/unknown endpoints.
- Implement adapter with timeout classification and normalized errors.
- Store diagnostics separately from result payload.

### 7. Minimal Admin Visibility

- Add read-only Admin API endpoints or internal views sufficient to inspect:
  - queue counts;
  - job details;
  - attempts;
  - selected endpoint;
  - normalized errors;
  - processor health summary.
- UI polish is deferred, but data contracts should support the future Admin MVP.

### 8. Verification

- Run unit tests.
- Run integration tests that do not require the external `pdf2txt` service.
- Run a local smoke test with fake adapter.
- If `pdf2txt` is available, run one external adapter smoke test and document the observed contract.
- Verify no raw secrets/input files/large payloads are stored in diagnostics/audit paths.

## Risks

- External `pdf2txt` may be unavailable. Mitigation: adapter boundary plus fake contract for internal tests.
- No Entity Framework means schema/migration process must be explicit early.
- Endpoint pool state must not rely on in-memory counters only if multiple Worker processes are active.
- Temporary file cleanup must not delete files while another attempt still needs them.

## Out of Scope For Phase 1

- Full Admin UI.
- API key management UI.
- Admin login/session implementation.
- Docker Compose final delivery.
- Mass retry.
- Retention policy.
- S3-compatible storage.
- External broker.

## Done Definition

- All acceptance criteria pass.
- Tests for the implemented slice pass.
- Architecture decisions that changed during implementation are reflected in `ESServer`.
- `.planning/STATE.md` points to the next phase.
- Work is committed.
