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
status = failed
final = true
retryAllowed = false/true
```

## Manual retry

Админка должна позволять:

- открыть failed subject;
- увидеть все attempts;
- увидеть ошибки;
- нажать manual retry;
- создать новую попытку;
- записать действие в audit.

## Worker recovery

Если worker умер во время обработки:

- heartbeat перестаёт обновляться;
- job считается abandoned;
- orchestrator решает, делать retry или final fail;
- админка показывает проблему.
