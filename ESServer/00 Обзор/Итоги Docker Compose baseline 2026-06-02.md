# Итоги Docker Compose baseline 2026-06-02

Эта заметка фиксирует checkpoint по Docker Compose Delivery MVP.

## Что добавлено

В корне репозитория добавлены:

```text
compose.yaml
compose.prod.yaml
.dockerignore
.env.example
.env.production.example
docker/Dockerfile.web
docker/Dockerfile.worker
docker/Dockerfile.migrator
scripts/run-release-smoke.ps1
docs/RELEASE_RUNBOOK.md
```

Compose-состав:

```text
postgres
migrator
web
worker
```

Порядок запуска:

```text
postgres -> migrator -> web + worker
```

`postgres` имеет `pg_isready` healthcheck. `migrator` зависит от healthy PostgreSQL и завершается как one-shot service. `web` и `worker` зависят от успешного завершения `migrator`.

## Runtime-конфигурация

Общий temporary storage volume:

```text
centerales-temporary-storage
```

PostgreSQL data volume:

```text
centerales-postgres-data
```

Web:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
Database:AutoBootstrap=false
Storage:TemporaryRoot=/var/lib/centerales/temporary-files
```

Worker:

```text
DOTNET_ENVIRONMENT=Production
Database:AutoBootstrap=false
Storage:TemporaryRoot=/var/lib/centerales/temporary-files
```

По умолчанию base `compose.yaml` использует:

```text
CENTERALES_PDF_RECOGNIZER=Fake
```

Это сделано, чтобы MVP delivery path мог подняться локально без внешнего `pdf2txt`. Для реального processor-а нужно задать:

```text
CENTERALES_PDF_RECOGNIZER=Http
CENTERALES_PDF2TXT_ENDPOINT=https://.../recognize_json/
```

Для production-like запуска используется override:

```text
compose.prod.yaml
```

Он принудительно задаёт `PdfStampRecognition:Recognizer=Http` и обязательный endpoint для Web и Worker. Это важно, чтобы Admin Settings/Services показывали тот же recognizer и endpoint pool, которые реально использует Worker.

## Проверки

Успешно выполнено без Docker:

```powershell
dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
```

Результат:

```text
74 unit tests passed
61 integration tests passed
```

CodeRabbit review по uncommitted Docker Compose baseline после исправлений:

```text
findings: 0
```

Также обновлён `/health/ready`: теперь schema check включает `processing_content_hashes`.

По результатам review также зафиксировано:

- compose больше не содержит fallback-пароль PostgreSQL и требует `CENTERALES_POSTGRES_PASSWORD`.
- Web/Worker/Migrator final Docker stages запускаются не от root.
- Worker использует `IHttpClientFactory` для HTTP recognizer-а.
- `PdfStampRecognition:Recognizer` теперь принимает только `Fake` или `Http`; неизвестное значение приводит к fail-fast при старте worker.

## Runtime smoke 2026-06-03

На машине с Docker Desktop выполнена реальная Compose-проверка без fake recognizer:

```text
docker compose -p centerales-real-smoke config --quiet
docker compose -p centerales-real-smoke build
docker compose -p centerales-real-smoke up -d
```

Локальная `.env` была ignored и задана в real-режиме:

```text
CENTERALES_PDF_RECOGNIZER=Http
CENTERALES_PDF2TXT_ENDPOINT=https://pdf2txt.selectel.dt1520.ru/recognize_json/
```

Результат:

```text
PostgreSQL: healthy
Migrator: completed successfully
Web /health/live: 200
Web /health/ready: 200
Worker: Resolved PdfStampRecognition:Recognizer as Http
Worker: POST https://pdf2txt.selectel.dt1520.ru/recognize_json/ -> HTTP 200
Public upload/polling with test.pdf: completed
Contract: pdf2txt-recognize-json-v1
```

Одноразовый Public API key был засеян только в локальный compose PostgreSQL; key/secret не фиксировались в документации и не выводились.

После smoke stack остановлен:

```text
docker compose -p centerales-real-smoke down
```

## Scripted release smoke 2026-06-03

После добавления production override и release smoke script выполнена повторяемая проверка:

```text
docker compose --env-file .env.production -p centerales-release-smoke -f compose.yaml -f compose.prod.yaml build
docker compose --env-file .env.production -p centerales-release-smoke -f compose.yaml -f compose.prod.yaml up -d
```

Отличие от первого runtime smoke:

- использовался `compose.prod.yaml`, который принудительно задаёт `Http` recognizer и endpoint для Web и Worker;
- admin был подготовлен из ignored `logon.env`;
- Public API key был создан через Admin API после login/session/CSRF, а не прямым insert в БД;
- `scripts/run-release-smoke.ps1` выполнил production-like проверку без вывода key/secret.

Результат:

```text
ADMIN_LOGIN=ok
API_KEY_CREATE=ok
SMOKE_OK_RELEASE
status=completed
contract=pdf2txt-recognize-json-v1
```

После scripted smoke stack был остановлен самим script-ом; контейнеров `centerales-release-smoke` не осталось.

## Следующий шаг

Для повторного real-запуска:

```text
Copy-Item .env.production.example .env.production
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml config --quiet
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml build
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml up -d
```

Повторяемый release smoke:

```text
powershell -ExecutionPolicy Bypass -File .\scripts\run-release-smoke.ps1 -EnvFile .env.production -PdfPath .\test.pdf -ApiKeyId "<key-id>" -ApiKeySecret "<secret>"
```

Операторский порядок поставки зафиксирован в `docs/RELEASE_RUNBOOK.md`.
