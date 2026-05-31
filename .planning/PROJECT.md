# CenteralESServer

## Что это

CenteralESServer — серверная платформа для desktop-приложения сервиса электронного подписания. Первый MVP-сценарий — асинхронное распознавание штампа PDF через внешний `pdf2txt` processor с очередью, retry, дедупликацией по hash, результатом по API и минимальной админской видимостью.

Проект не является одним PDF endpoint. Это основа расширяемой платформы capability/processor для будущих серверных функций сервиса ЭП.

## Core Value

Клиент ЭП должен надёжно отправить PDF, не зависнуть на долгой обработке и получить результат или понятный статус по hash/job.

## Requirements

### Validated

(Пока нет — реализация ещё не началась.)

### Active

- [ ] Реализовать первый vertical slice: Public API -> PostgreSQL queue -> Worker -> `pdf2txt` adapter -> result retrieval.
- [ ] Сохранить архитектурные границы: модульный монолит по коду, отдельные Web и Worker процессы.
- [ ] Соблюсти ограничения: .NET 9, PostgreSQL, без Entity Framework, Clean Architecture, DDD, TDD.
- [ ] Обеспечить операционную основу: retry, diagnostics, health, API keys, минимальная админская видимость.
- [ ] Проверить фактический контракт текущего `pdf2txt` сервиса до финализации adapter contract.

### Out of Scope

- Полноценные микросервисы — преждевременно для MVP и усложняет поставку.
- Windows Service/Linux daemon — Docker Compose является основным путём поставки MVP.
- Внешний broker RabbitMQ/Redis/Kafka/NATS — PostgreSQL-backed queue достаточна для первого релиза.
- Multi-node deployment — откладывается до появления устойчивого общего storage.
- Создание processor-ов с нуля через Admin UI — в MVP processor definitions задаются разработчиком.
- Retention UI и активное удаление результатов — откладывается до реальных требований.
- Отдельный status endpoint по hash — `GET /results/{hash}` и `GET /jobs/{jobId}` покрывают MVP.

## Context

Источник истины по архитектуре до и после старта GSD: Obsidian-заметки в `ESServer`.

Главные согласованные решения:

- модель: `Capability -> Processor -> Processor Instance -> Job -> Result`;
- Public API использует доменные endpoint-ы для клиента ЭП;
- Web принимает запросы, считает hash, ставит задачи, отдаёт status/result;
- Worker выполняет тяжёлую обработку, retry, вызов processor-ов и cleanup;
- очередь реализуется в PostgreSQL через конкурентный выбор задач, например `FOR UPDATE SKIP LOCKED`;
- входные файлы временные, результаты хранятся как cache по `capability + content_hash`;
- текущий `https://pdf2txt.selectel.dt1520.ru/recognize_json/` считается чёрным ящиком.

## Constraints

- **Tech stack**: .NET 9, PostgreSQL, без Entity Framework — исходное требование проекта.
- **Architecture**: Clean Architecture + DDD + TDD — обязательный стиль реализации.
- **Deployment**: Docker Compose, single-node, Web + один или несколько Worker — основной MVP-путь.
- **External dependency**: `pdf2txt` может быть недоступен и не имеет health endpoint — adapter должен быть устойчив к недоступности.
- **Security**: Public API использует `Authorization: ApiKey <keyId>.<secret>`, secret хранится только hash-ом.
- **Admin**: Admin UI для прикладного администратора, не DevOps; сложные роли и multi-tenant не входят в MVP.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Модульный монолит по коду | Меньше инфраструктурной сложности, проще первый релиз | Pending |
| Web и Worker как разные процессы | Долгая обработка не должна блокировать API и админку | Pending |
| PostgreSQL-backed queue | PostgreSQL уже нужен, внешний broker не нужен для MVP | Pending |
| Docker Compose вместо Windows Service | Docker полностью закрывает поставку MVP | Pending |
| Endpoint pool как часть processor instance | Несколько контейнеров `pdf2txt` являются одним сервисом для распределения нагрузки | Pending |
| Health без бизнес-обработки файлов | Админка не должна подменять клиента ЭП и дергать processor опасными тестами | Pending |
| Obsidian как источник истины | GSD-планы должны быть производными от согласованной архитектуры | Pending |

## Evolution

После каждой фазы:

- обновлять `REQUIREMENTS.md` по факту выполненного;
- переносить проверенные требования в Validated;
- если GSD-план конфликтует с Obsidian, сначала обновлять Obsidian-решение;
- фиксировать новые решения в `ESServer` и затем синхронизировать `.planning`.

---
*Last updated: 2026-05-31 after GSD initialization from Obsidian notes*
