# Админка MVP

## Назначение

Админка входит в MVP и должна быть качественным рабочим интерфейсом, а не временным CRUD.

Она нужна для:

- управления обработчиками в поставке;
- контроля состояния системы;
- просмотра очереди;
- просмотра ошибок;
- ручного retry;
- управления API keys;
- управления admin users;
- просмотра audit;
- контроля storage.

## Пользователь

Основной пользователь:

> Прикладной администратор.

Это означает:

- человек не обязан понимать внутреннюю архитектуру;
- UI должен использовать понятные термины;
- технические детали должны быть доступны, но не мешать;
- каждое предупреждение должно объяснять, что произошло и что можно сделать.

## Доступ

В первом релизе:

- админка не имеет внешнего публичного endpoint;
- доступ только во внутреннем контуре;
- одна роль: `admin`;
- login/password;
- session cookie;
- CSRF protection для Admin UI/Admin API;
- все действия пишутся в audit.

Текущий backend skeleton для доступа:

```http
POST /api/admin/auth/login
GET /api/admin/auth/me
POST /api/admin/auth/logout
```

`login` возвращает httpOnly session cookie и CSRF token для state-changing Admin API. `logout` требует валидную cookie-сессию и заголовок `X-CSRF-Token`.

## Логическая структура

```text
Dashboard
Поставка
Processors
Processor Details
Jobs
Job Details
Results
Result Details
API Keys
Storage
Health
Audit
Settings
Admin Users
```

## Dashboard

Первый экран должен отвечать:

- система работает?
- какие обработчики включены?
- есть ли проблемы?
- сколько задач в очереди?
- сколько задач обрабатывается?
- есть ли final failures?
- не заканчивается ли место во временном storage?
- есть ли ключи, требующие внимания?

Dashboard должен быть гибридным:

- в нормальном состоянии показывает состояние системы, доступные возможности, очередь, ключи и storage;
- при проблемах становится рабочей консолью: что случилось, насколько это важно, какое действие выполнить дальше.

Dashboard не должен быть декоративной страницей.

Он может показывать, например:

```text
12 задач требуют внимания
PDF обработчик не отвечает
Временное хранилище заполнено на 85%
2 ключа давно не использовались
```

Но опасные действия не выполняются прямо с Dashboard. Например, массовый retry ведёт в список задач с фильтрами, предпросмотром и подтверждением.

Текущий UI checkpoint:

```text
/admin
```

Первый UI shell обслуживается самим Web-приложением как статические assets без отдельного Node/Docker runtime. Экран поддерживает login, сводку, failed/blocked jobs с retry одной задачи, пассивный статус PDF-обработчика, управление API keys, Admin Users и audit list. Цвета должны оставаться высококонтрастными: основной текст тёмный на светлом фоне, без светло-серого текста на белом.

Текущий asset layout:

```text
src/Apps/CenteralES.Web/wwwroot/admin/index.html
src/Apps/CenteralES.Web/wwwroot/admin/app.js
src/Apps/CenteralES.Web/wwwroot/admin/formatters.js
src/Apps/CenteralES.Web/wwwroot/admin/dom.js
src/Apps/CenteralES.Web/wwwroot/admin/http.js
src/Apps/CenteralES.Web/wwwroot/admin/confirm-dialog.js
src/Apps/CenteralES.Web/wwwroot/admin/app.css
```

`app.js` держит состояние экранов, загрузчики данных и render/workflow-логику. Общие форматтеры, DOM helpers, HTTP client и confirm dialog вынесены в отдельные classic browser scripts без Node runtime и без сборщика.

## Основные сценарии администратора

Главные сценарии MVP:

```text
упавшие задачи
запуск retry
проверки работоспособности
управление API keys
поставка компонентов
```

Админка не занимается ручным вызовом бизнес-обработки.

Она не должна быть альтернативным клиентом PDF recognition или других capability. Исключение — диагностические проверки работоспособности, если конкретный processor явно поддерживает безопасный тест.

## Processors

Список возможностей и обработчиков.

Текущий processor status API:

```http
GET /api/admin/processors/pdf2txt-http-recognizer
```

Endpoint требует admin session cookie.

Endpoint показывает passive status для processor-а без активного вызова внешнего `/recognize_json/`: capability, processor key, health, queue counts, worker heartbeats, endpoint runtime distribution и последние diagnostics. Активные health/probe действия откладываются до подтверждения безопасного diagnostic сценария.

Текущий managed endpoints API:

```http
GET /api/admin/processors/pdf2txt-http-recognizer/endpoints?capability=pdf-stamp-recognition
POST /api/admin/processors/pdf2txt-http-recognizer/endpoints
PATCH /api/admin/processors/pdf2txt-http-recognizer/endpoints/{endpointId}
POST /api/admin/processors/pdf2txt-http-recognizer/endpoint-checks
```

`GET` требует admin session cookie и показывает общий список `source=env` + `source=db`, а также `effectiveEndpoints[]` для Worker snapshot. `POST` и `PATCH` требуют `X-CSRF-Token`, пишут audit и меняют только managed DB endpoint-ы. Env endpoint-ы остаются bootstrap/default fallback и напрямую из UI не редактируются.

`POST /endpoint-checks` — отдельный ручной diagnostic action для прикладного администратора. Endpoint требует admin session cookie и `X-CSRF-Token`, выбирает target только из уже настроенных `env/db` endpoint-ов, берёт PDF из `PdfStampRecognition:Diagnostics:SamplePdfPath`, отправляет его напрямую в выбранный внешний `/recognize_json/` и возвращает только безопасную диагностику: sanitized endpoint, status, HTTP status, duration, response size, normalized error и короткий excerpt. Он не создаёт processing job, не пишет result payload и не участвует в `/health/live`, `/health/ready` или realtime refresh. Если sample PDF не настроен или недоступен, результат `notConfigured`; внешний processor не вызывается. Частый запуск ограничивается cooldown `PdfStampRecognition:Diagnostics:EndpointCheckCooldown`.

Если worker heartbeats отсутствуют, processor health остаётся `unknown`. Если есть хотя бы один свежий worker heartbeat, skeleton возвращает `healthy`; если все worker heartbeat старше `3 minutes`, возвращает `unhealthy`.

Для `pdf2txt-http-recognizer` 2026-06-01 подтверждён безопасный диагностический сценарий `InvalidInputProbe`: отправка нулевого PDF возвращает `HTTP 200` с ожидаемыми validation errors. В Phase 1 этот факт только зафиксирован; кнопка активной проверки и audit такого действия остаются за пределами текущего skeleton.

Для каждого:

- название capability;
- processor;
- включён или выключен;
- health;
- последние ошибки;
- worker-ы и stale heartbeat;
- текущая очередь;
- лимит параллельности;
- быстрые действия.

## Processor Details

Карточка обработчика:

- описание понятным языком;
- endpoint или endpoint pool;
- enabled/disabled;
- health check;
- timeout;
- retry policy;
- pool concurrency limit;
- endpoint concurrency limit;
- состояние endpoint-ов: enabled, health, in-flight, last success, last error;
- last error;
- последние jobs;
- кнопки: сохранить, проверить, выключить, открыть очередь.

Runtime-параметры компонента управляются на странице компонента, рядом с состоянием, health, последними ошибками, связанными задачами и audit изменений.

Текущий UI checkpoint:

```text
/admin -> Обработчик
```

Первый экран Processor Details показывает passive health для `pdf2txt-http-recognizer`, queue counts, worker heartbeats, recent diagnostics и управление DB-managed endpoint-ами. Retry policy и глобальные pool settings остаются отдельными будущими checkpoint-ами.

Текущий endpoint-management checkpoint добавлен на тот же экран:

- Add endpoint;
- source `env/db`;
- enable/disable для DB-managed endpoint-а;
- изменение `concurrencyLimit`;
- отображение effective snapshot;
- runtime distribution остаётся пассивной: live/stale workers, in-flight, capacity, recent attempts/errors.
- realtime-панель с polling refresh каждые `2 seconds`, когда администратор явно включает `Realtime on`;
- endpoint runtime distribution показывает `activeProcessing`, utilization percent, avg/p95/max/last duration, completed/minute, recent attempts, failed/blocked/retryable counts и last HTTP/error.
- ручная кнопка `Проверить` для configured endpoint-а запускает только Admin diagnostic check с sample PDF, cooldown и подтверждением; результат показывается в строке endpoint-а и не создаёт обычную job.

UI и API не запускают health check через внешний `/recognize_json/`. Realtime refresh перечитывает только Admin Processor status. Изменения endpoint-ов влияют только на новые leases; уже взятые jobs не прерываются.

Опасные изменения требуют подтверждения:

```text
endpoint
endpoint pool
retry policy
pool concurrency limit
endpoint concurrency limit
выключение компонента
```

Подтверждение должно объяснять:

- что именно изменится;
- на какие новые задачи это повлияет;
- нужно ли выполнить health-check после изменения;
- будет ли действие записано в audit.

Для MVP не делаем универсальный редактор произвольных настроек. Форма настроек строится только по schema, которую processor definition объявил в коде.

## Jobs

Очередь и история задач.

Текущий skeleton read-only API:

```http
GET /api/admin/jobs?capability=&status=&hash=&limit=
GET /api/admin/jobs/{jobId}
```

Endpoint-ы требуют admin session cookie.

Эти endpoint-ы дают минимальную эксплуатационную видимость для Phase 1: статус, capability, hash, attempt number, timestamps, endpoint, normalized error, retryable и correlationId. Они не выполняют retry/cancel и не раскрывают входной PDF или secrets.

`GET /api/admin/jobs/{jobId}` также возвращает `attempts[]` по subject-у, чтобы Job Details мог показать цепочку попыток для одного hash/job flow. Обычный Job Details не возвращает `temporaryFileKey`; вместо storage key API отдает безопасный признак `inputRetained`.

Фильтры:

- status;
- capability;
- hash;
- date;
- processor;
- failed only.

Колонки:

- hash;
- capability;
- status;
- attempt;
- created;
- started;
- duration;
- action.

## Job Details

Детали задачи:

- hash;
- capability;
- текущий статус;
- все попытки;
- ошибки;
- retry policy;
- result link;
- кнопки manual retry/cancel, если разрешено.

По умолчанию страница показывает понятный статус, причину и доступные действия.

Технические поля должны быть в раскрываемом блоке `Технические детали`:

```text
jobId
correlationId
processor key
capability key
raw error
stack trace
heartbeat timestamp
```

Retry должен поддерживаться в двух формах:

```text
retry одной задачи
массовый retry по фильтру
```

Retry одной задачи запускается из `Job Details`.

Текущий backend skeleton:

```http
POST /api/admin/jobs/{jobId}/retry
```

Endpoint требует admin session cookie и `X-CSRF-Token`, создаёт новую queued attempt для текущей failed/blocked job и пишет audit event.

Текущий UI checkpoint:

```text
/admin -> Jobs -> Детали
```

Job Details в первом UI shell показывает идентификаторы, timestamps, diagnostics, признак удержания входного PDF без storage key, цепочку attempts, кнопку retry для failed/blocked задачи и скачивание support report JSON.

Массовый retry запускается из списка задач после фильтрации, например:

```text
capability = pdf-stamp-recognition
status = failed_final
date = today
```

Массовый retry должен иметь:

- предпросмотр списка задач;
- явное подтверждение;
- ограничение по правам;
- запись в audit;
- понятное сообщение о результате запуска.

## Results

`Results` входит в MVP как поиск и просмотр результатов, а не как аналитический раздел.

Поиск результата:

- по hash;
- по capability;
- по jobId;
- по дате;
- по статусу.

Для JSON результата:

- показать человекочитаемый summary;
- дать raw JSON в отдельном раскрываемом блоке.

Для файлового результата:

- показать metadata;
- дать скачать artifact, если это разрешено.

Текущий backend/UI checkpoint:

```http
GET /api/admin/results?capability=&hash=&jobId=&limit=
GET /api/admin/results/{resultIndexId}
GET /api/admin/results/{resultIndexId}/payload
```

```text
/admin -> Results
```

Первый экран `Results` показывает read-only result index metadata: resultIndexId, subjectId, jobId, capability, hash, result kind, payload table/id, contract version, payload size, createdAt и связанный job status/attempt. Endpoint-ы требуют admin session cookie, не требуют CSRF, не возвращают raw JSON payload, входной PDF или storage key. Result Details дополнительно показывает безопасный человекочитаемый summary для PDF-result.

## Result Details

Страница результата должна показывать:

- hash;
- capability;
- связанный job;
- статус результата;
- тип результата: JSON, файл, набор файлов, JSON плюс файлы;
- человекочитаемый summary;
- ссылку на payload или artifact metadata;
- технические детали в раскрываемом блоке.

Для PDF в MVP основной экран показывает найденных людей, должности и подписантов, если это можно извлечь из существующего JSON-контракта.

Raw JSON показывается отдельно и не должен быть главным экраном.

Текущий checkpoint:

```text
/admin -> Results -> Детали
```

Для `pdf_stamp_recognition_results` details endpoint возвращает безопасный summary, построенный из raw JSON payload на сервере:

- количество worker groups;
- количество распознанных scalar text items в `workers`;
- количество страниц в `workers_page`;
- список ключей страниц;
- количество `unrecognized_pages`;
- количество `errors`;
- `izm_number`, если он есть;
- короткие excerpts ошибок.

Summary не возвращает полный raw JSON payload, входной PDF, storage key, password/hash или другие секреты.

Полный raw JSON доступен только через отдельный controlled debug endpoint `GET /api/admin/results/{resultIndexId}/payload`:

- требует admin session cookie;
- read-only и не требует CSRF;
- работает только для allowlist table `pdf_stamp_recognition_results`;
- возвращает `422 unsupported_payload_table` для неподдержанных payload table;
- возвращает `413 payload_too_large`, если payload больше `1 MiB`;
- возвращает metadata (`resultIndexId`, `payloadTable`, `payloadId`, `contractVersion`, `payloadSize`), warning и поле `payload` с исходным JSON.

В обычном Result Details raw JSON не встраивается. UI даёт отдельную кнопку `Debug JSON` для скачивания controlled debug payload.

## Экспорт отчёта для поддержки

Экспорт отчёта входит в MVP.

Минимально экспорт доступен из `Job Details`.

Текущий backend skeleton:

```http
GET /api/admin/jobs/{jobId}/support-report
```

Endpoint требует admin session cookie, является read-only и не требует `X-CSRF-Token`. Он возвращает JSON-контекст для последующего UI/export: job/subject identifiers, capability, processor key, status, attempts, обрезанный diagnostics excerpt, passive processor/queue/worker snapshot, result index reference и audit events, связанные с attempts этого subject.

Если результат уже есть, `Result Details` может давать связанный экспорт контекста результата.

Отчёт должен включать:

```text
jobId
correlationId
capability
processor
normalized error code
retryable true/false
content hash
status
attempts
timestamps
error summary
retry history
health snapshot
result index reference
technical metadata
```

Для ошибок внешнего processor-а админка показывает нормализованный код, понятный текст, endpoint, HTTP status, retryable true/false, номер attempt, обрезанный excerpt в технических деталях и `correlationId`.

Raw diagnostic details показываются только как обрезанный и очищенный excerpt из attempt diagnostics. Полные response payload, входные файлы и secrets в админке не отображаются.

Текущий backend skeleton не возвращает `temporaryFileKey`, входной PDF и raw result payload в support report. `result` содержит только reference на `processing_result_index` и payload metadata.

Admin read surface разделён по ролям: jobs/support report, processor status, audit и results обслуживаются отдельными PostgreSQL read-store реализациями. Общий `IAdminProcessingReadStore` допускается только как совместимый composite facade и не должен смешивать Public API с Admin API.

Processor Details дополнительно показывает `staleProcessing` в queue counts. Это пассивная видимость job-level recovery риска: счётчик строится по `processing_jobs` с устаревшим job heartbeat и не вызывает внешний processor.

По умолчанию отчёт не включает:

- входной файл;
- большие payload;
- большие result artifacts.

Support report включает audit context только по связанному объекту:

- audit изменения processor-а, если оно могло повлиять на job;
- audit manual retry по этой job или subject;
- audit изменения API key не включается полностью, только `keyId/name`, если запрос связан с этим ключом;
- raw secrets никогда не попадают в report.

Для JSON-результата можно дать отдельную кнопку `Экспортировать raw JSON`, если права и политика доступа это разрешают.

Назначение отчёта — передать контекст в поддержку без необходимости читать БД или логи вручную.

## API Keys

Управление ключами клиента ЭП:

- список ключей;
- создать ключ;
- показать secret только один раз при создании или ротации;
- отключить ключ;
- rotate;
- allowed capabilities;
- last used;
- last used IP/User-Agent;
- expiresAt nullable;
- audit.

Текущий backend skeleton:

```http
GET /api/admin/api-keys?keyId=&active=&limit=
POST /api/admin/api-keys
POST /api/admin/api-keys/{keyId}/disable
```

`GET` требует admin session cookie. `POST` endpoint-ы требуют admin session cookie и `X-CSRF-Token`.

Создание ключа возвращает `secret` только один раз в ответе `POST /api/admin/api-keys`; список ключей и disable-response не возвращают secret или secret hash. `create_api_key` и `disable_api_key` пишутся в append-only audit без raw secret/hash.

Опасные действия с ключами требуют подтверждения:

```text
disable
rotate
изменить allowed capabilities
```

UI должен объяснять последствия для desktop-клиента ЭП.

Ротация в MVP выполняется вручную:

1. создать новый key;
2. прописать его в desktop-приложении;
3. проверить работу;
4. отключить старый key.

## Поставка компонентов

Экран `Поставка` входит в MVP как отдельный экран.

Он показывает не технический список классов, а карту возможностей текущей установки:

- какие capability входят в текущую установку;
- какие capability не входят в поставку;
- какие processor-ы доступны;
- какие processor instance включены runtime;
- какие endpoint-ы входят в pool внешнего сервиса;
- какие настройки доступны для изменения;
- какие компоненты требуют внимания.

Экран нужен, чтобы прикладной администратор мог быстро ответить:

```text
что вообще установлено
что включено сейчас
что выключено
что требует настройки
какие возможности недоступны в этой поставке
```

`Processors` остаётся рабочим экраном для состояния и управления конкретными обработчиками.

`Поставка` — обзор состава установки.

Текущий UI checkpoint:

```text
/admin -> Поставка
```

Первый экран `Поставка` показывает read-only состав MVP-установки: Web, Worker, PostgreSQL, processing schema, temporary storage, `pdf2txt-http-recognizer` и API keys. Там же зафиксированы локальные команды запуска Web/Worker через `dotnet run` без Docker. Docker Compose остаётся следующим delivery-шагом, Windows Service не входит в целевой путь MVP.

Администратор может управлять только тем, что уже включено разработчиком в поставку. Создание нового processor-а с нуля через UI не входит в MVP.

## Проверки работоспособности

Проверки делятся на три уровня:

```text
Basic health
Processor health
Optional diagnostic test
```

`Basic health` проверяет:

- Web;
- Worker;
- PostgreSQL;
- temporary storage;
- result storage;
- базовые фоновые процессы.

`Processor health` проверяет:

- доступность endpoint-а, контейнера или лицензированного сервиса;
- состояние endpoint pool-а, если processor использует несколько endpoint-ов;
- валидность runtime-настроек;
- наличие credentials/license refs, если они нужны;
- возможность безопасно обратиться к обработчику в режиме health.

Для внешних сервисов, которые не предоставляют отдельный health endpoint и не могут быть изменены, `Processor health` не должен имитировать обычную бизнес-обработку файла.

Базовый подход для таких чёрных ящиков:

- `config valid`: проверка endpoint, timeout, credentials и разрешённых runtime-параметров;
- `passive health`: статус по последним реальным вызовам, ошибкам, timeout-ам и времени последнего успеха;
- `manual diagnostic`: ручная проверка из админки, если processor definition явно описывает безопасный диагностический сценарий;
- `unknown` или `unreachable` для сервиса, который сейчас не отвечает или недоступен для проверки.

Проверка через отправку нулевого файла во внешний сервис рассматривается как возможный `InvalidInputProbe`, но пока не считается утверждённым контрактом. Её можно включать только после фактической проверки ответа сервиса и фиксации ожидаемого кода/тела ошибки в processor definition.

`Optional diagnostic test` допускается только если processor явно поддерживает безопасный тест. Такой тест не должен создавать обычную пользовательскую job и не должен подменять клиент ЭП.

## Storage

Контроль storage:

- размер temporary storage;
- количество временных файлов;
- stale files;
- cleanup status;
- размер result artifacts;
- retention policy read-only для MVP.

Текущий backend/UI checkpoint:

```http
GET /api/admin/storage
```

```text
/admin -> Storage
```

Первый экран `Storage` показывает read-only состояние temporary input storage: provider, purpose, root path, status, used bytes, soft/hard/min-free limits и доступное свободное место на volume. Экран не показывает список файлов, не отдаёт storage keys и не выполняет cleanup. Ручная очистка, retention policy и удаление файлов требуют отдельного действия с подтверждением и audit.

## Health

`Health` входит в MVP как отдельный экран.

Dashboard показывает summary и alerts.

`Health` показывает подробную картину:

```text
Web
Worker
PostgreSQL
Temporary storage
Result storage
Processor health
очередь
heartbeat
последняя проверка
```

Экран нужен для диагностики состояния системы без перехода в технические логи.

`Health` не должен запускать обычную бизнес-обработку файлов.

Текущий UI checkpoint:

```text
/admin -> Проверки
```

Первый экран Health показывает `/health/live` и `/health/ready`, включая checks `postgres`, `processingSchema` и `temporaryStorage`. Ready `503` отображается как проблемное состояние, а не как ошибка интерфейса. Экран не вызывает внешний `pdf2txt` и не отправляет PDF.

Минимальный состав экрана `Health` для MVP:

- Web: live, ready, version/build, database connectivity, schema compatibility;
- Worker: количество worker-ов, последний heartbeat, stale worker-ы, текущие processing jobs;
- PostgreSQL: доступность, latency последней проверки, ошибка подключения при наличии;
- Temporary storage: used percent, free space, soft/hard limit, возможность записи;
- Result storage: доступность чтения/записи;
- Queue: queued, processing, retry scheduled, failed/blocked;
- Processor health: состояние processor instance и endpoint pool без опасных активных вызовов;
- Последняя проверка: время, длительность, correlationId.

Статусы отображаются как:

```text
healthy
degraded
unhealthy
unknown
```

`unknown` допустим для внешних чёрных ящиков, если система не может безопасно проверить состояние без обычной обработки файла.

Кнопка `Проверить` на экране `Health` запускает только безопасные проверки:

- локальные проверки Web/Worker/PostgreSQL/storage;
- валидацию runtime-настроек processor-а;
- passive health refresh по последним событиям;
- optional diagnostic test только если processor definition явно описывает безопасный сценарий.

Для `pdf2txt-http-recognizer` без подтверждённого diagnostic probe кнопка `Проверить` не отправляет PDF или нулевой файл во внешний `/recognize_json/`.

## Audit

Текущий backend skeleton read-only API:

```http
GET /api/admin/audit?action=&targetType=&targetId=&actor=&occurredFrom=&occurredTo=&limit=
```

Endpoint требует admin session cookie, но не требует `X-CSRF-Token`, так как не меняет состояние.

Список audit возвращает только безопасные metadata-поля события: auditId, время, actor, action, target, comment и correlationId. `old_value_json`, `new_value_json` и `technical_metadata_json` не отдаются в общем списке, чтобы не раскрывать raw secrets и большие payload. Детализация значений может добавляться отдельным controlled endpoint-ом, если для конкретного action будет определен безопасный contract.

Журнал действий:

- кто выполнил действие;
- когда;
- что изменил;
- на какой объект повлияло;
- старое значение;
- новое значение;
- причина или комментарий, если действие требует объяснения;
- correlationId;
- IP/user agent, если применимо;
- технические детали в metadata.

Техническая metadata не должна быть главным содержанием строки audit.

Список audit показывает человеческое описание действия, а подробная metadata раскрывается в деталях.

Текущий UI checkpoint:

```text
/admin -> Аудит
```

Экран `Аудит` поддерживает фильтры по action, target type, target id, actor, диапазону дат и limit. Список показывает количество найденных событий, краткое описание примененного фильтра и раскрываемые безопасные детали: audit id, action, target, actor, occurred, correlationId и comment. Raw value JSON, credentials, hashes, входные файлы и большие payload не выводятся.

Audit обязателен для действий администратора, включая:

```text
изменение endpoint pool
изменение pool/endpoint concurrency limits
изменение timeout
изменение retry policy
включение/выключение processor-а
manual retry
массовый retry
создание API key
disable/rotate API key
изменение allowed capabilities
создание/отключение admin user
смена пароля admin user
cleanup временных файлов
изменение settings
```

Комментарий обязателен для:

```text
массовый retry
disable processor
изменение endpoint pool
изменение concurrency limits
изменение retry policy
disable API key
rotate API key
cleanup temporary files вручную
отключение admin user
```

Audit append-only: записи нельзя редактировать или удалять через UI. Raw API key secret, входные файлы и большие payload в audit не пишутся; значения секретов маскируются.

## Settings

Только общесистемные настройки.

Не превращать Settings в склад всего подряд. Настройки конкретного processor-а должны быть на странице processor-а.

В MVP `Settings` должен быть узким экраном:

- общие параметры системы;
- параметры, которые не принадлежат конкретному processor-у;
- read-only информация о режиме поставки, если её нельзя менять безопасно.

Текущий backend/UI checkpoint:

```http
GET /api/admin/settings
GET /api/admin/services
```

```text
/admin -> Settings
```

`Settings` показывает только безопасную read-only сводку runtime-настроек: лимит загрузки Public API, temporary storage root/limits, параметры `pdf2txt-http-recognizer`, sanitized endpoint pool, concurrency limits, timeout, retry attempts, overloaded delay и contract version. Экран не возвращает credentials, connection strings, env var names, пароли, hash-значения и не выполняет редактирование. Изменение настроек остается отдельным checkpoint с audit, подтверждением и валидацией влияния на новые задачи.

Settings и Storage также показывают read-only retention policy MVP:

- active cleanup выключен;
- dry-run cleanup пока недоступен;
- completed temporary input удаляется Worker lifecycle cleanup-ом;
- failed/blocked temporary input сохраняется для manual retry;
- result JSON payload и admin audit events хранятся бессрочно в текущем MVP;
- orphan temporary input пока не удаляется автоматически, будущий путь — dry-run после grace window и отдельное audited cleanup action.

Эта видимость не добавляет cleanup worker, ручную кнопку удаления или редактирование retention.

## Admin Users

`Admin Users` входит в MVP.

В первом релизе есть только роль `admin`, но экран нужен для базового управления доступом:

- создать администратора;
- отключить администратора;
- сменить пароль;
- посмотреть last login;
- посмотреть audit действий.

Текущий backend skeleton:

```http
GET /api/admin/users?login=&active=&limit=
POST /api/admin/users
POST /api/admin/users/{userId}/disable
POST /api/admin/users/{userId}/password
```

`GET` требует admin session cookie. `POST` endpoint-ы требуют admin session cookie и `X-CSRF-Token`.

Ответы Admin Users не возвращают пароль или password hash. `create_admin_user`, `disable_admin_user` и `change_admin_password` пишутся в append-only audit без пароля/hash. Отключение администратора делает его последующие login/session validation недействительными.

Пароль администратора:

- minimum length: 8 символов;
- хранится только hash-ом;
- предпочтительно `Argon2id`, допустимый fallback `bcrypt` или встроенный в .NET `PBKDF2-SHA256`.

Первый admin создаётся отдельным тестовым WinForms bootstrap-приложением:

```text
src/Apps/CenteralES.Admin.Bootstrap.WinForms
```

Приложение использует общий backend contract `IAdminBootstrapper` и PostgreSQL-реализацию `PostgresAdminBootstrapper`: применяет SQL migrations, проверяет количество активных администраторов и создаёт первого admin user только если активных администраторов ещё нет. Web setup wizard в MVP не делаем.

То же WinForms-приложение расширено как тестовый MVP client:

- вкладка `MVP сервисы` логинится в Admin API через `POST /api/admin/auth/login`;
- получает текущий список MVP-сервисов из `GET /api/admin/services`;
- для текущего MVP показывает `pdf-stamp-recognition / pdf2txt-http-recognizer`;
- тестирует `/health/live`, `/health/ready` и passive processor status через `GET /api/admin/processors/{processorKey}`;
- при указанном Public API key и PDF-файле дополнительно тестирует `POST /api/pdf-stamp-recognition/jobs` и polling `GET /api/pdf-stamp-recognition/results/{hash}`.

`GET /api/admin/services` является read-only registry endpoint-ом для зарегистрированных MVP-сервисов. В текущей поставке он возвращает один сервис `pdf-stamp-recognition / pdf2txt-http-recognizer`: capability, processorKey, displayName, description, enabled, recognizer, endpointCount, contractVersion, passive status source, admin status endpoint, settings endpoint, public endpoint paths и поддерживаемые test capability flags. Endpoint не возвращает secrets, connection strings, raw processor endpoint credentials и не вызывает внешний processor.

Bootstrap audit action:

```text
bootstrap_admin_user
```

Audit не содержит пароль, password hash, connection string или другие секреты. Повторный запуск после создания первого активного admin возвращает состояние `already initialized` и не перезаписывает существующего пользователя.

Сложные роли и права не входят в MVP, но модель должна позволять добавить их позже.

## Граница MVP

В MVP входят:

```text
Dashboard
Поставка
Processors
Processor Details
Jobs
Job Details
Results
Result Details
API Keys
Storage
Health
Audit
Admin Users
Settings
```

Из MVP откладываются:

```text
сложные роли кроме admin
создание processor с нуля через UI
редактор произвольных contracts
retention editing/cleanup UI для результатов
полноценная аналитика и графики
экспорт больших payload пакетами
глобальный DevOps mode
multi-tenant и организации
```
