# Phase 1 Context: Walking Skeleton PDF Processing

## Goal

Build the first end-to-end PDF recognition path:

```text
Public API -> PostgreSQL queue -> Worker -> pdf2txt adapter -> result retrieval -> минимальная админская видимость
```

## Non-Negotiable Constraints

- .NET 9.
- PostgreSQL.
- No Entity Framework.
- Clean Architecture.
- DDD.
- TDD.
- Docker Compose is the delivery target, but Compose completion is Phase 3.

## Obsidian Decisions To Preserve

- Web and Worker are separate processes.
- Public API uses domain endpoint names.
- Queue is PostgreSQL-backed for MVP.
- Content hash is the MVP idempotency key.
- `pdf2txt` is a black box.
- Admin UI does not run business file processing for health checks.
- Endpoint pool represents several instances of one processor service.
- Diagnostics are stored separately from result payload.
- Raw secrets, input files, and large payloads are not stored in audit/diagnostics.

## External Discovery

At the start of this phase, verify current `pdf2txt` behavior if the service is available:

- endpoint: `https://pdf2txt.selectel.dt1520.ru/recognize_json/`;
- successful JSON for a valid PDF sample;
- response for zero-byte file;
- status/body for validation error;
- whether zero-byte file can become `InvalidInputProbe`.

If service is unavailable, implement adapter behind a contract boundary and mark discovery as blocked external fact.
