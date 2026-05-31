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
cancelled
```

Внутренние статусы:

```text
retry_scheduled
failed_retryable
failed_final
abandoned
```

## Heartbeat

Так как обработка может длиться больше 5 минут, worker должен обновлять heartbeat.

Если heartbeat давно не обновлялся:

- job считается abandoned;
- orchestrator решает, можно ли retry;
- админка показывает проблему.

## Индексы

Минимально нужны:

```text
unique processing_subjects(capability, content_hash)
index processing_jobs(status, scheduled_at)
index processing_jobs(subject_id, attempt_number)
index processing_jobs(heartbeat_at)
index processing_result_index(capability, content_hash)
```

## Будущая замена

Нужно проектировать через интерфейс:

```text
IJobQueue
```

Чтобы позже заменить PostgreSQL-backed queue на RabbitMQ, Redis, NATS или другой брокер без переписывания домена.
