# Handoff после code audit refactor 2026-06-02

Эта заметка фиксирует состояние после полного анализа исходников и первого пакета исправлений по найденным слабым местам. Нужна для продолжения после очистки контекста.

## Репозиторий

```text
D:\Projects\DT1520\CenteralESServer
branch: main
```

Рабочая копия сейчас не чистая. Это ожидаемо: изменения внесены, проверены build/test, но коммит еще не сделан.

## Что было найдено на аудите

Основные слабые места:

- Manual retry мог создавать новую queued job после удаления temporary PDF. Worker затем падал бы на чтении старого `temporary_file_key`.
- `POST /api/pdf-stamp-recognition/jobs` проверял размер файла после `ReadFormAsync`, то есть слишком большой multipart мог быть разобран до бизнес-лимита.
- Повторный upload того же active hash сначала писал temporary file, а dedup происходил только в `PostgresProcessingJobQueue.EnqueueAsync`.
- Bootstrap Web/Worker дублировал resolver-логику connection string и temporary storage root.
- SQL schema была только `create table if not exists`, без точки учета версии схемы.
- `PostgresProcessingJobQueue`, `PostgresAdminProcessingReadStore`, `WebApiContractTests` и `wwwroot/admin/app.js` уже стали крупными файлами накопления сложности.

## Что исправлено

### Temporary input retention

Добавлена политика:

```text
src/Modules/Processing/ProcessingInputRetentionPolicy.cs
```

Поведение:

- completed job удаляет temporary input;
- failed/blocked/cancelled terminal states сохраняют temporary input для operator/manual retry.

`WorkerJobProcessor` больше не удаляет temporary PDF после final failed/blocked job. Это закрывает дефект manual retry после cleanup.

Покрытие:

```text
tests/Unit/ProcessingInputRetentionPolicyTests.cs
tests/Unit/WorkerJobProcessorTests.cs
```

### Public upload pipeline

В `src/Apps/CenteralES.Web/PublicPdfEndpoints.cs` порядок изменен:

1. authorize API key;
2. parse multipart с request-size guard;
3. validate file;
4. compute hash;
5. check existing result;
6. check active current job by hash;
7. только потом check temporary storage capacity;
8. save temporary file;
9. enqueue.

Теперь duplicate active upload возвращает existing job до записи в temporary storage.

Покрытие:

```text
tests/Integration/WebApiContractTests.cs
Duplicate_active_pdf_job_returns_existing_job_before_temporary_storage_write
```

### Request size guard

Для upload endpoint добавлена endpoint metadata:

```text
PdfUploadRequestSizeLimitMetadata : IRequestSizeLimitMetadata
```

Также `ReadFormAsync` оборачивается в обработку:

- `BadHttpRequestException` со статусом 413;
- `InvalidDataException`.

Оба случая возвращают безопасный JSON:

```json
{
  "error": {
    "code": "payload_too_large"
  }
}
```

### Bootstrap resolver cleanup

Добавлены общие resolver-классы:

```text
src/Shared/CenteralES.Infrastructure/Postgres/PostgresDatabaseConnectionStringResolver.cs
src/Modules/Storage/TemporaryStorageRootResolver.cs
```

`Program.cs` в Web и Worker теперь использует их вместо локальных дублирующихся static methods.

### Schema migration baseline

Добавлена минимальная точка учета схемы:

```sql
create table if not exists schema_migrations (
    id text primary key,
    applied_at timestamptz not null
);
```

Bootstrap после применения schema пишет marker:

```text
0001_processing_baseline
```

Это еще не полноценный migration runner, но теперь есть совместимая точка расширения под будущие явные SQL-миграции без EF.

Покрытие:

```text
tests/Unit/PostgresProcessingSqlTests.cs
```

## Измененные файлы

```text
src/Apps/CenteralES.Web/Program.cs
src/Apps/CenteralES.Web/PublicPdfEndpoints.cs
src/Apps/CenteralES.Worker/Program.cs
src/Apps/CenteralES.Worker/WorkerJobProcessor.cs
src/Shared/CenteralES.Infrastructure/Postgres/PostgresDatabaseBootstrapper.cs
src/Shared/CenteralES.Infrastructure/Processing/PostgresProcessingSql.cs
tests/Integration/WebApiContractTests.cs
tests/Unit/PostgresProcessingSqlTests.cs
tests/Unit/WorkerJobProcessorTests.cs
src/Modules/Processing/ProcessingInputRetentionPolicy.cs
src/Modules/Storage/TemporaryStorageRootResolver.cs
src/Shared/CenteralES.Infrastructure/Postgres/PostgresDatabaseConnectionStringResolver.cs
tests/Unit/ProcessingInputRetentionPolicyTests.cs
```

## Проверки

Проектный SDK из `AGENTS.md` не найден в текущем окружении:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
```

Ошибка:

```text
The term 'C:\Users\Admin\.dotnet\dotnet.exe' is not recognized
```

Проверки через доступный системный `dotnet` прошли:

```powershell
dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
```

Результат:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

```powershell
dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
```

Результат:

```text
Unit: 45/45 passed
Integration: 45/45 passed
```

## Важные ограничения

- Не выводить секреты из `db.env` и `logon.env`.
- `db.env`, `logon.env`, `.codex-local/`, `test.pdf` должны оставаться ignored.
- Не использовать Entity Framework.
- Public API и Admin API не смешивать.
- Health/admin diagnostic не должны запускать обычную бизнес-обработку файла во внешнем processor-е.
- Нельзя откатывать текущие незакоммиченные изменения без явного запроса пользователя.

## Что делать дальше

Ближайшие практичные шаги:

1. Сделать финальный review текущего diff.
2. Запустить проверки проектным SDK, если путь `C:\Users\Admin\.dotnet\dotnet.exe` появится или будет исправлен.
3. Закоммитить текущий пакет исправлений.
4. Следующий отдельный refactor checkpoint: вынести SQL migrations в файлы и сделать простой migration runner.
5. После этого: разбирать `wwwroot/admin/app.js` на модули или переходить к Docker Compose Delivery MVP.

