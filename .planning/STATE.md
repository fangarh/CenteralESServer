# GSD State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-05-31)

**Core value:** Клиент ЭП должен надёжно отправить PDF, не зависнуть на долгой обработке и получить результат или понятный статус по hash/job.  
**Current focus:** Phase 2 — Security, Retry, Health, Admin Actions

## Source of Truth

Architecture source of truth:

```text
ESServer/
```

Primary notes:

- `ESServer/00 Обзор/Краткое резюме договорённостей.md`
- `ESServer/00 Обзор/Текущая точка обсуждения.md`
- `ESServer/00 Обзор/Карта архитектурных решений.md`
- `ESServer/01 Архитектура/Deployment - Web и Worker службы.md`
- `ESServer/02 Модули и обработчики/Processor Registry.md`
- `ESServer/03 API/Public API для сервиса ЭП.md`
- `ESServer/04 Админка/Админка MVP.md`
- `ESServer/05 Данные и хранение/Результаты и payload.md`
- `ESServer/06 Эксплуатация/Retry и дедупликация.md`

## Current Phase

```text
Phase 2: Security, Retry, Health, Admin Actions
```

Plan:

```text
Continue Phase 2 after temporary storage hard-limit with broader audit coverage, support report, and remaining admin actions.
```

## Current Open External Facts

- none for the current Phase 2 entry task.

The Phase 1 `pdf2txt` discovery facts have been captured in `ESServer/02 Модули и обработчики/PDF Stamp Recognition.md`.

## Implementation Checkpoints

- 2026-05-31: .NET 9 SDK installed locally, solution skeleton created, domain processing primitives added, Web API contract skeleton added, unit/integration tests passing.
- 2026-05-31: PostgreSQL queue contracts and initial schema SQL added, including `FOR UPDATE SKIP LOCKED` claim query and separate attempt diagnostics/result index tables.
- 2026-05-31: `db.env` is ignored by Git; PostgreSQL connection verified; target database bootstrapped; `PostgresProcessingJobQueue` implemented and covered by a real DB integration test.
- 2026-05-31: Web `POST /api/pdf-stamp-recognition/jobs` now uses `PostgresProcessingJobQueue`; app startup resolves local `db.env` or configured connection string and applies schema.
- 2026-05-31: Worker claim -> fake pdf recognizer -> PostgreSQL result store -> queue complete flow added; Web can return cached result by hash; real DB integration test covers save/read/complete.
- 2026-05-31: Public status read-model added: `GET /api/pdf-stamp-recognition/results/{hash}` returns `202` for active jobs and `GET /api/jobs/{jobId}` returns sanitized job status; integration DB tests run sequentially to avoid shared-database truncation races.
- 2026-05-31: Local temporary PDF storage abstraction added; Web stores uploaded input by content-hash key before enqueue using configurable `Storage:TemporaryRoot`.
- 2026-05-31: Worker now reads the queued temporary PDF stream through `ITemporaryFileStore`, passes it to the recognizer boundary, completes the job, and deletes the temporary file after successful completion.
- 2026-06-01: .NET SDK 9.0.314 installed per-user; first real `HttpPdfStampRecognizer` boundary added next to fake recognizer with endpoint pool options, multipart upload, raw successful JSON payload preservation, normalized adapter exceptions, and fake-HTTP unit coverage.
- 2026-06-01: `processor_overloaded` now defers the claimed job back to PostgreSQL queue instead of creating a failed attempt; integration tests tolerate missing local `db.env`/`test_db` by skipping DB-backed assertions.
- 2026-06-01: Minimal Admin read-only job visibility added through `GET /api/admin/jobs` and `GET /api/admin/jobs/{jobId}` backed by PostgreSQL read models; full test suite passes against local PostgreSQL.
- 2026-06-01: Minimal Admin processor passive status added through `GET /api/admin/processors/pdf2txt-http-recognizer`, including queue counts and recent diagnostics without calling the external processor.
- 2026-06-01: External `pdf2txt` zero-byte discovery completed: `/recognize_json/` is reachable and returns `HTTP 200` JSON with expected validation errors for an empty PDF, so `InvalidInputProbe` is viable as a separate diagnostic scenario.
- 2026-06-01: External `pdf2txt` successful smoke shape captured with a generated one-page PDF without stamp: `HTTP 200` JSON with empty `errors`, `workers`, `unrecognized_pages`, `workers_page`, and empty `izm_number`.
- 2026-06-01: Worker terminal adapter failures now use `ProcessorErrorClassifier`; non-retryable failures are marked final and clean temporary PDF input after the queue state is persisted.
- 2026-06-01: Web upload size guard added for PDF jobs through `PdfStampRecognition:MaxUploadBytes` with default `250 MiB`, hard cap `500 MiB`, and `413 payload_too_large` response.
- 2026-06-01: Admin Job Details now includes `attempts[]` for the processing subject, exposing attempt history for support/admin visibility.
- 2026-06-01: Local `test.pdf` added as ignored manual pdf2txt sample; external smoke returned `HTTP 200`, top-level result fields, `workers` count 3, and `workers_page` keys 2/3/15 without recording payload values in Git.
- 2026-06-01: `HttpPdfStampRecognizer` now treats `HTTP 200` JSON responses with non-empty `errors` as normalized `InvalidInput` instead of saving them as successful result payloads.
- 2026-06-01: Retryable processor failures now schedule a new queued attempt on the same processing subject and temporary file with fixed 30 second retry delay; old attempt keeps failed diagnostics.
- 2026-06-01: Worker now enforces skeleton `maxAttempts = 5`; retryable failures at the limit become terminal `blocked` and clean temporary input.
- 2026-06-01: Worker retry options are now configuration-backed through `PdfStampRecognition:Processor:maxAttempts` and `processorOverloadedDelay` while keeping MVP defaults 5 and 15 seconds.
- 2026-06-01: Web and Worker now use console logging explicitly for local/Docker-friendly runs, avoiding Windows EventLog write failures under non-admin accounts.
- 2026-06-01: Generic worker `InternalError` now follows retry classification: non-transient internal failures become terminal `blocked` and clean temporary input instead of creating an endless retry loop.
- 2026-06-01: Local web+worker smoke passed against PostgreSQL with fake pdf2txt: upload accepted, worker completed the job, and result polling returned `source=fake-pdf2txt`.
- 2026-06-01: PostgreSQL worker heartbeat added through `processing_worker_heartbeats`; Worker writes heartbeat every 30 seconds and Admin processor status now includes `workers[]` plus health derived from fresh/stale heartbeats.
- 2026-06-01: Local web+worker smoke passed again after heartbeat integration.
- 2026-06-01: Processing job heartbeat refresh added: Worker now updates `processing_jobs.heartbeat_at` while a claimed job is running, backed by `RefreshHeartbeatAsync` and PostgreSQL integration coverage.
- 2026-06-01: Local web+worker smoke passed again after job-level heartbeat integration.
- 2026-06-01: Web `/health/ready` now performs real passive readiness checks for PostgreSQL and temporary storage without calling external processors; integration tests set only test-process `SSL Mode=Disable` for the local Windows PostgreSQL handshake issue, leaving `db.env` untouched.
- 2026-06-01: Local web+worker smoke now checks `/health/ready` before upload and passed after readiness integration.
- 2026-06-01: Web `/health/ready` now also verifies minimal processing schema compatibility by checking required processing/result/worker heartbeat tables; local smoke passed with the schema check enabled.
- 2026-06-01: Phase 1 backend checkpoint recorded in requirements/roadmap; next implementation focus is Phase 2 API key auth for Public API.
- 2026-06-01: Public API key auth baseline added: `client_applications` table, PBKDF2 secret hashes, PostgreSQL authenticator, `Authorization: ApiKey <keyId>.<secret>` enforcement on Public API endpoints, 401/403 contract tests, and local smoke with seeded API key.
- 2026-06-01: Admin login/session/CSRF backend baseline added: `admin_users` and `admin_sessions` tables, PBKDF2 admin password hashes, hashed session/CSRF tokens, `POST /api/admin/auth/login`, `GET /api/admin/auth/me`, `POST /api/admin/auth/logout`, session protection for read-only Admin API, and CSRF enforcement for logout.
- 2026-06-01: Manual retry backend baseline added: `POST /api/admin/jobs/{jobId}/retry` requires admin session + CSRF, creates a new queued attempt for the current failed/blocked job, updates current subject state, and writes append-only `manual_retry_job` audit event in `admin_audit_events`.
- 2026-06-01: Temporary storage capacity baseline added: `LocalTemporaryStorageMonitor`, config keys `Storage:TemporarySoftLimitBytes`, `Storage:TemporaryHardLimitBytes`, `Storage:TemporaryMinimumFreeBytes`, readiness storage capacity check, and `503 temporary_storage_full` on Public PDF upload when the hard/min-free limit is exceeded.
- 2026-06-01: Admin Job Details support report MVP added through `GET /api/admin/jobs/{jobId}/support-report`, returning sanitized job/attempt diagnostics, passive processor/queue/worker context, result index reference, and related processing-job audit events without temporary input file keys or raw payloads.
- 2026-06-01: Web API composition refactor split `Program.cs` into endpoint groups, API contracts, authorization helpers, and mappings; `Program.cs` is now a small composition root. Processing job status DB/API mapping is centralized, and manual retry results use typed records instead of enum plus nullable payload.
- 2026-06-02: Admin audit read API added through `GET /api/admin/audit` with action/target/actor/date/limit filters; response exposes only safe audit metadata and does not return raw old/new JSON or technical metadata payloads.
- 2026-06-02: Admin API key management backend added: `GET /api/admin/api-keys`, `POST /api/admin/api-keys`, and `POST /api/admin/api-keys/{keyId}/disable`; create returns raw secret only once, create/disable require CSRF and write safe audit events without secret/hash.
- 2026-06-02: Admin user management backend added: `GET /api/admin/users`, `POST /api/admin/users`, `POST /api/admin/users/{userId}/disable`, and `POST /api/admin/users/{userId}/password`; state changes require CSRF, never return password/hash, and write safe audit events.

## Workflow Rules

- Keep GSD interactive by default.
- Do not use autonomous/yolo execution unless explicitly requested.
- Commit planning artifacts when they form a coherent checkpoint.
- If implementation reveals architecture mismatch, update Obsidian first.

---
*Last updated: 2026-06-02 after Admin user management checkpoint*
