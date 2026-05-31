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
