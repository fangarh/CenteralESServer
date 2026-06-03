# Итоги Docker Compose baseline 2026-06-02

Эта заметка фиксирует checkpoint по Docker Compose Delivery MVP.

## Что добавлено

В корне репозитория добавлены:

```text
compose.yaml
.dockerignore
.env.example
docker/Dockerfile.web
docker/Dockerfile.worker
docker/Dockerfile.migrator
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

По умолчанию compose использует:

```text
CENTERALES_PDF_RECOGNIZER=Fake
```

Это сделано, чтобы MVP delivery path мог подняться локально без внешнего `pdf2txt`. Для реального processor-а нужно задать:

```text
CENTERALES_PDF_RECOGNIZER=Http
CENTERALES_PDF2TXT_ENDPOINT=https://.../recognize_json/
```

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

## Следующий шаг

Для повторного real-запуска:

```text
copy .env.example .env
set CENTERALES_PDF_RECOGNIZER=Http
set CENTERALES_PDF2TXT_ENDPOINT=https://.../recognize_json/
docker compose config
docker compose build
docker compose up
```
