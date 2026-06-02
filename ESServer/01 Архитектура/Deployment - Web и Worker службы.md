# Deployment - Web и Worker службы

## Выбранный вариант

Выбран вариант B из обсуждения:

```text
Web отдельно, Workers отдельно
```

Это не тяжёлая микросервисная архитектура. Это один продукт, который поставляется несколькими процессами.

Для первого релиза выбран основной путь поставки:

```text
Docker Compose
single-node deployment
Web + один или несколько Worker
PostgreSQL
локальный shared storage volume
```

Docker полностью снимает необходимость делать отдельный Windows Service в MVP.

Windows Service и Linux daemon не входят в основной путь первого релиза. Если такая потребность появится у конкретной поставки, её можно рассматривать отдельно после MVP.

## Процессы

### CenteralES.Web

Отвечает за:

- Public REST API для desktop-клиента ЭП;
- Admin API;
- Admin UI;
- авторизацию;
- выпуск и управление API key;
- постановку задач;
- быстрый ответ клиенту;
- проверку статуса задач;
- выдачу результатов.

Web не должен выполнять тяжёлую обработку файлов.

### CenteralES.Worker

Отвечает за:

- выбор задач из очереди;
- вызов Processor-ов;
- контроль timeout;
- retry;
- heartbeat;
- сохранение результата;
- cleanup временных файлов;
- обработку зависших задач.

Worker может быть один или несколько.

## Минимальные health-check-и

В MVP health-check-и нужны для двух разных задач:

- Docker должен понимать, жив ли контейнер и можно ли его считать готовым;
- админка должна показывать прикладному администратору понятную картину состояния.

Эти задачи не смешиваются. Docker health-check возвращает короткий технический ответ без чувствительных деталей. Подробная диагностика доступна только в Admin UI/Admin API после входа администратора.

### Web health

`CenteralES.Web` предоставляет минимальные HTTP endpoint-ы:

```http
GET /health/live
GET /health/ready
```

`/health/live` проверяет только, что Web-процесс запущен и способен отвечать HTTP. Он не ходит в PostgreSQL, storage или внешние processor-ы.

`/health/ready` проверяет, что Web может принимать рабочие запросы:

- конфигурация загружена;
- PostgreSQL доступен с коротким timeout;
- версия схемы БД совместима с приложением;
- temporary storage доступен для записи;
- result storage доступен для чтения/записи;
- temporary storage не находится за hard limit;
- критические runtime-настройки можно прочитать.

`/health/ready` не вызывает внешние processor-ы и не запускает обработку файлов.

Текущий skeleton реализует минимальную версию readiness:

- PostgreSQL `select 1`;
- наличие обязательных processing/result/worker heartbeat таблиц;
- temporary storage write/read/delete probe.

Явная таблица версий схемы, result storage как отдельная категория и storage capacity thresholds остаются следующим расширением health surface.

### Worker health

`CenteralES.Worker` не публикуется как внешний HTTP-сервис. Для Docker health-check в MVP достаточно одного из двух технических вариантов:

```text
centerales worker healthcheck
```

или локальный management endpoint внутри контейнера:

```http
GET /health/live
```

Выбор конкретного механизма можно сделать при реализации. Архитектурное требование одно: health-check Worker не должен быть внешним публичным API и не должен запускать бизнес-обработку файлов.

Worker считается живым, если:

- процесс запущен;
- основной цикл worker-а не остановлен;
- heartbeat обновляется;
- PostgreSQL доступен;
- worker может читать runtime-настройки;
- worker имеет доступ к shared temporary storage;
- worker имеет доступ к result storage.

Worker пишет heartbeat в PostgreSQL каждые `30 seconds`. Если heartbeat не обновлялся дольше `3 minutes`, админка считает worker stale/unhealthy.

Текущий skeleton хранит heartbeat в таблице `processing_worker_heartbeats`:

```text
worker_id
processor_key
capability
started_at
heartbeat_at
updated_at
```

`CenteralES.Worker` генерирует runtime `worker_id` при старте процесса и обновляет запись для `pdf2txt-http-recognizer`. Admin processor status читает эти записи пассивно, без вызова внешнего processor-а.

### Health status

Для отображения в админке используем четыре состояния:

```text
healthy
degraded
unhealthy
unknown
```

`healthy` означает, что проверка прошла.

`degraded` означает, что система работает, но есть риск или ограничение: storage выше soft limit, часть endpoint-ов processor pool недоступна, есть stale worker при наличии других живых worker-ов, растёт очередь.

`unhealthy` означает, что компонент не может выполнять свою основную функцию: Web не может принимать новые задачи, PostgreSQL недоступен, storage за hard limit, все worker-ы stale, все endpoint-ы нужного processor-а недоступны или выключены.

`unknown` означает, что состояние нельзя достоверно определить без опасной или неподдерживаемой проверки. Для внешнего `pdf2txt` без health endpoint это допустимое состояние до появления реальных вызовов или безопасного diagnostic probe.

### Docker Compose baseline

Docker Compose baseline реализован в корне репозитория:

```text
compose.yaml
.env.example
docker/Dockerfile.web
docker/Dockerfile.worker
docker/Dockerfile.migrator
```

Минимальный порядок запуска:

```text
postgres -> migrator -> web + worker
```

`postgres` использует `pg_isready` healthcheck. `migrator` стартует только после healthy PostgreSQL и завершается как one-shot service. `web` и `worker` стартуют только после `migrator` с `service_completed_successfully`.

Для Web runtime readiness ориентируется на `/health/ready`.

Для Worker Docker health-check ориентируется на локальный worker healthcheck command или локальный management endpoint.

Admin UI не полагается только на Docker status. Источник истины для Worker в приложении — heartbeat и runtime-состояние в PostgreSQL.

Перед запуском Web/Worker production compose должен выполнить отдельный migration step:

```text
CenteralES.DatabaseMigrator
```

Этот step применяет embedded SQL migrations из `CenteralES.Infrastructure`, записывает `schema_migrations` и завершается до старта runtime-процессов. Web и Worker в Production должны запускаться с `Database:AutoBootstrap=false`, чтобы runtime не требовал `CREATE DATABASE`/DDL прав.

Локальный запуск MVP:

```powershell
copy .env.example .env
docker compose build
docker compose up
```

После запуска Web доступен на:

```text
http://localhost:8080
```

Готовность Web:

```text
GET http://localhost:8080/health/ready
```

По умолчанию compose использует `CENTERALES_PDF_RECOGNIZER=Fake`, чтобы delivery MVP поднимался без внешнего `pdf2txt`. Для реального processor-а нужно задать:

```text
CENTERALES_PDF_RECOGNIZER=Http
CENTERALES_PDF2TXT_ENDPOINT=https://.../recognize_json/
```

## Общая инфраструктура

```text
PostgreSQL
  jobs
  subjects
  result index
  processor runtime settings
  API keys
  users
  audit

Local / shared file storage
  temporary input files
  result artifacts, если результатом является файл
```

## Режимы поставки

### Single-node mode

Для первого релиза это основной и поддерживаемый вариант.

```text
одна машина
один Web
один или несколько Worker
локальный shared storage volume
PostgreSQL
Docker Compose
```

Плюсы:

- проще установка;
- проще эксплуатация;
- не нужен общий storage;
- не нужен Windows Service;
- достаточно для MVP.

Минусы:

- масштабирование ограничено одной машиной;
- worker должен иметь доступ к тем же временным файлам.

### Shared-volume mode

В рамках MVP допускается только как локальный shared volume на одной машине между Web и Worker контейнерами.

Multi-node shared-volume mode не входит в первый релиз.

```text
несколько процессов
общий volume для temp/result файлов
одна PostgreSQL
```

Плюсы:

- можно масштабировать worker-ы;
- storage остаётся похожим на локальную файловую систему.

Минусы:

- нужна аккуратная настройка общего volume;
- появляются вопросы блокировок, cleanup и доступности.

### S3-compatible mode

Не MVP, но архитектурно должно быть возможно.

```text
TemporaryFileStorage -> S3TemporaryFileStorage
ResultPayloadStorage -> S3ResultPayloadStorage
```

Плюсы:

- лучше для масштабирования;
- лучше для нескольких узлов;
- удобнее для больших файлов и артефактов.

Минусы:

- отдельная инфраструктура;
- сложнее поставка;
- не нужна для первого релиза.

## Почему не один процесс

Один Web Host с background service проще, но имеет риски:

- тяжёлые задачи конкурируют с API;
- падение или зависание обработки влияет на админку;
- сложнее масштабировать только обработку;
- сложнее перезапускать worker без влияния на пользователей.

## Почему не микросервисы

Полноценные микросервисы сейчас преждевременны:

- больше компонентов;
- больше сетевых контрактов;
- сложнее транзакции;
- сложнее локальная разработка;
- сложнее установка у заказчика.

## Масштабирование в MVP

В MVP поддерживаем масштабирование обработки только в пределах одной машины:

```text
1 Web container
N Worker containers
1 PostgreSQL
1 локальный shared storage volume
```

Несколько worker-ов безопасно забирают задачи через PostgreSQL queue, например через `FOR UPDATE SKIP LOCKED`.

Несколько машин с общим storage в MVP не поддерживаются.

Причина:

- multi-node сразу требует устойчивый общий storage;
- усложняется cleanup временных файлов;
- появляются дополнительные эксплуатационные риски;
- для первого релиза достаточно single-node режима.

## Рекомендуемая структура solution

Примерная структура:

```text
src/
  Apps/
    CenteralES.Web/
    CenteralES.Worker/
  Modules/
    Processing/
    AccessControl/
    Admin/
    Storage/
    PdfStampRecognition/
  Shared/
    CenteralES.Domain/
    CenteralES.Application/
    CenteralES.Infrastructure/
tests/
  Unit/
  Integration/
  Architecture/
```

Итоговая структура должна быть уточнена перед реализацией.
