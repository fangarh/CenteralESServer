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

## Ограничения

- ML-сервис считается чёрным ящиком.
- Мы не влияем на его внутреннюю логику.
- Контракт JSON результата уже существует.
- Мы не должны менять контракт результата для клиента.
- Обработка может длиться больше 5 минут.
- Входные PDF могут быть больше 100 МБ.

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
adapter: Pdf2TxtHttpProcessorAdapter
```

## Runtime-настройки

Минимальный набор:

- enabled;
- endpoint;
- timeout;
- maxAttempts;
- retry delay;
- after max attempts policy;
- concurrencyLimit;
- health check mode;
- credentials, если понадобятся.

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
- endpoint;
- health;
- текущие jobs;
- количество queued/processing/failed;
- последние ошибки внешнего сервиса;
- цепочку попыток по hash;
- кнопку manual retry;
- timeout и retry policy;
- лимит параллельности;
- размер временных файлов.
