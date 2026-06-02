# Requirements: CenteralESServer MVP

**Defined:** 2026-05-31  
**Core Value:** Клиент ЭП должен надёжно отправить PDF, не зависнуть на долгой обработке и получить результат или понятный статус по hash/job.

## v1 Requirements

### Architecture

- [x] **ARCH-01**: Solution разделён на Web, Worker, модули домена/application/infrastructure и tests.
- [x] **ARCH-02**: Реализация не использует Entity Framework.
- [x] **ARCH-03**: Основные бизнес-операции покрываются TDD-тестами до или вместе с реализацией.
- [x] **ARCH-04**: PostgreSQL schema bootstrap использует явные SQL migration-файлы и runner без EF; production path имеет отдельный `CenteralES.DatabaseMigrator`, а Web/Worker auto-bootstrap включается только в Development или явным `Database:AutoBootstrap=true`.

### Public API

- [x] **API-01**: `POST /api/pdf-stamp-recognition/jobs` принимает PDF и возвращает `200` для готового результата или `202` для активной обработки.
- [x] **API-02**: `GET /api/pdf-stamp-recognition/results/{hash}` возвращает `200`, `202` или `404` по согласованному контракту.
- [x] **API-03**: `GET /api/jobs/{jobId}` возвращает статус конкретной попытки без raw errors.
- [x] **API-04**: Public API использует единый формат ошибок с `correlationId`.

### Security

- [x] **SEC-01**: Public API принимает `Authorization: ApiKey <keyId>.<secret>`.
- [x] **SEC-02**: Сервер хранит только hash API key secret.
- [x] **SEC-03**: API key может быть отключён и ограничен allowed capabilities.
- [x] **SEC-04**: Admin API требует session cookie, а state-changing endpoint-ы требуют CSRF token.

### Queue and Processing

- [x] **QUEUE-01**: Задачи хранятся в PostgreSQL-backed queue.
- [x] **QUEUE-02**: Worker выбирает задачи конкурентно безопасно.
- [x] **QUEUE-03**: Дедупликация использует `capability + content_hash`.
- [x] **QUEUE-04**: Retry policy поддерживает max attempts и безопасный final state `blocked`.
- [x] **QUEUE-05**: Worker recovery возвращает stale `processing` jobs обратно в `queued` без создания новой attempt.

### PDF Processor

- [x] **PDF-01**: Фактический JSON-контракт `pdf2txt` зафиксирован discovery-задачей.
- [x] **PDF-02**: Adapter вызывает `pdf2txt` через выбранный endpoint из endpoint pool.
- [x] **PDF-03**: Endpoint selection MVP использует `least in-flight`.
- [x] **PDF-04**: Endpoint и diagnostics сохраняются в истории attempt.
- [x] **PDF-05**: `InvalidInputProbe` включается только если discovery подтвердит безопасный validation response.

### Storage and Results

- [x] **STORE-01**: Входные PDF хранятся временно и удаляются после terminal state.
- [x] **STORE-02**: Result index хранит lightweight ссылку на payload.
- [x] **STORE-03**: PDF result payload хранится в result store подсистемы; для одного `capability + content_hash` удерживается актуальный payload без orphan JSON rows при повторном сохранении.
- [x] **STORE-04**: Temporary storage hard limit блокирует новые upload-ы с `503 temporary_storage_full`.
- [x] **STORE-05**: Public PDF upload supports selectable content hash algorithms through an enum-like request parameter: default `sha256` and `gost-r-34.11-2012-256` / Streebog-256; the algorithm prefix is part of `content_hash`.
- [x] **STORE-06**: Public PDF upload computes and stores aliases for every supported content hash algorithm so result/job lookup can accept any supported prefixed hash.

### Admin and Operations

- [x] **ADMIN-01**: Admin UI показывает минимальную видимость очереди, failed/blocked jobs и processor health.
- [x] **ADMIN-02**: Admin может выполнить manual retry одной failed/blocked задачи.
- [x] **ADMIN-03**: Audit фиксирует опасные admin actions.
- [x] **ADMIN-04**: Admin API отдаёт support report для Job Details без входного PDF, raw secrets и больших payload.
- [x] **ADMIN-05**: Первый admin может быть создан повторяемым bootstrap tool без Web setup wizard и без вывода секретов.
- [x] **ADMIN-06**: Admin Result Details показывает безопасный человекочитаемый summary PDF-result без raw payload.
- [x] **ADMIN-07**: WinForms test client получает текущий MVP service list через Admin Services registry API и запускает безопасные health/processor/Public API проверки.
- [x] **ADMIN-08**: Admin API предоставляет read-only registry зарегистрированных MVP-сервисов без секретов, connection strings и активного вызова внешних processor-ов.
- [x] **ADMIN-09**: Admin Result Details имеет отдельный controlled debug endpoint/download для raw JSON payload с auth, table allowlist и size limit.
- [x] **ADMIN-10**: Admin Storage/Settings показывают read-only retention policy MVP без запуска cleanup, dry-run или ручного удаления.
- [x] **HEALTH-01**: Web предоставляет `/health/live` и `/health/ready`.
- [x] **HEALTH-02**: Worker пишет heartbeat каждые `30 seconds`, stale threshold `3 minutes`.

### Delivery

- [ ] **DEPLOY-01**: MVP запускается через Docker Compose.
- [ ] **DEPLOY-02**: Compose содержит Web, Worker, PostgreSQL и shared local storage volume.

## v2 Requirements

- **ADMIN-V2-01**: Массовый retry по фильтру.
- **ADMIN-V2-02**: Полноценный экран поставки компонентов.
- **RET-V2-01**: Retention policy UI и активное удаление результатов.
- **BROKER-V2-01**: Внешний broker через `IJobQueue`, если PostgreSQL queue перестанет быть достаточной.
- **S3-V2-01**: S3-compatible storage для multi-node или больших payload.
- **AUTH-V2-01**: Сложные роли и права в админке.

## Out of Scope

| Feature | Reason |
|---------|--------|
| Windows Service в MVP | Docker Compose выбран основным путём поставки |
| Multi-node deployment | Требует устойчивого общего storage и усложняет cleanup |
| Создание processor-а через UI | Processor definitions должны быть заданы разработчиком |
| Отдельный `/status/{hash}` | `GET /results/{hash}` и `GET /jobs/{jobId}` покрывают сценарий MVP |
| Обычная обработка файла из админки для health | Админка не должна подменять клиента ЭП |

## Traceability

| Requirement | Phase | Status |
|-------------|-------|--------|
| ARCH-01 | Phase 1 | Done |
| ARCH-02 | Phase 1 | Done |
| ARCH-03 | Phase 1 | Done |
| ARCH-04 | Phase 2 | Done: embedded SQL migration runner with `schema_migrations` baseline, standalone `CenteralES.DatabaseMigrator`, and Development-or-explicit runtime auto-bootstrap |
| API-01 | Phase 1 | Done |
| API-02 | Phase 1 | Done |
| API-03 | Phase 1 | Done |
| API-04 | Phase 1 | Done |
| SEC-01 | Phase 2 | Done |
| SEC-02 | Phase 2 | Done |
| SEC-03 | Phase 2 | Done: auth enforcement plus Admin create/list/disable backend |
| SEC-04 | Phase 2 | Done |
| QUEUE-01 | Phase 1 | Done |
| QUEUE-02 | Phase 1 | Done |
| QUEUE-03 | Phase 1 | Done |
| QUEUE-04 | Phase 1 | Done |
| QUEUE-05 | Phase 2 | Done: job heartbeat based stale processing recovery returns current jobs to `queued` with `FOR UPDATE SKIP LOCKED` |
| PDF-01 | Phase 1 | Done |
| PDF-02 | Phase 1 | Done |
| PDF-03 | Phase 1 | Done |
| PDF-04 | Phase 1 | Done |
| PDF-05 | Phase 1 | Done |
| STORE-01 | Phase 1 | Done |
| STORE-02 | Phase 1 | Done: lightweight result index plus Admin Results metadata visibility |
| STORE-03 | Phase 1 | Done: PDF payload store, Admin Results avoiding raw payload exposure, repeated saves replacing the prior payload for the same hash |
| STORE-04 | Phase 2 | Done: hard-limit upload guard plus Admin Storage capacity visibility |
| STORE-05 | Phase 2 | Done: selectable prefixed content hash algorithms for Public PDF upload, including SHA-256 and GOST R 34.11-2012 / Streebog-256 |
| STORE-06 | Phase 2 | Done: all supported content hashes are stored as subject aliases and public lookup resolves by any alias |
| ADMIN-01 | Phase 1 | Done: `/admin` UI shell, Jobs/Job Details, Results, Processor Details, Health, Delivery, Storage, Settings plus Admin API |
| ADMIN-02 | Phase 2 | Done: Backend endpoint plus UI action for single-job retry |
| ADMIN-03 | Phase 2 | Done: manual retry/API key/admin user audit plus filtered Admin Audit UI with safe details |
| ADMIN-04 | Phase 2 | Done: Backend support report endpoint plus UI download action |
| ADMIN-05 | Phase 2 | Done: shared `IAdminBootstrapper` plus separate WinForms test bootstrap app, with backend smoke coverage for bootstrap -> login -> CSRF session validation |
| ADMIN-06 | Phase 4 | Done: safe PDF stamp recognition summary in Admin Result Details |
| ADMIN-07 | Phase 2 | Done: WinForms test client discovers current MVP service from `GET /api/admin/services` and tests health, passive processor status, and optional Public PDF upload/polling |
| ADMIN-08 | Phase 2 | Done: `GET /api/admin/services` returns safe registered service metadata for `pdf-stamp-recognition / pdf2txt-http-recognizer` |
| ADMIN-09 | Phase 4 | Done: `GET /api/admin/results/{resultIndexId}/payload` exposes controlled raw JSON debug payload for supported PDF results only |
| ADMIN-10 | Phase 4 | Done: `/api/admin/storage`, `/api/admin/settings`, and Admin UI expose read-only retention policy visibility |
| HEALTH-01 | Phase 2 | Done: Web endpoints plus Admin UI Health screen |
| HEALTH-02 | Phase 2 | Done |
| DEPLOY-01 | Phase 3 | Pending |
| DEPLOY-02 | Phase 3 | Pending |

**Coverage:**
- v1 requirements: 42 total
- Mapped to phases: 42
- Unmapped: 0

---
*Requirements defined: 2026-05-31*
*Last updated: 2026-06-02 after code review remediation*
