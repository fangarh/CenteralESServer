# Итоги content hash algorithms 2026-06-02

Эта заметка фиксирует checkpoint после добавления выбираемых content hash algorithms и хранения hash aliases по всем доступным алгоритмам.

## Решение

Public PDF upload теперь поддерживает параметр `hashAlgorithm`.

Поддержанные значения:

- `sha256`;
- `gost-r-34.11-2012-256` / Стрибог-256.

Если параметр не указан, используется `sha256`.

При upload система считает все поддержанные hash-и одним проходом по stream:

```text
sha256:<hex>
gost-r-34.11-2012-256:<hex>
```

Выбранный алгоритм остаётся canonical `content_hash` для текущего `processing_subject`, `processing_jobs` и `processing_result_index`. Все остальные значения сохраняются как aliases в `processing_content_hashes`.

## Почему так

- Существующий публичный контракт и admin views продолжают видеть один canonical `hash`.
- Дедупликация и lookup могут работать по любому поддержанному prefixed hash.
- Смена default-алгоритма в будущем не обнуляет возможность найти старые/новые результаты.
- БД не требует ломать текущие `content_hash` поля; добавлена отдельная миграция alias-таблицы.

## Реализация

Ключевые изменения:

- `ContentHashAlgorithm`, `ContentHashValue`, `IContentHasher`, `ContentHasher`;
- `BouncyCastle.Cryptography` 2.6.2 для ГОСТ Р 34.11-2012 / Стрибог-256;
- `ComputeAllAsync` считает SHA-256 и Стрибог-256 одним чтением stream;
- `CreateProcessingJobCommand` принимает optional `ContentHashes`;
- `PostgresProcessingJobQueue` сохраняет aliases и ищет subject по canonical или alias hash;
- `PostgresPdfStampRecognitionResultStore` ищет result по canonical или alias hash;
- Admin Jobs/Results filters ищут по canonical или alias hash;
- миграция `0002_processing_content_hashes.sql` создаёт alias-таблицу и backfill-ит существующие canonical hash-и.

## Контракт

Upload:

```http
POST /api/pdf-stamp-recognition/jobs?hashAlgorithm=sha256
POST /api/pdf-stamp-recognition/jobs?hashAlgorithm=gost-r-34.11-2012-256
```

Также `hashAlgorithm` можно передать в multipart form.

Lookup:

```http
GET /api/pdf-stamp-recognition/results/{hash}
```

`{hash}` может быть любым сохранённым prefixed hash из поддержанных алгоритмов. Ответ возвращает canonical hash текущего subject/result.

## Проверки

Последний успешный прогон:

```powershell
dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
```

Результат:

```text
74 unit tests passed
61 integration tests passed
```

`git diff --check` ошибок не нашёл, только стандартные предупреждения Git о будущей замене LF на CRLF.

## CodeRabbit

CodeRabbit review по uncommitted diff был запущен после реализации.

Первый прогон нашёл 1 minor finding:

- `ContentHashAlgorithms.TryParse(null/empty)` не должен молча возвращать `sha256`.

Решение:

- `TryParse` сделан строгим и возвращает `false` для `null`/empty/whitespace;
- default `sha256` применяется явно только в Public endpoint, если параметр `hashAlgorithm` отсутствует;
- unit-тесты обновлены на строгий parsing.

Повторный прогон:

```powershell
coderabbit review --agent --type uncommitted
```

Результат:

```text
findings: 0
```

## Следующий шаг

Запустить локальный smoke без Docker.
