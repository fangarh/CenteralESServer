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

или

```http
409 Conflict
```

Точный код нужно согласовать.

Пример тела:

```json
{
  "hash": "H",
  "jobId": "J5",
  "attemptNumber": 5,
  "status": "failed",
  "final": true,
  "retryAllowed": false,
  "message": "Processing failed after configured retry attempts"
}
```

## GET /api/pdf-stamp-recognition/results/{hash}

Назначение:

- получить результат по hash;
- если результат готов, вернуть его;
- если job идёт, вернуть текущий jobId и status;
- если ничего не известно, вернуть отсутствие данных.

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
  "hash": "H",
  "capability": "pdf-stamp-recognition",
  "attemptNumber": 3,
  "status": "processing",
  "maxAttempts": 5,
  "retrying": true,
  "previousAttempts": 2
}
```

## Статусы наружу

Для клиента ЭП статусы должны быть простыми:

```text
queued
processing
completed
failed
cancelled
```

Внутренние статусы могут быть подробнее:

```text
retry_scheduled
failed_retryable
failed_final
abandoned
```

## Дедупликация

Если два клиента отправили один файл для одной capability:

- повторная обработка не запускается;
- оба клиента получают один текущий jobId;
- результат будет доступен по одному hash.

## Ошибки доступа

```http
401 Unauthorized
```

Ключ отсутствует или неверный.

```http
403 Forbidden
```

Ключ валиден, но capability не разрешена.

## Важные нерешённые детали

- точный код ответа для `failed_final`;
- нужен ли endpoint отмены job;
- нужен ли отдельный status endpoint по hash;
- максимальный размер файла;
- формат ошибок API;
- формат correlation id.
