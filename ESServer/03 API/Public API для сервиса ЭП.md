# Public API для сервиса ЭП

## Назначение

Public API предназначен для desktop-приложения сервиса ЭП.

Он должен быть простым для клиента, но внутри использовать расширяемую модель платформы.

## Выбранный стиль

Выбран гибридный стиль:

- снаружи endpoint-ы понятны по доменной функции;
- внутри они работают через `Capability -> Job -> Processor -> Result`.

Не делаем основной endpoint вида:

```http
POST /api/capabilities/{capability}/jobs
```

Пока это слишком абстрактно для desktop-клиента.

## Пример для PDF

```http
POST /api/pdf-stamp-recognition/jobs
GET  /api/pdf-stamp-recognition/results/{hash}
GET  /api/jobs/{jobId}
```

## POST /api/pdf-stamp-recognition/jobs

Назначение:

- принять PDF;
- проверить API key;
- посчитать content hash;
- найти готовый результат;
- найти активную job;
- создать job, если нужно;
- вернуть hash, jobId, статус и номер попытки.

## Варианты ответа

`POST /api/pdf-stamp-recognition/jobs` использует content hash как идемпотентность первого релиза.

Базовые варианты:

- `200 OK`, если результат уже есть;
- `202 Accepted`, если создана новая job;
- `202 Accepted`, если активная job по этому hash уже есть;
- `200 OK`, если обработка по этому hash уже заблокирована после исчерпания retry;
- `400 Bad Request`, если файл или запрос невалиден;
- `413 Payload Too Large`, если файл превышает лимит загрузки;
- `403 Forbidden`, если API key не имеет права на capability;
- `409 Conflict`, если capability выключена настройкой;
- `503 Service Unavailable`, если capability временно недоступна из-за состояния processor-а.
- `503 Service Unavailable`, если temporary storage заполнен.

### Результат уже готов

```http
200 OK
```

```json
{
  "hash": "H",
  "jobId": "J1",
  "status": "completed",
  "result": {}
}
```

### Новая job создана

```http
202 Accepted
```

```json
{
  "hash": "H",
  "jobId": "J2",
  "attemptNumber": 1,
  "status": "queued",
  "deduplicated": false
}
```

### Такая job уже идёт

```http
202 Accepted
```

```json
{
  "hash": "H",
  "jobId": "J2",
  "attemptNumber": 1,
  "status": "processing",
  "deduplicated": true
}
```

### Обработка упала окончательно

```http
200 OK
```

Пример тела:

```json
{
  "hash": "H",
  "jobId": "J5",
  "attemptNumber": 5,
  "status": "blocked",
  "final": true,
  "retryAllowed": false,
  "message": "Processing is blocked after configured retry attempts"
}
```

## GET /api/pdf-stamp-recognition/results/{hash}

Назначение:

- получить результат по hash;
- если результат готов, вернуть его;
- если job идёт, вернуть текущий jobId и status;
- если ничего не известно, вернуть отсутствие данных.

Коды:

- `200 OK`, если результат есть;
- `202 Accepted`, если есть активная обработка по этому hash;
- `404 Not Found`, если результата нет и активной обработки нет;
- `410 Gone` не нужен в MVP, потому что retention результатов пока бессрочный.

Отдельный endpoint статуса по hash в MVP не добавляем.

Причина: `GET /api/pdf-stamp-recognition/results/{hash}` уже покрывает публичный сценарий клиента ЭП:

- `200 OK` — результат готов;
- `202 Accepted` — обработка идёт, в ответе есть текущий `jobId`;
- `404 Not Found` — результата и активной обработки нет.

Если клиенту нужны детали конкретной попытки, он использует `GET /api/jobs/{jobId}`.

Отдельный `/status/{hash}` можно добавить позже, если появится другой клиентский сценарий, где нужен лёгкий status без result payload.

## GET /api/jobs/{jobId}

Назначение:

- получить технический статус job;
- показать номер попытки;
- показать текущий state;
- показать retry information.

Пример:

```json
{
  "jobId": "J3",
  "capability": "pdf-stamp-recognition",
  "contentHash": "sha256:...",
  "status": "processing",
  "attempt": 3,
  "maxAttempts": 5,
  "createdAt": "...",
  "updatedAt": "...",
  "resultAvailable": false,
  "resultUrl": null,
  "error": null,
  "correlationId": "..."
}
```

Для failed/blocked job ошибка возвращается в нормализованном виде без raw details:

```json
{
  "status": "blocked",
  "error": {
    "code": "processor_timeout",
    "message": "Processing failed after retry attempts.",
    "correlationId": "..."
  }
}
```

## Статусы наружу

Для клиента ЭП статусы должны быть простыми:

```text
queued
processing
completed
failed
blocked
cancelled
```

Смысл:

- `queued`: задача создана и ждёт Worker;
- `processing`: Worker взял задачу;
- `completed`: результат сохранён;
- `failed`: текущая попытка упала, но система ещё может retry;
- `blocked`: лимит retry исчерпан, нужен manual retry через админку;
- `cancelled`: отменено пользователем или админом, но endpoint отмены не входит в MVP.

Внутренние статусы могут быть подробнее:

```text
retry_scheduled
failed_retryable
failed_final
abandoned
```

Endpoint отмены job в MVP не добавляем. Причина: для него нужна полноценная cooperative cancellation, cleanup временных файлов и понятная политика отмены внешнего HTTP-вызова. Статус `cancelled` оставляем в модели как future-ready состояние для админских или будущих пользовательских сценариев.

## Дедупликация

Если два клиента отправили один файл для одной capability:

- повторная обработка не запускается;
- оба клиента получают один текущий jobId;
- результат будет доступен по одному hash.

## Ошибки доступа

```http
401 Unauthorized
```

Ключ отсутствует, формат неверный, `keyId` не найден, secret не совпал, ключ отключён или истёк.

```http
403 Forbidden
```

Ключ валиден, но capability не разрешена.

## Нормализованные ошибки

Public API и Admin UI используют стабильные error codes:

```text
invalid_input
processor_timeout
processor_unreachable
processor_http_error
processor_bad_response
processor_contract_error
processor_overloaded
temporary_storage_full
internal_error
```

## Формат ошибок

Public API возвращает единый формат ошибки:

```json
{
  "error": {
    "code": "processor_unavailable",
    "message": "PDF recognition is temporarily unavailable.",
    "details": null,
    "correlationId": "..."
  }
}
```

Внешние raw errors клиенту ЭП не отдаём. Они доступны в админке, audit и support report.

## Важные нерешённые детали

Точные поля result payload зависят от фактического JSON-контракта текущего PDF ML-сервиса.
