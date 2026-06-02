# Стартовый промпт после очистки контекста

Скопируй этот текст в новую сессию Codex:

```text
Продолжаем проект CenteralESServer.

Рабочая папка:
D:\Projects\DT1520\CenteralESServer

Отвечай по-русски.

Обязательные правила:
- Не выводи секреты из db.env и logon.env.
- db.env, logon.env, .codex-local/ и test.pdf должны оставаться ignored.
- Не используй Entity Framework. Только явный SQL/Npgsql.
- Public API и Admin API не смешивать.
- Health/admin diagnostic не должны запускать обычную бизнес-обработку файла во внешнем processor-е.
- Перед изменениями читай существующие паттерны в src, tests, ESServer, .planning.
- Не откатывай незакоммиченные изменения без явного запроса пользователя.

Текущая важная точка:
В предыдущей сессии был проведен полный code audit и внесен первый пакет исправлений. Рабочая копия ожидаемо dirty, изменения уже прошли build/test через системный dotnet, но еще не закоммичены.

Сначала выполни:
1. git status --short
2. git diff --stat
3. Прочитай:
   - AGENTS.md
   - ESServer/00 Обзор/Handoff после code audit refactor 2026-06-02.md
   - docs/START_PROMPT_AFTER_CONTEXT_CLEAR.md

Ожидаемые измененные файлы:
- src/Apps/CenteralES.Web/Program.cs
- src/Apps/CenteralES.Web/PublicPdfEndpoints.cs
- src/Apps/CenteralES.Worker/Program.cs
- src/Apps/CenteralES.Worker/WorkerJobProcessor.cs
- src/Shared/CenteralES.Infrastructure/Postgres/PostgresDatabaseBootstrapper.cs
- src/Shared/CenteralES.Infrastructure/Processing/PostgresProcessingSql.cs
- tests/Integration/WebApiContractTests.cs
- tests/Unit/PostgresProcessingSqlTests.cs
- tests/Unit/WorkerJobProcessorTests.cs
- src/Modules/Processing/ProcessingInputRetentionPolicy.cs
- src/Modules/Storage/TemporaryStorageRootResolver.cs
- src/Shared/CenteralES.Infrastructure/Postgres/PostgresDatabaseConnectionStringResolver.cs
- tests/Unit/ProcessingInputRetentionPolicyTests.cs

Что уже исправлено:
- Worker больше не удаляет temporary PDF после final failed/blocked job, чтобы manual retry мог переиспользовать input.
- Добавлена ProcessingInputRetentionPolicy.
- Public upload pipeline теперь проверяет existing result и active job by hash до записи temporary file.
- Duplicate active upload возвращает existing job до temporary storage write.
- Upload endpoint получил request-size metadata и безопасный 413 payload_too_large при ошибке разбора multipart.
- Общий resolver connection string вынесен в PostgresDatabaseConnectionStringResolver.
- Общий resolver temporary storage root вынесен в TemporaryStorageRootResolver.
- Добавлена таблица schema_migrations и marker 0001_processing_baseline.
- Добавлены/обновлены unit и integration tests.

Последние проверки:
- C:\Users\Admin\.dotnet\dotnet.exe build ... не запустился, потому что этот SDK path отсутствовал в текущем окружении.
- dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal прошел успешно.
- dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal прошел успешно:
  - Unit: 45/45 passed
  - Integration: 45/45 passed

Следующая задача:
1. Быстро перепроверь текущий diff.
2. Если все нормально, сделай commit с сообщением вроде:
   Fix processing input retention and upload dedup flow
3. После commit можно планировать следующий checkpoint:
   - полноценный SQL migration runner без EF;
   - или разбиение wwwroot/admin/app.js;
   - или Docker Compose Delivery MVP.
```

