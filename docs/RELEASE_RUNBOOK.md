# CenteralESServer MVP Release Runbook

Документ описывает минимальный релизный путь Docker Compose для реального `pdf2txt`.

## 1. Подготовить окружение

Создать production-like env-файл из шаблона:

```powershell
Copy-Item .env.production.example .env.production
notepad .env.production
```

Заполнить значения:

```text
CENTERALES_POSTGRES_PASSWORD=<strong password>
CENTERALES_PDF2TXT_ENDPOINT=https://<pdf2txt-host>/recognize_json/
CENTERALES_PDF2TXT_ENDPOINT_1=https://<second-pdf2txt-host>/recognize_json/
CENTERALES_PDF2TXT_PROXY_URL=
CENTERALES_PDF2TXT_DISABLE_ENVIRONMENT_PROXY=false
CENTERALES_PDF2TXT_DIAGNOSTIC_SAMPLE_PDF_PATH=
CENTERALES_PDF2TXT_DIAGNOSTIC_CHECK_COOLDOWN=00:05:00
CENTERALES_PDF2TXT_DIAGNOSTIC_CHECK_TIMEOUT=00:01:00
CENTERALES_WEB_PORT=8080
CENTERALES_ALLOWED_HOSTS=localhost;127.0.0.1
```

Файл `.env.production` не должен попадать в Git.

## 2. Проверить Compose-конфигурацию

Для релиза использовать base compose вместе с production override:

```powershell
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml config --quiet
```

Production override принудительно задаёт:

```text
PdfStampRecognition:Recognizer=Http
PdfStampRecognition:Processor:endpointPool:0=<CENTERALES_PDF2TXT_ENDPOINT>
PdfStampRecognition:Processor:endpointPool:1=<CENTERALES_PDF2TXT_ENDPOINT_1>
PdfStampRecognition:Processor:proxyUrl=<CENTERALES_PDF2TXT_PROXY_URL>
PdfStampRecognition:Processor:disableEnvironmentProxy=<CENTERALES_PDF2TXT_DISABLE_ENVIRONMENT_PROXY>
PdfStampRecognition:Diagnostics:SamplePdfPath=<CENTERALES_PDF2TXT_DIAGNOSTIC_SAMPLE_PDF_PATH>
```

Настройка применяется и к Web, и к Worker, чтобы Admin Settings/Services показывали тот же recognizer, которым реально пользуется Worker.
`CENTERALES_PDF2TXT_ENDPOINT` обязателен. `CENTERALES_PDF2TXT_ENDPOINT_1..3` опциональны; пустые значения игнорируются Worker-ом. При нескольких endpoint-ах Worker выбирает endpoint по least in-flight внутри configured pool, а Admin Processor Details показывает recent endpoint distribution по сохранённым attempt diagnostics.
Если среда запуска содержит нежелательные `HTTP_PROXY`/`HTTPS_PROXY`/`ALL_PROXY`, задайте `CENTERALES_PDF2TXT_DISABLE_ENVIRONMENT_PROXY=true`. Если нужен явный корпоративный proxy, задайте `CENTERALES_PDF2TXT_PROXY_URL`; не задавайте оба режима одновременно.

Admin Processor Details имеет ручную кнопку проверки endpoint-а. Она не участвует в health/readiness и не создаёт processing job. Чтобы включить её в production-like окружении, положите небольшой диагностический PDF внутрь доступного Web-контейнеру path и задайте `CENTERALES_PDF2TXT_DIAGNOSTIC_SAMPLE_PDF_PATH`. Если path пустой, кнопка вернёт `notConfigured`, внешний `pdf2txt` не будет вызван. Частота ограничивается `CENTERALES_PDF2TXT_DIAGNOSTIC_CHECK_COOLDOWN`.

## 3. Собрать и запустить

```powershell
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml build
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml up -d
```

Порядок старта:

```text
postgres -> migrator -> web + worker
```

Проверить состояние:

```powershell
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml ps
Invoke-WebRequest http://127.0.0.1:8080/health/live -UseBasicParsing
Invoke-WebRequest http://127.0.0.1:8080/health/ready -UseBasicParsing
```

Оба health endpoint-а должны вернуть `200`.

## 4. Создать первого admin

Первый admin создаётся отдельным WinForms bootstrap/test client:

```text
src/Apps/CenteralES.Admin.Bootstrap.WinForms
```

Bootstrap должен подключиться к PostgreSQL текущего Compose-окружения, применить миграции при необходимости и создать первого active admin только если admin-пользователей ещё нет.

Не выводить и не сохранять пароль admin в документации, issue, commit message или логах.

## 5. Создать Public API key

Войти в Admin UI:

```text
http://127.0.0.1:8080/admin
```

Создать API key с capability:

```text
pdf-stamp-recognition
```

Секрет API key показывается один раз. Сохранить его во внешнем секрет-хранилище. Не коммитить и не вставлять в документы.

## 6. Запустить release smoke

Smoke использует уже созданный Public API key и реальный PDF:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-release-smoke.ps1 `
  -EnvFile .env.production `
  -PdfPath .\test.pdf `
  -ApiKeyId "<key-id>" `
  -ApiKeySecret "<secret>"
```

Ожидаемый результат:

```text
live health OK
ready health OK
SMOKE_OK_RELEASE ... status=completed contract=pdf2txt-recognize-json-v1
```

Скрипт:

- запускает Compose с `compose.yaml + compose.prod.yaml`;
- проверяет `/health/live` и `/health/ready`;
- отправляет PDF через Public API;
- ждёт результат через Public result API;
- падает, если result source равен `fake-pdf2txt`;
- по умолчанию останавливает stack через `docker compose down`.

Для сохранения stack после smoke:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-release-smoke.ps1 `
  -EnvFile .env.production `
  -PdfPath .\test.pdf `
  -ApiKeyId "<key-id>" `
  -ApiKeySecret "<secret>" `
  -KeepRunning
```

## 7. Остановить или обновить

Остановить контейнеры без удаления данных:

```powershell
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml down
```

Удалить контейнеры и volumes:

```powershell
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml down -v
```

`down -v` удаляет PostgreSQL data volume и temporary storage volume. Не использовать для production-данных без явного backup.

## 8. Volumes

Compose создаёт:

```text
centerales-postgres-data
centerales-temporary-storage
```

PostgreSQL volume хранит:

- processing jobs;
- result index and payload rows;
- admin users and sessions;
- API keys hashes;
- audit events;
- schema migrations.

Temporary storage volume хранит входные PDF на время обработки. Успешно завершённые задачи очищают temporary input через Worker. Failed/blocked input может сохраняться для manual retry.

## 9. Backup минимум

Перед обновлением снять PostgreSQL dump:

```powershell
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml exec -T postgres `
  pg_dump -U centerales -d centerales -Fc > centerales-backup.dump
```

Восстановление в пустую БД:

```powershell
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml exec -T postgres `
  pg_restore -U centerales -d centerales --clean --if-exists < centerales-backup.dump
```

Проверить restore сначала на отдельном окружении.

## 10. Release gate

Перед тегом релиза выполнить:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
C:\Users\Admin\.dotnet\dotnet.exe test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
docker compose --env-file .env.production -f compose.yaml -f compose.prod.yaml config --quiet
powershell -ExecutionPolicy Bypass -File .\scripts\run-release-smoke.ps1 -EnvFile .env.production -PdfPath .\test.pdf -ApiKeyId "<key-id>" -ApiKeySecret "<secret>"
```

После успешного gate:

```powershell
git tag v0.1.0-mvp
```
