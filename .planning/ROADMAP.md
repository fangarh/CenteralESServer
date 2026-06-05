# Roadmap: CenteralESServer MVP

**Created:** 2026-05-31
**Source of truth:** `ESServer` Obsidian notes

## Milestone 1: Operational PDF Recognition MVP

Цель: получить работающий server-side путь от PDF upload до результата/status, с очередью, worker-ом, `pdf2txt` adapter-ом и минимальной операционной видимостью.

### Phase 1: Walking Skeleton PDF Processing

**Mode:** mvp
**Status:** Complete
**Plan:** `.planning/phases/001-walking-skeleton-pdf-processing/PLAN.md`

Сквозной slice:

```text
Public API -> PostgreSQL queue -> Worker -> pdf2txt adapter -> result retrieval -> минимальная админская видимость
```

Scope:

- scaffold .NET 9 solution без EF;
- discovery фактического `pdf2txt` contract;
- минимальная PostgreSQL queue;
- temporary file storage;
- Web endpoint `POST /api/pdf-stamp-recognition/jobs`;
- Worker loop;
- `pdf2txt` adapter с endpoint pool MVP;
- result retrieval по hash/job;
- минимальная админская read-only видимость jobs/attempts/processor state;
- tests для доменной логики, queue selection, retry classification и adapter contract boundaries.

### Phase 2: Security, Retry, Health, Admin Actions

**Status:** Complete

Scope:

- API key model, hashed secret storage, auth middleware, allowed capabilities, Admin create/list/disable backend and UI implemented;
- admin login/session/CSRF baseline implemented; first-admin WinForms bootstrap app implemented and covered by backend smoke;
- read-only Admin Services registry endpoint implemented for the registered MVP service; WinForms test client discovers and tests services through this registry plus existing Admin/Public API endpoints;
- manual retry одной failed/blocked задачи implemented in backend and Admin UI;
- audit для MVP admin actions implemented, including manual retry, API keys, admin users, bootstrap and filtered Audit UI with safe details;
- health live/ready, Worker heartbeat, job heartbeat, stale-processing recovery and Admin Health screen implemented without invoking external `pdf2txt`;
- temporary storage hard/min-free guard implemented; Admin Storage/Settings show read-only capacity and retention visibility;
- blocked/final failure handling implemented;
- support report MVP implemented in backend and exposed as Admin Job Details JSON download.

### Phase 3: Docker Compose Delivery MVP

**Status:** Released as `v0.1.0-mvp`

Scope:

- Dockerfiles для Web и Worker implemented;
- docker-compose с PostgreSQL и shared local storage implemented; baseline keeps demo-only `Fake` recognizer default and explicit `Http`/endpoint override for real `pdf2txt`;
- configuration examples implemented; `.env.example` documents local demo versus real processor settings;
- first-admin bootstrap/test client path implemented; shared backend service, WinForms app, MVP service testing, and backend smoke implemented before Docker checkpoint;
- migration/bootstrap process без EF implemented; explicit SQL runner and one-shot DatabaseMigrator run before Web/Worker;
- smoke tests для локальной поставки passed on 2026-06-03 with real `Http` recognizer: Public upload -> Worker -> external `pdf2txt` `/recognize_json/` -> completed result polling.
- production-like delivery workflow added after smoke: `compose.prod.yaml`, `.env.production.example`, `scripts/run-release-smoke.ps1`, and `docs/RELEASE_RUNBOOK.md`.
- release artifacts verified with Compose config/build and full .NET build/test; scripted release smoke passed with an Admin API-created Public API key;
- local annotated tag `v0.1.0-mvp` created after release gate.

### Phase 4: Admin MVP Completion

**Status:** Post-MVP backlog

Scope:

- Dashboard, Jobs/Job Details, single-job retry and support report download implemented for MVP;
- Processor Details passive health, queue counts, worker heartbeats, recent diagnostics, and DB-managed endpoint add/enable/disable/per-endpoint concurrency editing implemented; retry policy and global pool settings remain post-MVP;
- Results/Result Details read-only baseline, safe PDF summary and controlled raw JSON debug download implemented;
- API Keys UI implemented for create/list/disable; dedicated rotate endpoint remains post-MVP;
- Storage screen read-only baseline and retention policy visibility implemented; cleanup/dry-run/delete actions remain post-MVP;
- Health screen implemented;
- Audit screen baseline implemented with filters and safe details;
- Admin Users UI implemented for create/password/disable;
- Settings read-only baseline implemented; editing remains post-MVP.

## Deferred

- multi-node deployment;
- S3-compatible storage;
- external broker;
- сложные роли;
- multi-tenant;
- processor creation через UI;
- retention policy UI;
- retention cleanup actions;
- production backup/restore automation hardening;
- GitHub release notes publication;
- analytics;
- batch export больших payload.

## Roadmap Rules

- Obsidian decisions override GSD plan if conflict is found.
- Any conflict must be resolved in `ESServer` first, then reflected here.
- Phase plans should keep vertical slices working end-to-end.

---
*Last updated: 2026-06-05 after Admin-managed processor endpoints checkpoint*
