# PDF Stamp Recognition

## Назначение

Первый capability проекта:

```text
pdf-stamp-recognition
```

Цель: обработать PDF-чертёж и получить из штампа:

- авторов;
- должности;
- подписантов;
- всех, кто должен подписать чертёж.

## Внешний сервис

Текущий внешний сервис:

```text
https://pdf2txt.selectel.dt1520.ru/recognize_json/
```

Он уже развёрнут как REST API.

Сервис принимает файл и возвращает JSON.

У текущего сервиса нет отдельного health endpoint, и пока нельзя рассчитывать на возможность добавить его в сам сервис.

Для MVP внешний сервис считается чёрным ящиком:

- контракт результата не меняем;
- обычную бизнес-обработку из админки не запускаем;
- доступность показываем через passive health по последним реальным вызовам;
- ручную диагностику допускаем только как отдельный безопасный сценарий.

Возможный диагностический вариант: отправить нулевой файл и считать ожидаемую ошибку валидации признаком того, что endpoint жив и обработчик отвечает. Этот вариант пока не утверждён, потому что фактический ответ `/recognize_json/` ещё не проверен. После проверки нужно зафиксировать ожидаемый HTTP status и тело ошибки.

Фактическая проверка 2026-06-01:

```text
request: multipart/form-data, file=empty.pdf, 0 bytes
status: 200 OK
content-type: application/json
```

Форма ответа:

```json
{
  "encoding": "utf-8",
  "file": "temp_*.pdf",
  "create_datetime": "01.06.2026 08:20:14",
  "errors": [
    "произошла ошибка - Cannot read an empty file",
    "В файле отсутствуют страницы для распознавания"
  ],
  "workers": [],
  "unrecognized_pages": [],
  "workers_page": {}
}
```

Вывод: нулевой файл можно использовать как `InvalidInputProbe`, но успешность probe должна определяться по `HTTP 200` плюс ожидаемым validation errors, а не по 4xx статусу. Этот probe остаётся отдельным диагностическим сценарием и не должен смешиваться с обычной пользовательской обработкой PDF.

Фактическая smoke-проверка валидным одностраничным PDF без штампа 2026-06-01:

```text
request: multipart/form-data, file=centerales-smoke.pdf
status: 200 OK
content-type: application/json
```

Форма ответа:

```json
{
  "encoding": "utf-8",
  "file": "temp_*.pdf",
  "create_datetime": "01.06.2026 08:21:51",
  "errors": [],
  "workers": [],
  "unrecognized_pages": [],
  "workers_page": {},
  "izm_number": ""
}
```

Вывод: успешный ответ внешнего сервиса — `HTTP 200` JSON. Для PDF без распознаваемого штампа `errors` пустой, но прикладные массивы тоже пустые. Adapter сохраняет этот JSON как raw payload, не преобразуя контракт.

Фактическая проверка локальным `test.pdf` 2026-06-01:

```text
file: test.pdf
git: ignored locally, not committed
status: 200 OK
top-level fields: encoding, file, create_datetime, errors, workers, unrecognized_pages, workers_page, izm_number
errors count: 0
workers count: 3
unrecognized_pages count: 0
workers_page keys: 2, 3, 15
```

Структура `workers` на этом примере: массив групп; первая группа содержит 2 строковых элемента. Значения не фиксируются в документации, чтобы не переносить содержимое тестового PDF в репозиторий.

## Ограничения

- ML-сервис считается чёрным ящиком.
- Мы не влияем на его внутреннюю логику.
- Контракт JSON результата уже существует.
- Мы не должны менять контракт результата для клиента.
- Обработка может длиться больше 5 минут.
- Входные PDF могут быть больше 100 МБ.

Baseline лимитов MVP:

```text
default max upload size: 250 MiB
hard safety max upload size: 500 MiB
connect timeout: 15 seconds
overall processor timeout: 15 minutes
max recommended processor timeout: 60 minutes
```

Если PDF превышает лимит загрузки, Public API возвращает `413 Payload Too Large`.

Текущий skeleton Web config:

```json
{
  "PdfStampRecognition": {
    "MaxUploadBytes": 262144000
  }
}
```

Если `PdfStampRecognition:MaxUploadBytes` не задан, используется default `250 MiB`. Значение выше hard safety limit `500 MiB` считается ошибкой конфигурации при старте.

## Как платформа должна работать с этим сервисом

Платформа не должна просто проксировать HTTP-запрос.

Она должна:

- принять файл;
- посчитать hash;
- проверить наличие результата;
- избежать повторной обработки;
- поставить job в очередь;
- вызвать внешний сервис из Worker;
- сохранить результат;
- вернуть клиенту jobId/hash/status;
- дать возможность получить результат позже;
- показать состояние в админке.

## Capability

```text
key: pdf-stamp-recognition
displayName: Распознавание штампа PDF
```

## Processor

```text
key: pdf2txt-http-recognizer
type: ExternalHttpProcessor
adapter: HttpPdfStampRecognizer
```

Текущее состояние реализации MVP:

- `FakePdfStampRecognizer` остаётся default-адаптером для локальной разработки и тестового end-to-end без внешнего сервиса.
- `HttpPdfStampRecognizer` добавлен как реальная HTTP boundary-реализация для `/recognize_json/`.
- Worker переключается между fake и HTTP adapter через конфигурацию `PdfStampRecognition:Recognizer`.
- Успешный JSON от внешнего сервиса сохраняется как сырой result payload без изменения контракта.
- Ошибки HTTP adapter нормализуются в стабильные `NormalizedProcessorError` и не пробрасывают raw body внешнего сервиса в Public API.
- Нулевой-file diagnostic probe подтверждён отдельно; HTTP adapter интерпретирует успешный JSON с непустым `errors` как `invalid_input`, не сохраняя raw external error messages в Public API.
- Smoke successful shape для валидного PDF без штампа зафиксирован: `errors: []`, `workers: []`, `workers_page: {}`, `unrecognized_pages: []`, `izm_number: ""`.
- Локальный `test.pdf` подтверждает прикладной successful result shape с непустым `workers` и page-indexed `workers_page`; файл добавлен в `.gitignore` и не должен коммититься.

## Runtime-настройки

Минимальный набор:

- enabled;
- endpoint pool;
- timeout;
- maxAttempts;
- retry delay;
- after max attempts policy;
- poolConcurrencyLimit;
- endpointConcurrencyLimit;
- health check mode;
- credentials, если понадобятся.

Текущие имена конфигурации Worker:

```json
{
  "PdfStampRecognition": {
    "Recognizer": "Fake",
    "Processor": {
      "endpointPool": [
        "https://pdf2txt.selectel.dt1520.ru/recognize_json/"
      ],
      "poolConcurrencyLimit": 4,
      "endpointConcurrencyLimit": 2,
      "timeout": "00:00:30",
      "maxAttempts": 5,
      "processorOverloadedDelay": "00:00:15"
    }
  }
}
```

`pdf2txt-http-recognizer` может быть развёрнут несколькими одинаковыми Docker-контейнерами. В этом случае в админке задаётся список endpoint-ов одного pool-а, например пять URL. Это один сервис и один processor instance, а не пять разных processor-ов.

Распределение нагрузки в MVP:

- Worker выбирает endpoint на момент выполнения attempt;
- выбираются только enabled endpoint-ы без явного unhealthy;
- алгоритм: `least in-flight`;
- общий лимит pool-а ограничивает суммарную параллельность;
- per-endpoint limit ограничивает нагрузку на конкретный контейнер;
- выбранный endpoint записывается в историю attempt.

Default для endpoint pool:

```text
endpointConcurrencyLimit: 2
poolConcurrencyLimit: endpoint count * endpointConcurrencyLimit
```

Например, для пяти endpoint-ов:

```text
endpointConcurrencyLimit: 2
poolConcurrencyLimit: 10
```

Если все endpoint-ы заняты, это не считается ошибкой обработки. Job остаётся в очереди или получает короткий `scheduled_at` delay на 10-30 секунд.

## Классификация ошибок processor-а

Внешний PDF processor нормализует ошибки в стабильные коды:

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

Retry behavior:

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

`processor_overloaded` не должен создавать failed attempt. Это нормальное состояние насыщения pool-а.

## ResultStore

Результат PDF должен храниться в таблице подсистемы:

```text
pdf_stamp_recognition_results
```

Общий индекс результата должен ссылаться на эту таблицу.

## API

Примерные endpoint-ы:

```http
POST /api/pdf-stamp-recognition/jobs
GET  /api/pdf-stamp-recognition/results/{hash}
GET  /api/jobs/{jobId}
```

## Ответ при новой обработке

```json
{
  "hash": "H",
  "jobId": "J1",
  "attemptNumber": 1,
  "status": "queued",
  "deduplicated": false
}
```

## Ответ при уже идущей обработке

```json
{
  "hash": "H",
  "jobId": "J1",
  "attemptNumber": 1,
  "status": "processing",
  "deduplicated": true
}
```

## Ответ при готовом результате

```json
{
  "hash": "H",
  "jobId": "J1",
  "status": "completed",
  "result": {}
}
```

Поле `result` содержит существующий внешний JSON-контракт.

## Что должна показывать админка

Для PDF capability:

- включён ли обработчик;
- endpoint pool;
- health каждого endpoint-а;
- in-flight по pool и по endpoint;
- health;
- текущие jobs;
- количество queued/processing/failed;
- последние ошибки внешнего сервиса;
- цепочку попыток по hash;
- кнопку manual retry;
- timeout и retry policy;
- общий лимит параллельности pool-а;
- лимит параллельности endpoint-а;
- размер временных файлов.
