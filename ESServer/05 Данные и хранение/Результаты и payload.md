# Результаты и payload

## Главная договорённость

Общая таблица результатов не хранит большие payload.

Она хранит только индекс и ссылку.

Payload хранится в таблицах конкретных подсистем или в файловом storage, если результатом является файл.

## Почему так

Если хранить все JSON/payload в общей таблице, со временем возникнут проблемы:

- таблица станет большой и тяжёлой;
- списки jobs/results начнут читать лишние данные;
- разные модули будут конкурировать за одну таблицу;
- сложнее индексировать поля конкретного обработчика;
- новый модуль может резко ухудшить производительность общей таблицы.

Поэтому правило должно быть единым:

> Каждый capability имеет свой ResultStore и своё место хранения payload.

## Общий индекс

```text
processing_result_index
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

Эта таблица должна быть лёгкой.

Она нужна для:

- быстрого поиска результата по hash;
- связи с job;
- отображения списков в админке;
- определения, где лежит payload;
- общей навигации.

## Payload таблицы подсистем

Пример для PDF:

```text
pdf_stamp_recognition_results
  id
  result_index_id
  payload_json jsonb
  contract_version
  created_at
```

Пример для изображений:

```text
image_processing_results
  id
  result_index_id
  metadata_json jsonb
  primary_artifact_id
  created_at
```

## Result artifacts

Если результатом является файл или набор файлов, используется `result_artifacts`.

```text
result_artifacts
  id
  result_index_id
  artifact_kind
  storage_provider
  storage_key
  file_name
  content_type
  size_bytes
  checksum
  created_at
```

## Типы результата

```text
json
file
artifact_set
json_and_files
external_ref
```

Для PDF в MVP:

```text
result_kind = json
payload table = pdf_stamp_recognition_results
```

## Большие JSON-результаты

В MVP не вводим отдельное приложенческое сжатие JSON-результатов.

Базовый подход:

- JSON payload хранится в result store конкретной подсистемы;
- общий индекс хранит `payload_size`;
- списки и search endpoints не читают payload без необходимости;
- Admin UI не встраивает raw JSON в обычный Result Details; полный payload доступен только по явному `Debug JSON` download через controlled endpoint.
- для одного `capability + content_hash` индекс указывает на один актуальный payload; повторное сохранение результата заменяет ссылку и удаляет прежний JSON payload этой подсистемы, чтобы не оставлять orphan rows.

Причина:

- отдельный gzip/deflate слой усложняет чтение, поиск и поддержку;
- сначала нужно увидеть фактический размер JSON текущего PDF ML-сервиса;
- PostgreSQL и storage-слой уже дают базовые механизмы хранения больших значений;
- модель `result_kind` и `result_artifacts` позволяет позже вынести крупный payload в файловое/S3-compatible storage.

Если фактические PDF JSON-результаты окажутся большими, после MVP можно добавить capability-level policy:

```text
store_jsonb
store_compressed_json_artifact
store_json_summary_plus_artifact
```

Для первого релиза это не входит в обязательный scope.

Текущий admin checkpoint:

```http
GET /api/admin/results?capability=&hash=&jobId=&limit=
GET /api/admin/results/{resultIndexId}
GET /api/admin/results/{resultIndexId}/payload
```

Admin Results возвращает только lightweight metadata из `processing_result_index` и связанную job metadata. Raw `payload_json` не возвращается в списке и обычных деталях результата, чтобы не тащить большие payload и не раскрывать лишние данные в операционном UI.

Для `pdf_stamp_recognition_results` обычный Result Details показывает безопасный summary: counts, page keys, optional `izm_number` и короткие excerpts ошибок без входного PDF, storage key и полного raw JSON.

Полный raw JSON доступен только через отдельный controlled debug endpoint `GET /api/admin/results/{resultIndexId}/payload`:

- endpoint требует admin session cookie и является read-only;
- CSRF не требуется, потому что состояние не меняется;
- table allowlist ограничен `pdf_stamp_recognition_results`;
- неподдержанные payload table возвращают `422 unsupported_payload_table`;
- payload больше `1 MiB` возвращает `413 payload_too_large`;
- ответ содержит metadata, warning и поле `payload` с исходным JSON.

Admin UI не встраивает raw JSON в Result Details. Для него есть отдельная явная кнопка `Debug JSON`, которая скачивает controlled debug payload.

Для будущего обработчика изображений:

```text
result_kind = json_and_files
payload table = image_processing_results
artifacts = processed image files
```

## Retention

В MVP результаты хранятся бессрочно как cache. Для одного hash хранится актуальный payload результата; исторические payload-версии по тому же `capability + content_hash` не удерживаются отдельной историей.

Но модель должна предусматривать будущую retention policy:

```text
keep_forever
delete_after_days
delete_after_last_access
manual_only
legal_hold
```

Для MVP:

```text
retention_policy = keep_forever
expires_at = null
```

Retention policy не реализуется как активное удаление в MVP. Модель должна хранить будущие поля политики, но экран управления retention и фоновые удаления результатов откладываются до появления реальных требований по срокам хранения, размеру базы или регламенту заказчика.

## Важное различие

Входной файл и результат — разные сущности.

```text
Входной файл:
  временный
  удаляется после обработки

Результат:
  cache
  хранится по hash
  может быть JSON или файл
```

## Диагностика внешних вызовов

Диагностика вызова processor-а не является результатом обработки.

Результат хранит бизнес-ответ capability по `capability + content_hash`. Диагностика хранит технический контекст конкретной attempt.

Для каждой attempt можно хранить:

```text
job_id
attempt_number
processor_key
processor_instance_id
endpoint
started_at
finished_at
duration_ms
http_status nullable
normalized_error_code nullable
retryable
timeout_kind nullable
request_size
response_content_type nullable
response_size nullable
raw_error_excerpt nullable
correlation_id
```

Полный входной файл, API key secret, credentials, большие request/response payload и полный successful result в diagnostics не пишутся.

`raw_error_excerpt` допускается только как обрезанный и очищенный фрагмент для диагностики ошибок. Базовый лимит MVP:

```text
max raw diagnostic excerpt = 16 KiB
```

На успешной обработке diagnostics хранит только техническую metadata: endpoint, duration, attempt, размер ответа, correlationId. Сам successful JSON-результат хранится в result payload конкретной подсистемы.

При `processor_bad_response` и `processor_contract_error` diagnostics может хранить обрезанный фрагмент ответа, чтобы администратор и поддержка видели, почему адаптер не смог разобрать контракт.

Support report может включать diagnostics attempt-а, но не включает входной файл, secrets и большие payload.
