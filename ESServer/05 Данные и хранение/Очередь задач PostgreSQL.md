# Очередь задач PostgreSQL

## Выбранный подход

Для MVP выбрана очередь задач на PostgreSQL.

Внешний брокер пока не добавляем.

## Почему PostgreSQL

Плюсы:

- PostgreSQL уже нужен проекту;
- меньше инфраструктуры;
- проще поставка;
- проще локальная разработка;
- достаточно для MVP;
- можно масштабировать worker-ы через SQL-блокировки.

Минусы:

- это не специализированный брокер;
- нужно аккуратно проектировать индексы;
- нужно следить за ростом таблиц;
- нужна политика heartbeat/recovery.

## Основные таблицы

### processing_subjects

Представляет объект обработки по capability и hash.

```text
id
capability
content_hash
state
current_job_id
result_id
created_at
updated_at
```

Уникальность:

```text
unique(capability, content_hash)
```

### processing_jobs

Представляет конкретную попытку обработки.

```text
id
subject_id
capability
content_hash
attempt_number
status
scheduled_at
started_at
completed_at
heartbeat_at
processor_instance_id
error_code
error_message
error_is_retryable
created_at
updated_at
```

### processing_result_index

Лёгкий индекс результата.

```text
id
subject_id
capability
content_hash
job_id
result_kind
payload_table
payload_id
contract_version
payload_size
created_at
```

## Выбор job worker-ом

Worker должен выбирать задачи конкурентно безопасно.

Подход:

```sql
FOR UPDATE SKIP LOCKED
```

Идея:

```text
1. Worker открывает транзакцию.
2. Выбирает следующую job со status queued/retry_scheduled.
3. Блокирует строку.
4. Меняет status на processing.
5. Фиксирует транзакцию.
6. Выполняет обработку.
```

Так несколько worker-ов не возьмут одну job одновременно.

## Статусы

Внешние статусы:

```text
queued
processing
completed
failed
blocked
cancelled
```

`blocked` означает, что лимит retry исчерпан и нужна ручная операция администратора. Наружу это отдельный статус, а не обычный `failed`, чтобы клиент ЭП не пытался бесконечно ждать или перезапускать обработку.

Внутренние статусы:

```text
retry_scheduled
failed_retryable
failed_final
abandoned
```

## Heartbeat

Так как обработка может длиться больше 5 минут, нужны два heartbeat:

- worker heartbeat показывает, что процесс Worker жив;
- job heartbeat показывает, что конкретная `processing` job ещё удерживается активным Worker-ом.

Baseline MVP:

```text
heartbeat interval: 30 seconds
stale heartbeat threshold: 3 minutes
```

В текущем skeleton:

- `CenteralES.Worker` пишет worker heartbeat в `processing_worker_heartbeats`;
- при `ClaimNext` очередь выставляет `processing_jobs.heartbeat_at`;
- пока job обрабатывается, Worker периодически обновляет `processing_jobs.heartbeat_at` через `RefreshHeartbeatAsync`;
- `Complete`, `Fail` и `Defer` завершают или возвращают job в очередь, поэтому дальнейший job heartbeat не нужен.

Если heartbeat давно не обновлялся:

- job считается stale;
- Worker recovery периодически возвращает такую job из `processing` в `queued`;
- `attempt_number` не меняется, новая attempt не создаётся;
- `started_at` и `heartbeat_at` очищаются;
- `scheduled_at` выставляется в момент recovery, чтобы job могла быть забрана снова;
- recovery применяет current-subject guard: восстанавливается только job, которая всё ещё является `processing_subjects.current_job_id`;
- SQL использует `FOR UPDATE SKIP LOCKED`, чтобы несколько worker-ов не восстанавливали одну строку одновременно.

MVP defaults:

```text
job heartbeat interval: 30 seconds
stale job timeout: 5 minutes
recovery interval: 1 minute
recovery batch size: 50
```

Recovery не пишет failed diagnostics и не переводит job в `blocked`, потому что stale processing обычно означает падение/остановку Worker-а, а не ошибку внешнего processor-а.

Admin Processor status показывает пассивный счётчик `staleProcessing`: количество `processing` jobs, у которых `coalesce(heartbeat_at, started_at, updated_at)` старше recovery timeout. Этот счётчик нужен для видимости recovery риска и не является отдельным status в таблице.

Если все endpoint-ы processor pool заняты по concurrency limit, это не считается failed attempt. Job остаётся в очереди или получает короткий `scheduled_at` delay:

```text
default delay: 10-30 seconds
```

## Индексы

Минимально нужны:

```text
unique processing_subjects(capability, content_hash)
index processing_jobs(status, scheduled_at)
index processing_jobs(subject_id, attempt_number)
index processing_jobs(heartbeat_at)
index processing_result_index(capability, content_hash)
```

## Миграции схемы

Схема PostgreSQL применяется явным SQL migration runner-ом без Entity Framework.

Текущий baseline:

```text
src/Shared/CenteralES.Infrastructure/Postgres/Migrations/0001_processing_baseline.sql
```

Runner:

- загружает embedded `.sql` migrations из `CenteralES.Infrastructure`;
- сортирует их по id;
- перед применением создает служебную таблицу `schema_migrations`, если ее еще нет;
- пропускает уже примененные migration id;
- применяет каждую новую migration в отдельной транзакции;
- пишет marker в `schema_migrations` только после успешного выполнения SQL.

Web и Worker не выполняют schema SQL напрямую. Точка входа остается:

```text
PostgresDatabaseBootstrapper.ApplySchemaAsync
```

Runtime auto-bootstrap теперь включается политикой окружения:

- в `Development` Web/Worker могут создать target database и применить SQL migrations автоматически;
- вне `Development` auto-bootstrap выключен по умолчанию и включается только явным `Database:AutoBootstrap=true`;
- production/Docker path выполняет отдельный bootstrap/migration шаг через `CenteralES.DatabaseMigrator`; runtime auto-bootstrap для Web/Worker не требуется.

Это сохраняет локальный startup/bootstrap contract и дает точку расширения для следующих миграций без EF, но не требует `CREATE DATABASE` прав от production runtime по умолчанию.

Production migration command:

```powershell
dotnet run --project src/Apps/CenteralES.DatabaseMigrator -- --connection-string "<postgres connection string>"
```

Если target database уже создана отдельным DBA/init-container шагом:

```powershell
dotnet run --project src/Apps/CenteralES.DatabaseMigrator -- --connection-string "<postgres connection string>" --no-create-database
```

CLI не выводит connection string. При отсутствии `--connection-string` используется `CENTERALES_PROCESSING_DATABASE`, а для локальной разработки допускается resolver через ignored `db.env`.

## Будущая замена

Нужно проектировать через интерфейс:

```text
IJobQueue
```

Чтобы позже заменить PostgreSQL-backed queue на RabbitMQ, Redis, NATS или другой брокер без переписывания домена.
