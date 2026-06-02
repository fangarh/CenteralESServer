# Файлы и Storage

## Виды файлов

В системе есть два разных вида файлов:

```text
Temporary input files
  входные файлы, нужные только до окончания обработки

Result artifacts
  выходные файлы, которые являются результатом обработки
```

Их нельзя смешивать.

## TemporaryFileStorage

Используется для входных файлов.

Например:

- PDF, который нужно распознать;
- изображение, которое нужно обработать;
- временный файл для внешнего сервиса.

Жизненный цикл:

```text
upload
  -> save temp file
  -> create job
  -> worker reads file
  -> processing completed/failed final/cancelled
  -> cleanup temp file
```

## ResultPayloadStorage

Используется для файлов, которые являются результатом.

Например:

- обработанное изображение;
- сформированный preview;
- преобразованный документ;
- набор артефактов.

Жизненный цикл результата отличается от временного файла.

В MVP результаты хранятся бессрочно как cache, если нет отдельной retention policy.

## MVP-реализация

Для MVP выбран локальный storage.

```text
LocalTemporaryFileStorage
LocalResultPayloadStorage
```

Почему:

- входные файлы не хранятся для истории;
- локальный storage проще;
- не нужен S3/MinIO в первом релизе;
- проще установка;
- проще отладка.

## Архитектурное требование

Нельзя завязывать домен на локальный диск.

Нужны интерфейсы:

```text
TemporaryFileStorage
  SaveAsync(stream)
  OpenReadAsync(fileRef)
  DeleteAsync(fileRef)
  CleanupExpiredAsync()

ResultPayloadStorage
  SaveAsync(stream)
  OpenReadAsync(payloadRef)
  DeleteAsync(payloadRef)
```

## Ограничения local storage

При local storage важно:

- Web и Worker должны иметь доступ к одному файлу;
- если процессов несколько, нужен общий путь или single-node mode;
- нельзя хранить файлы по имени пользователя;
- storage key должен быть безопасным и внутренним;
- нужна очистка stale files;
- нужно учитывать лимит диска.

Baseline MVP:

```text
soft limit: 80%
hard limit: 90%
minimum free space: 10 GiB
```

Текущий backend skeleton поддерживает byte-based limits через конфигурацию:

```text
Storage:TemporarySoftLimitBytes
Storage:TemporaryHardLimitBytes
Storage:TemporaryMinimumFreeBytes
```

Если `TemporaryHardLimitBytes` задан и текущий размер temporary storage плюс входящий файл превышает лимит, `POST /api/pdf-stamp-recognition/jobs` возвращает `503 temporary_storage_full` до сохранения файла. Если `TemporaryMinimumFreeBytes` задан, upload также блокируется, когда после входящего файла свободного места будет меньше настроенного минимума.

`TemporarySoftLimitBytes` влияет на capacity status как warning. Admin Storage screen показывает текущий статус, used bytes, soft/hard/min-free limits и операционный риск блокировки новых upload-ов.

Поведение:

- при soft limit админка показывает предупреждение;
- при hard limit Web перестаёт принимать новые файлы;
- `POST /jobs` возвращает `503 Service Unavailable` с кодом `temporary_storage_full`;
- Worker не берёт новые job, которым нужен новый temporary file;
- cleanup запускается или усиливается;
- уже идущие job не прерываются без крайней необходимости.

Cleanup temporary input files:

```text
completed -> удалить input file
blocked/final failed -> сохранить input file для manual retry
cancelled -> удалить input file
abandoned -> future dry-run candidate после grace period
```

Default grace period для abandoned:

```text
24 hours
```

## Retention policy MVP

Текущий checkpoint фиксирует read-only retention policy без активного фонового удаления:

- `temporary-input-active`: временный PDF хранится, пока job находится в `queued` или `processing`.
- `temporary-input-completed`: временный PDF удаляется Worker-ом после успешного `completed`, когда состояние и result index уже сохранены.
- `temporary-input-failed-blocked`: временный PDF сохраняется после `failed`/`blocked`, чтобы manual retry мог переиспользовать input.
- `result-json-payload`: JSON payload результата хранится бессрочно в текущем MVP как cache и diagnostic source.
- `admin-audit-events`: audit events append-only и хранятся бессрочно в текущем MVP.
- `orphan-temporary-input`: активного sweep-а нет; будущий safe-путь — сначала dry-run кандидатов после grace window `24 hours`, затем отдельное audited cleanup action.

`GET /api/admin/storage` и `GET /api/admin/settings` показывают эту политику как read-only metadata:

- `activeCleanupEnabled = false`;
- `dryRunAvailable = false`;
- правила retention;
- явную границу MVP.

Этот checkpoint не добавляет удаление файлов, удаление payload, cleanup worker, ручную кнопку cleanup или редактирование retention. Такие действия требуют отдельного backend/UI checkpoint с confirmation, audit и проверкой влияния на manual retry.

## S3-compatible storage

Не MVP, но должно быть предусмотрено.

Будущие реализации:

```text
S3TemporaryFileStorage
S3ResultPayloadStorage
```

Когда понадобится:

- несколько серверов;
- Kubernetes;
- большие объёмы файлов;
- необходимость горизонтального масштабирования;
- отдельное долговременное хранение result artifacts.

## Что показывает админка

В MVP админка должна показывать:

- размер temporary storage;
- количество временных файлов;
- stale files;
- статус cleanup;
- размер result artifacts;
- предупреждение при приближении к лимиту.
