# Roadmap: CenteralESServer MVP

**Created:** 2026-05-31  
**Source of truth:** `ESServer` Obsidian notes

## Milestone 1: Operational PDF Recognition MVP

Цель: получить работающий server-side путь от PDF upload до результата/status, с очередью, worker-ом, `pdf2txt` adapter-ом и минимальной операционной видимостью.

### Phase 1: Walking Skeleton PDF Processing

**Mode:** mvp  
**Status:** Backend checkpoint complete  
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

**Status:** In Progress

Scope:

- API key model and auth middleware; baseline implemented, admin creation/rotation UI remains later in Phase 2/4;
- admin login/session/CSRF baseline; backend baseline implemented, init command/UI/audit remain later in Phase 2/4;
- manual retry одной задачи; backend endpoint implemented, UI remains Phase 4;
- audit для admin actions; current MVP dangerous actions and filtered Audit UI implemented;
- health live/ready и Worker heartbeat; baseline already implemented in Phase 1, remaining work is hardening/admin visibility;
- temporary storage hard/soft limit behavior; backend hard-limit block implemented, Admin UI warning remains Phase 4;
- blocked/final failure handling;
- support report MVP; backend endpoint implemented, UI/export button remains Phase 4.

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
- Results/Result Details read-only baseline done; raw JSON summary remains later;
- API Keys UI;
- Storage screen read-only baseline done; cleanup/retention actions remain later;
- Health screen;
- Audit screen baseline done with filters and safe details;
- Admin Users;
- Settings read-only baseline done; editing remains later.

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
*Last updated: 2026-06-02 after Admin Audit UI checkpoint*
