# Requirements: CenteralESServer MVP

**Defined:** 2026-05-31  
**Core Value:** Клиент ЭП должен надёжно отправить PDF, не зависнуть на долгой обработке и получить результат или понятный статус по hash/job.

## v1 Requirements

### Architecture

- [ ] **ARCH-01**: Solution разделён на Web, Worker, модули домена/application/infrastructure и tests.
- [ ] **ARCH-02**: Реализация не использует Entity Framework.
- [ ] **ARCH-03**: Основные бизнес-операции покрываются TDD-тестами до или вместе с реализацией.

### Public API

- [ ] **API-01**: `POST /api/pdf-stamp-recognition/jobs` принимает PDF и возвращает `200` для готового результата или `202` для активной обработки.
- [ ] **API-02**: `GET /api/pdf-stamp-recognition/results/{hash}` возвращает `200`, `202` или `404` по согласованному контракту.
- [ ] **API-03**: `GET /api/jobs/{jobId}` возвращает статус конкретной попытки без raw errors.
- [ ] **API-04**: Public API использует единый формат ошибок с `correlationId`.

### Security

- [ ] **SEC-01**: Public API принимает `Authorization: ApiKey <keyId>.<secret>`.
- [ ] **SEC-02**: Сервер хранит только hash API key secret.
- [ ] **SEC-03**: API key может быть отключён и ограничен allowed capabilities.

### Queue and Processing

- [ ] **QUEUE-01**: Задачи хранятся в PostgreSQL-backed queue.
- [ ] **QUEUE-02**: Worker выбирает задачи конкурентно безопасно.
- [ ] **QUEUE-03**: Дедупликация использует `capability + content_hash`.
- [ ] **QUEUE-04**: Retry policy поддерживает max attempts и безопасный final state `blocked`.

### PDF Processor

- [ ] **PDF-01**: Фактический JSON-контракт `pdf2txt` зафиксирован discovery-задачей.
- [ ] **PDF-02**: Adapter вызывает `pdf2txt` через выбранный endpoint из endpoint pool.
- [ ] **PDF-03**: Endpoint selection MVP использует `least in-flight`.
- [ ] **PDF-04**: Endpoint и diagnostics сохраняются в истории attempt.
- [ ] **PDF-05**: `InvalidInputProbe` включается только если discovery подтвердит безопасный validation response.

### Storage and Results

- [ ] **STORE-01**: Входные PDF хранятся временно и удаляются после terminal state.
- [ ] **STORE-02**: Result index хранит lightweight ссылку на payload.
- [ ] **STORE-03**: PDF result payload хранится в result store подсистемы.
- [ ] **STORE-04**: Temporary storage hard limit блокирует новые upload-ы с `503 temporary_storage_full`.

### Admin and Operations

- [ ] **ADMIN-01**: Admin UI показывает минимальную видимость очереди, failed/blocked jobs и processor health.
- [ ] **ADMIN-02**: Admin может выполнить manual retry одной failed/blocked задачи.
- [ ] **ADMIN-03**: Audit фиксирует опасные admin actions.
- [ ] **HEALTH-01**: Web предоставляет `/health/live` и `/health/ready`.
- [ ] **HEALTH-02**: Worker пишет heartbeat каждые `30 seconds`, stale threshold `3 minutes`.

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
| ARCH-01 | Phase 1 | Pending |
| ARCH-02 | Phase 1 | Pending |
| ARCH-03 | Phase 1 | Pending |
| API-01 | Phase 1 | Pending |
| API-02 | Phase 1 | Pending |
| API-03 | Phase 1 | Pending |
| API-04 | Phase 1 | Pending |
| SEC-01 | Phase 2 | Pending |
| SEC-02 | Phase 2 | Pending |
| SEC-03 | Phase 2 | Pending |
| QUEUE-01 | Phase 1 | Pending |
| QUEUE-02 | Phase 1 | Pending |
| QUEUE-03 | Phase 1 | Pending |
| QUEUE-04 | Phase 1 | Pending |
| PDF-01 | Phase 1 | Pending |
| PDF-02 | Phase 1 | Pending |
| PDF-03 | Phase 1 | Pending |
| PDF-04 | Phase 1 | Pending |
| PDF-05 | Phase 1 | Pending |
| STORE-01 | Phase 1 | Pending |
| STORE-02 | Phase 1 | Pending |
| STORE-03 | Phase 1 | Pending |
| STORE-04 | Phase 2 | Pending |
| ADMIN-01 | Phase 1 | Pending |
| ADMIN-02 | Phase 2 | Pending |
| ADMIN-03 | Phase 2 | Pending |
| HEALTH-01 | Phase 2 | Pending |
| HEALTH-02 | Phase 2 | Pending |
| DEPLOY-01 | Phase 3 | Pending |
| DEPLOY-02 | Phase 3 | Pending |

**Coverage:**
- v1 requirements: 30 total
- Mapped to phases: 30
- Unmapped: 0

---
*Requirements defined: 2026-05-31*
*Last updated: 2026-05-31 after GSD initialization*
