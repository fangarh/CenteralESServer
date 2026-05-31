# GSD State

## Project Reference

See: `.planning/PROJECT.md` (updated 2026-05-31)

**Core value:** Клиент ЭП должен надёжно отправить PDF, не зависнуть на долгой обработке и получить результат или понятный статус по hash/job.  
**Current focus:** Phase 1 — Walking Skeleton PDF Processing

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
Phase 1: Walking Skeleton PDF Processing
```

Plan:

```text
.planning/phases/001-walking-skeleton-pdf-processing/PLAN.md
```

## Current Open External Facts

- точный successful JSON-контракт `https://pdf2txt.selectel.dt1520.ru/recognize_json/`;
- фактический HTTP status/body для нулевого файла как возможного `InvalidInputProbe`.

These facts are discovery tasks inside Phase 1, not blockers for GSD planning.

## Implementation Checkpoints

- 2026-05-31: .NET 9 SDK installed locally, solution skeleton created, domain processing primitives added, Web API contract skeleton added, unit/integration tests passing.
- 2026-05-31: PostgreSQL queue contracts and initial schema SQL added, including `FOR UPDATE SKIP LOCKED` claim query and separate attempt diagnostics/result index tables.

## Workflow Rules

- Keep GSD interactive by default.
- Do not use autonomous/yolo execution unless explicitly requested.
- Commit planning artifacts when they form a coherent checkpoint.
- If implementation reveals architecture mismatch, update Obsidian first.

---
*Last updated: 2026-05-31 after Phase 1 implementation start*
