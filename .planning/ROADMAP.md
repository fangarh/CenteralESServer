# Roadmap: CenteralESServer MVP

**Created:** 2026-05-31  
**Source of truth:** `ESServer` Obsidian notes

## Milestone 1: Operational PDF Recognition MVP

Цель: получить работающий server-side путь от PDF upload до результата/status, с очередью, worker-ом, `pdf2txt` adapter-ом и минимальной операционной видимостью.

### Phase 1: Walking Skeleton PDF Processing

**Mode:** mvp  
**Status:** Planned  
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

**Status:** Pending

Scope:

- API key model and auth middleware;
- admin login/session/CSRF baseline;
- manual retry одной задачи;
- audit для admin actions;
- health live/ready и Worker heartbeat;
- temporary storage hard/soft limit behavior;
- blocked/final failure handling;
- support report MVP.

### Phase 3: Docker Compose Delivery MVP

**Status:** Pending

Scope:

- Dockerfiles для Web и Worker;
- docker-compose с PostgreSQL и shared local storage;
- configuration examples;
- init command для первого admin;
- migration/bootstrap process без EF;
- smoke tests для локальной поставки.

### Phase 4: Admin MVP Completion

**Status:** Pending

Scope:

- Dashboard как рабочая консоль;
- Processors/Processor Details;
- API Keys UI;
- Storage screen;
- Health screen;
- Audit screen;
- Admin Users;
- Settings как узкий общесистемный экран.

## Deferred

- multi-node deployment;
- S3-compatible storage;
- external broker;
- сложные роли;
- multi-tenant;
- processor creation через UI;
- retention policy UI;
- analytics;
- batch export больших payload.

## Roadmap Rules

- Obsidian decisions override GSD plan if conflict is found.
- Any conflict must be resolved in `ESServer` first, then reflected here.
- Phase plans should keep vertical slices working end-to-end.

---
*Last updated: 2026-05-31 after GSD initialization*
