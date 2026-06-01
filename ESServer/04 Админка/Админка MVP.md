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

Текущий skeleton read-only API:

```http
GET /api/admin/processors/pdf2txt-http-recognizer
```

Endpoint показывает passive status для processor-а без активного вызова внешнего `/recognize_json/`: capability, processor key, health, queue counts, worker heartbeats и последние diagnostics. Активные health/probe действия откладываются до подтверждения безопасного diagnostic сценария.

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

Эти endpoint-ы дают минимальную эксплуатационную видимость для Phase 1: статус, capability, hash, attempt number, timestamps, endpoint, normalized error, retryable и correlationId. Они не выполняют retry/cancel и не раскрывают входной PDF или secrets.

`GET /api/admin/jobs/{jobId}` также возвращает `attempts[]` по subject-у, чтобы Job Details мог показать цепочку попыток для одного hash/job flow.

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

## Экспорт отчёта для поддержки

Экспорт отчёта входит в MVP.

Минимально экспорт доступен из `Job Details`.

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

Для ошибок внешнего processor-а админка показывает нормализованный код, понятный текст, endpoint, HTTP status, retryable true/false, номер attempt, raw error в технических деталях и `correlationId`.

Raw diagnostic details показываются только как обрезанный и очищенный excerpt из attempt diagnostics. Полные response payload, входные файлы и secrets в админке не отображаются.

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

Минимальный состав экрана `Health` для MVP:

- Web: live, ready, version/build, database connectivity, schema compatibility;
- Worker: количество worker-ов, последний heartbeat, stale worker-ы, текущие processing jobs;
- PostgreSQL: доступность, latency последней проверки, ошибка подключения при наличии;
- Temporary storage: used percent, free space, soft/hard limit, возможность записи;
- Result storage: доступность чтения/записи;
- Queue: queued, processing, retry scheduled, blocked/final failed;
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

## Admin Users

`Admin Users` входит в MVP.

В первом релизе есть только роль `admin`, но экран нужен для базового управления доступом:

- создать администратора;
- отключить администратора;
- сменить пароль;
- посмотреть last login;
- посмотреть audit действий.

Пароль администратора:

- minimum length: 8 символов;
- хранится только hash-ом;
- предпочтительно `Argon2id`, допустимый fallback `bcrypt`.

Первый admin создаётся через init command, например `centerales admin create`. Web setup wizard в MVP не делаем.

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
retention policy UI для результатов
полноценная аналитика и графики
экспорт больших payload пакетами
глобальный DevOps mode
multi-tenant и организации
```
