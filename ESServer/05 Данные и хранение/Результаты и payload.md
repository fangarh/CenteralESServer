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

Для будущего обработчика изображений:

```text
result_kind = json_and_files
payload table = image_processing_results
artifacts = processed image files
```

## Retention

В MVP результаты хранятся бессрочно как cache.

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
