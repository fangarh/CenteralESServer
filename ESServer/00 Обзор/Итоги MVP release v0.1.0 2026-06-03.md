# Итоги MVP release v0.1.0 2026-06-03

Эта заметка фиксирует релизный checkpoint `v0.1.0-mvp`.

## Git checkpoint

```text
tag: v0.1.0-mvp
commit: e557142 Update Obsidian MVP release notes
```

Аннотированный тег создан локально после прохождения release gate и финальной синхронизации Obsidian release notes.

## Что входит в MVP

Рабочий delivery path:

```text
Docker Compose -> PostgreSQL -> DatabaseMigrator -> Web -> Worker -> real pdf2txt -> result/status API
```

Основной сценарий:

```text
Public PDF upload -> PostgreSQL queue -> Worker -> external /recognize_json/ -> result store -> Public result polling
```

Ключевые элементы:

- Web API на ASP.NET Core Minimal API;
- Worker как отдельный процесс;
- PostgreSQL queue без Entity Framework;
- explicit SQL migrations через `CenteralES.DatabaseMigrator`;
- shared temporary storage volume;
- API key auth для Public API;
- Admin session + CSRF для Admin API/UI;
- Admin UI для jobs, processor status, health, storage, settings, audit, users, API keys, results;
- WinForms bootstrap/test client для первого admin и сервисных проверок;
- Docker Compose production-like override для real `pdf2txt`.

## Release Compose

Base Compose:

```text
compose.yaml
```

Production-like override:

```text
compose.prod.yaml
```

Production env template:

```text
.env.production.example
```

Реальный `.env.production` ignored и не должен попадать в Git.

`compose.prod.yaml` принудительно задаёт:

```text
PdfStampRecognition:Recognizer=Http
PdfStampRecognition:Processor:endpointPool:0=<CENTERALES_PDF2TXT_ENDPOINT>
```

Настройка применяется и к Web, и к Worker, чтобы Admin Settings/Services показывали тот же recognizer, который реально используется обработкой.

## Release smoke

Повторяемый smoke script:

```text
scripts/run-release-smoke.ps1
```

Проверенный сценарий 2026-06-03:

```text
ADMIN_LOGIN=ok
API_KEY_CREATE=ok
SMOKE_OK_RELEASE
status=completed
contract=pdf2txt-recognize-json-v1
```

Важно:

- Public API key был создан через Admin API с session/CSRF, а не прямым insert в БД;
- secret key не выводился и не фиксировался в документации;
- обработка шла через real `HttpPdfStampRecognizer`, не через `FakePdfStampRecognizer`;
- release-smoke Compose stack был остановлен, контейнеров не осталось.

## Verification

Перед тегом выполнено:

```text
docker compose --env-file .env.production.example -f compose.yaml -f compose.prod.yaml config --quiet
docker compose --env-file .env.production.example -p centerales-release-verify -f compose.yaml -f compose.prod.yaml build
C:\Users\Admin\.dotnet\dotnet.exe build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
C:\Users\Admin\.dotnet\dotnet.exe test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
scripts/run-release-smoke.ps1 with Admin API-created Public API key
```

Результаты:

```text
Docker Compose config/build: passed
dotnet build: passed with known obj-cache warnings only
dotnet test: 75 unit, 63 integration passed
scripted release smoke: passed
```

## Операторские документы

```text
docs/RELEASE_RUNBOOK.md
docs/START_PROMPT_OFFICE.md
```

Runbook покрывает:

- `.env.production` setup;
- first admin bootstrap;
- Public API key creation;
- release smoke;
- volume lifecycle;
- PostgreSQL backup/restore minimum;
- release gate.

## Осталось после MVP

Не блокирует `v0.1.0-mvp`:

- refactor `PostgresProcessingJobQueue`;
- дальнейшая полировка Admin MVP;
- retention cleanup actions;
- production hardening вокруг backup/restore automation;
- публикация GitHub release notes.

## Правила, которые остаются обязательными

- Не использовать Entity Framework.
- Не смешивать Public API и Admin API.
- Не выводить secrets из `db.env`, `.env.production`, `logon.env`.
- `db.env`, `.env`, `.env.production`, `.codex-local/`, `logon.env`, `test.pdf` должны оставаться ignored.
- Health/admin diagnostics не запускают обычную обработку файла во внешнем processor-е.
