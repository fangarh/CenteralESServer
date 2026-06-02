# Итоги code review remediation 2026-06-02

Связанные заметки:

- [[Текущая точка реализации 2026-06-02]]
- [[../05 Данные и хранение/Очередь задач PostgreSQL]]
- [[../01 Архитектура/Deployment - Web и Worker службы]]
- [[../04 Админка/Админка MVP]]
- [[../06 Эксплуатация/Retry и дедупликация]]

## Что сделано

Проведён полный code review на чистую архитектуру, границы модулей, безопасность данных и операционную устойчивость. После review выполнены три итерации исправлений.

### Итерация 1: clean architecture и state safety

- Усилены PostgreSQL state guards для `Complete`, `Defer`, `Fail`.
- `ClaimNext` теперь переводит `processing_subjects.state` в `processing`.
- Public/Worker/Admin зависимости сужены до role-specific ports.
- Admin Job Details перестал отдавать `temporaryFileKey` и raw diagnostics.
- Result store заменяет старый payload для того же hash без orphan JSON rows.
- Production auto-bootstrap выключен по умолчанию.

### Итерация 2: orchestration и admin read stores

- Public PDF upload orchestration вынесен в `SubmitPdfStampRecognitionJobHandler`.
- PostgreSQL Admin read stores физически разделены на jobs, processor, audit и results.
- `PostgresAdminProcessingReadStore` оставлен только composite facade.
- Добавлены Production config files для Web и Worker.
- На этой итерации `HttpClient` оставался DI-owned singleton; позднее, в Docker Compose baseline, HTTP recognizer переведён на `IHttpClientFactory`.

### Итерация 3: production migration path

- Добавлен standalone `CenteralES.DatabaseMigrator`.
- Migrator применяет embedded SQL migrations без EF.
- Поддерживает `--connection-string`, `--no-create-database`, `--help`.
- Production flow: сначала migrator, затем Web/Worker с `Database:AutoBootstrap=false`.

### Итерация 4: Worker stale-processing recovery

- Добавлен `IProcessingJobRecoveryQueue`.
- Worker периодически возвращает stale `processing` jobs в `queued`.
- Recovery использует current-subject guard и `FOR UPDATE SKIP LOCKED`.
- Новая attempt не создаётся, failed diagnostics не пишутся.
- Defaults: stale timeout 5 минут, interval 1 минута, batch 50.

## Проверки

Последний полный прогон:

```powershell
dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
```

Результат:

```text
Build: success
Unit tests: 74 passed
Integration tests: 61 passed
```

Дополнительно:

```powershell
dotnet run --project src/Apps/CenteralES.DatabaseMigrator/CenteralES.DatabaseMigrator.csproj -- --help
```

## Следующий шаг

Следующий крупный блок после этой заметки был выполнен:

```text
Docker Compose Delivery MVP
```

Итог зафиксирован в [[Итоги Docker Compose baseline 2026-06-02]]. Минимальный состав:

- PostgreSQL;
- migrator/init step;
- Web;
- Worker;
- shared temporary storage volume;
- env examples без секретов;
- smoke-инструкция.

## Ограничения

- Не использовать Entity Framework.
- Не сохранять секреты в Obsidian/Git.
- Public API и Admin API не смешивать.
- Admin diagnostics не запускают обычную обработку PDF.
- Raw payload и входные PDF не показываются в обычной админке/support report.
