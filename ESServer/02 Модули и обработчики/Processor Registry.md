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
settingsSchema: endpoint, timeout, maxAttempts, concurrencyLimit
```

### Processor Instance

Описывает runtime-настройку в конкретной поставке.

Пример:

```text
processorKey: pdf2txt-http-recognizer
enabled: true
endpoint: https://pdf2txt.selectel.dt1520.ru/recognize_json/
timeout: 10 minutes
concurrencyLimit: 3
maxAttempts: 5
afterMaxAttempts: admin_required
```

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
- endpoint;
- timeout;
- retry policy;
- concurrency limit;
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

## Использование при обработке

Поток:

```text
Job требует capability
  -> Registry ищет активный Processor Instance
  -> Orchestrator проверяет enabled/health/concurrency
  -> Worker получает adapter
  -> Adapter выполняет обработку
  -> ResultStore сохраняет результат
```

## Что видит администратор

Прикладной администратор не должен видеть внутренние классы.

Он должен видеть:

- название возможности;
- включена ли она в поставке;
- включена ли она сейчас;
- статус здоровья;
- последнюю ошибку понятным текстом;
- лимиты;
- endpoint или источник обработки;
- кнопки: включить, выключить, проверить, открыть очередь, retry.

Runtime-настройки processor instance управляются через страницу компонента в админке.

Админ может менять только параметры, которые processor definition явно разрешил через settings schema.

Примеры разрешённых параметров:

```text
endpoint
timeout
retry policy
concurrency limit
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

## Ошибки Registry

Типовые ситуации:

- capability запрошена, но не включена в поставку;
- processor выключен;
- processor unhealthy;
- нет доступного processor-а;
- настройки невалидны;
- превышен лимит параллельности.

Эти ситуации должны возвращать понятные статусы в API и понятные сообщения в админке.
