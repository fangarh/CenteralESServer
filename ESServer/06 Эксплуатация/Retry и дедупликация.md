# Retry и дедупликация

## Дедупликация

Система должна избегать повторной обработки одинакового файла.

Ключ дедупликации:

```text
capability + content_hash
```

Если тот же файл отправлен из двух мест:

- второй job не создаётся, если активная job уже есть;
- клиенту возвращается текущий jobId;
- результат потом доступен обоим клиентам.

## ProcessingSubject

`ProcessingSubject` представляет обрабатываемый объект:

```text
capability
content_hash
current_job_id
state
result_id
```

Это не попытка обработки, а “предмет обработки”.

## ProcessingJob

`ProcessingJob` представляет конкретную попытку.

```text
jobId = id конкретной попытки
```

Пример:

```text
hash H
  job J1, attempt 1 -> failed
  job J2, attempt 2 -> failed
  job J3, attempt 3 -> processing
```

## Почему jobId — это попытка

Потому что при падениях нужно видеть историю:

- какая попытка упала;
- с какой ошибкой;
- какой processor использовался;
- когда был retry;
- какая попытка дала результат.

История attempt хранит не только статус, но и диагностический контекст внешнего вызова: выбранный endpoint, длительность, HTTP status, нормализованный код ошибки, retryable true/false и correlationId.

Диагностика attempt отделена от result payload. Она нужна для админки, retry-решений и support report, но не считается бизнес-результатом capability.

## Ответ клиенту

Если идёт повторная попытка:

```json
{
  "hash": "H",
  "jobId": "J3",
  "attemptNumber": 3,
  "status": "processing",
  "retrying": true,
  "previousAttempts": 2
}
```

## Retry policy

Настраивается в админке на уровне Processor Instance или Capability.

Настройки:

```text
maxAttempts
retryDelay
retryStrategy
afterMaxAttempts
cooldown
```

## Retry strategies

Возможные стратегии:

```text
immediate
fixed_delay
exponential_backoff
```

Для MVP можно начать с fixed delay.

Текущее состояние skeleton:

```text
retryDelay: 30 seconds
strategy: fixed_delay
maxAttempts: 5
processorOverloadedDelay: 15 seconds
afterMaxAttempts: blocked/admin_required
```

Если Worker получает retryable processor error, текущая job фиксируется как `failed` с diagnostics, а очередь создаёт новую `queued` job для того же `ProcessingSubject`, `content_hash` и `temporary_file_key` с `attemptNumber + 1`. `processing_subjects.current_job_id` переводится на новую попытку, поэтому Public API снова видит активную обработку.

Если ошибка non-retryable или текущая попытка достигла `maxAttempts`, subject переводится в terminal `blocked`, и Worker чистит временный PDF после записи состояния очереди.

`processor_overloaded` обрабатывается отдельно: это не failed attempt, а defer текущей job обратно в `queued` с коротким `scheduled_at`.

## After max attempts

Поведение после исчерпания попыток настраивается.

Варианты:

```text
admin_required
  новая серия попыток только вручную через админку

client_may_restart
  повторный запрос клиента может начать новую серию

client_may_restart_after_cooldown
  клиент может начать новую серию после cooldown
```

Безопасный default:

```text
admin_required
```

## Failed final

Если попытки исчерпаны:

```text
status = failed_final
```

Клиенту наружу:

```text
status = blocked
final = true
retryAllowed = false/true
```

Для Public API `blocked` означает, что система больше не делает автоматический retry. В безопасном MVP-контуре новая серия попыток запускается через manual retry в админке.

## Retry по типам ошибок

```text
invalid_input -> no retry
processor_timeout -> retry
processor_unreachable -> retry
processor_http_error -> retry
processor_bad_response -> retry
processor_contract_error -> limited retry + admin warning
processor_overloaded -> no failed attempt, queue delay
temporary_storage_full -> no processor retry
internal_error -> depends on transient flag
```

`processor_overloaded` означает, что все endpoint-ы processor pool заняты. Это не ошибка обработки и не failed attempt; job остаётся в очереди или получает короткий `scheduled_at` delay.

В текущем skeleton `internal_error` считается non-transient по умолчанию. Worker помечает такую попытку terminal/blocked, пишет diagnostics и чистит временный PDF, чтобы не создавать бесконечный retry loop для ошибок приложения или потерянного temporary storage.

`processor_contract_error` retry-ится ограниченно, но обязательно подсвечивается в админке как возможное изменение внешнего сервиса или нарушение ожидаемого JSON-контракта.

Для `processor_bad_response` и `processor_contract_error` attempt может сохранять обрезанный raw diagnostic excerpt, чтобы не терять причину сбоя. Полный response body и большие payload в историю attempts не пишутся.

## Manual retry

Админка должна позволять:

- открыть failed subject;
- увидеть все attempts;
- увидеть ошибки;
- нажать manual retry;
- создать новую попытку;
- записать действие в audit.

Текущий backend skeleton:

```http
POST /api/admin/jobs/{jobId}/retry
```

Endpoint:

- требует admin session cookie;
- требует `X-CSRF-Token`;
- разрешает retry только для текущей `failed` или `blocked` job;
- создаёт новую `queued` attempt с `attemptNumber + 1`;
- переводит `processing_subjects.current_job_id` на новую attempt;
- пишет append-only audit event `manual_retry_job`;
- возвращает `409 retry_not_allowed`, если job не является текущей failed/blocked попыткой.

Ограничение текущего skeleton: если temporary input уже удалён после terminal state, новая attempt будет создана, но Worker не сможет открыть файл и безопасно переведёт попытку в terminal error. Для полноценного ручного retry после cleanup нужен отдельный retention/reupload/result-storage сценарий.

## Worker recovery

Если worker умер во время обработки:

- heartbeat перестаёт обновляться;
- job считается abandoned;
- orchestrator решает, делать retry или final fail;
- админка показывает проблему.
