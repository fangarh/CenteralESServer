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
blocked/final failed -> удалить input file
cancelled -> удалить input file
abandoned -> удалить после grace period
```

Default grace period для abandoned:

```text
24 hours
```

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
