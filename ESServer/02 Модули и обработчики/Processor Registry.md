# Processor Registry

## Что такое Processor Registry

`Processor Registry` — это каталог возможностей и обработчиков, доступных в текущей версии системы и текущей поставке.

Он отвечает на вопросы:

- какие capability известны системе;
- какие processor-ы могут выполнять эти capability;
- какие processor instance включены в этой поставке;
- какие настройки доступны администратору;
- какой processor выбрать для новой job;
- можно ли сейчас запускать обработку.

## Что Registry не делает

Registry не выполняет тяжёлую обработку.

Он не должен:

- сам вызывать ML-сервис;
- сам читать PDF;
- сам хранить результат;
- сам управлять UI.

Он даёт платформе информацию, какой обработчик доступен и как с ним работать.

## Слои Registry

### Capability Definition

Описывает бизнес-возможность.

Пример:

```text
key: pdf-stamp-recognition
displayName: Распознавание штампа PDF
description: Извлекает авторов, должности и подписантов из штампа чертежа
```

### Processor Definition

Описывает реализацию, заданную разработчиком.

Пример:

```text
key: pdf2txt-http-recognizer
capability: pdf-stamp-recognition
type: ExternalHttpProcessor
adapter: Pdf2TxtHttpProcessorAdapter
resultStore: PdfStampRecognitionResultStore
settingsSchema: endpointPool, timeout, maxAttempts, poolConcurrencyLimit, endpointConcurrencyLimit, proxyUrl, disableEnvironmentProxy, diagnostics.samplePdfPath, diagnostics.endpointCheckCooldown
```

В текущем кодовом skeleton adapter class называется `HttpPdfStampRecognizer`. Для `pdf2txt-http-recognizer` уже есть первый DB-backed слой управления endpoint-ами: таблица `processor_endpoints`, Admin API для добавления/включения/выключения/изменения per-endpoint concurrency и Worker refresh snapshot без рестарта сервиса.

### Processor Instance

Описывает runtime-настройку в конкретной поставке.

Пример:

```text
processorKey: pdf2txt-http-recognizer
enabled: true
endpointPool:
  - https://pdf2txt-1.local/recognize_json/
  - https://pdf2txt-2.local/recognize_json/
  - https://pdf2txt-3.local/recognize_json/
timeout: 10 minutes
poolConcurrencyLimit: 6
endpointConcurrencyLimit: 2
proxyUrl: null
disableEnvironmentProxy: false
maxAttempts: 5
afterMaxAttempts: admin_required
```

Если внешний сервис развёрнут несколькими одинаковыми Docker-контейнерами, это не создаёт несколько разных processor-ов. Это один processor instance с несколькими endpoint-ами в pool.

Env endpoint-ы остаются bootstrap/default fallback. Они не записываются автоматически в БД, но Admin API показывает общий список `source=env` и `source=db`, а Worker строит effective snapshot из env + DB. DB-запись с тем же normalized endpoint имеет приоритет над env fallback; если такая DB-запись disabled, этот endpoint не попадает в effective snapshot для новых jobs.

HTTP transport для внешнего processor-а настраивается отдельно от endpoint pool. По умолчанию Worker использует стандартный .NET HTTP proxy discovery из окружения/OS. Если нужно зафиксировать корпоративный proxy, задаётся `PdfStampRecognition:Processor:proxyUrl`. Если локальный запуск не должен наследовать `HTTP_PROXY`/`HTTPS_PROXY`/`ALL_PROXY` из окружения, задаётся `PdfStampRecognition:Processor:disableEnvironmentProxy=true`. `proxyUrl` и `disableEnvironmentProxy` взаимоисключающие.

## Где что хранится

В коде:

- capability key;
- processor key;
- тип processor-а;
- adapter class;
- result store;
- допустимая schema настроек;
- default limits;
- input/output contract metadata.

В БД:

- enabled/disabled;
- управляемые endpoint-ы в `processor_endpoints`;
- timeout;
- HTTP transport: `proxyUrl` или `disableEnvironmentProxy`;
- retry policy;
- общий concurrency limit pool-а;
- per-endpoint concurrency limit;
- credentials/license refs;
- health status;
- last error;
- timestamps.

## Загрузка при старте

При старте Web и Worker:

1. Загружаются Processor Definitions из кода.
2. Считываются Processor Instances из БД.
3. Если instance отсутствует, создаётся запись с default settings.
4. Если definition изменился, админке показывается предупреждение.
5. Registry становится доступен use case-ам.

Текущий MVP не создаёт Processor Instance целиком из БД. Worker стартует с env endpoint pool и затем каждые `15 seconds` перечитывает DB-managed endpoint-ы. Если БД временно недоступна, Worker продолжает использовать последний валидный snapshot; если snapshot ещё не загружался, остаётся env fallback.

## Использование при обработке

Поток:

```text
Job требует capability
  -> Registry ищет активный Processor Instance
  -> Orchestrator проверяет enabled/health/concurrency pool-а
  -> Endpoint selector выбирает endpoint из pool-а
  -> Worker получает adapter
  -> Adapter выполняет обработку через выбранный endpoint
  -> ResultStore сохраняет результат
```

Для MVP алгоритм выбора endpoint-а в pool:

```text
least in-flight среди enabled и healthy/unknown endpoint-ов
```

Weighted round-robin, приоритеты и сложные веса откладываются. Если все endpoint-ы заняты или unhealthy, job возвращается в очередь с коротким delay. Если endpoint падает во время attempt, падает только attempt; следующий retry может выбрать другой endpoint.

Выбранный endpoint фиксируется в истории attempt, чтобы админка и support report могли показать, на каком endpoint-е произошла ошибка.

Endpoint pool поддерживает динамическое обновление списка endpoint-ов и per-endpoint concurrency limit. Уже взятые in-flight leases не прерываются при disabled/removed endpoint-е; такой endpoint просто перестаёт получать новые leases и удаляется из runtime metrics после освобождения последнего lease.

Admin Processor status агрегирует endpoint runtime без активного diagnostic вызова внешнего сервиса:

- текущий `inFlight` и `concurrencyLimit` берутся из Worker heartbeat;
- utilization считается как `inFlight / concurrencyLimit`;
- recent attempts, completed/failed/blocked/retryable counts, avg/p95/max/last duration, completed/minute и last HTTP/error берутся из `processing_attempt_diagnostics`;
- endpoint values в ответах остаются sanitized: без userinfo, query и fragment.

Manual diagnostic check отделён от passive processor status. Для `pdf2txt-http-recognizer` Admin API поддерживает явное действие `POST /api/admin/processors/pdf2txt-http-recognizer/endpoint-checks`: администратор выбирает уже настроенный env/db endpoint, Web берёт PDF из `PdfStampRecognition:Diagnostics:SamplePdfPath`, отправляет его напрямую в выбранный endpoint и возвращает только sanitized runtime result. Этот вызов не создаёт processing job, не сохраняет result payload и не участвует в `/health/*` или realtime polling. Если diagnostic sample не задан, результат остаётся `notConfigured`. Частота ограничена `PdfStampRecognition:Diagnostics:EndpointCheckCooldown`.

## Что видит администратор

Прикладной администратор не должен видеть внутренние классы.

Он должен видеть:

- название возможности;
- включена ли она в поставке;
- включена ли она сейчас;
- статус здоровья;
- последнюю ошибку понятным текстом;
- лимиты;
- endpoint pool или источник обработки;
- количество endpoint-ов;
- health каждого endpoint-а;
- текущий in-flight по pool и по endpoint;
- utilization и capacity по endpoint;
- avg/p95/max/last duration по последним attempts;
- completed/minute и recent failed/blocked/retryable counts;
- кнопки: включить, выключить, проверить, открыть очередь, retry.

Runtime-настройки processor instance управляются через страницу компонента в админке.

Текущий Admin API для managed endpoint-ов:

```http
GET /api/admin/processors/pdf2txt-http-recognizer/endpoints?capability=pdf-stamp-recognition
POST /api/admin/processors/pdf2txt-http-recognizer/endpoints
PATCH /api/admin/processors/pdf2txt-http-recognizer/endpoints/{endpointId}
POST /api/admin/processors/pdf2txt-http-recognizer/endpoint-checks
```

`GET` возвращает sanitized `endpoints[]` и `effectiveEndpoints[]`. Endpoint-ы изменения конфигурации требуют admin session cookie и `X-CSRF-Token`, пишут audit и не вызывают внешний `/recognize_json/`. Manual diagnostic check также требует `X-CSRF-Token`, но не меняет endpoint configuration и не создаёт processing job.

В responses и audit используются только normalized endpoint values без userinfo, query и fragment. Raw endpoint value может храниться в БД для Worker runtime, но не логируется и не возвращается наружу.

Админ может менять только параметры, которые processor definition явно разрешил через settings schema.

Примеры разрешённых параметров:

```text
endpoint
endpoint pool
timeout
retry policy
pool concurrency limit
endpoint concurrency limit
enabled/disabled
credentials/license refs
```

Админ не может создать произвольный новый processor в MVP.

## Проверки Registry

Registry должен поддерживать данные для трёх уровней проверки:

```text
Basic health
Processor health
Optional diagnostic test
```

`Basic health` относится к платформе: Web, Worker, PostgreSQL, storage.

`Processor health` относится к конкретному processor instance: endpoint, лицензия, контейнер, валидность настроек.

`Optional diagnostic test` доступен только если processor definition явно объявляет безопасный диагностический тест.

Диагностический тест не должен создавать обычную пользовательскую job и не должен подменять клиент ЭП.

Для внешних чёрных ящиков без отдельного health endpoint Registry должен поддерживать осторожную модель состояния:

- `config valid`: настройки заполнены и проходят локальную валидацию;
- `passive health`: состояние выводится из последних реальных вызовов processor-а;
- `manual diagnostic`: ручной тест доступен только при явно описанном безопасном сценарии;
- `unknown`: сервис ещё не проверялся или контракт диагностического теста не подтверждён;
- `unreachable`: сетевой вызов недоступен или завершился timeout-ом.

Если для внешнего сервиса можно безопасно отправить заведомо невалидный вход, например нулевой файл, это оформляется как `InvalidInputProbe`. Такой probe считается успешным только при заранее зафиксированном ожидаемом validation error. До проверки фактического ответа внешнего сервиса этот режим остаётся гипотезой, а не обязательной частью MVP.

## Ошибки Registry

Типовые ситуации:

- capability запрошена, но не включена в поставку;
- processor выключен;
- processor unhealthy;
- нет доступного processor-а;
- настройки невалидны;
- превышен лимит параллельности.

Эти ситуации должны возвращать понятные статусы в API и понятные сообщения в админке.

Если превышен лимит параллельности endpoint pool-а, это классифицируется как `processor_overloaded`. Такая ситуация не создаёт failed attempt: job остаётся в очереди или получает короткий delay.
