# Capabilities и Processors

## Главная идея

Платформа должна оперировать возможностями, а не техническими реализациями.

Для desktop-клиента сервиса ЭП важно:

```text
что сервер умеет сделать
```

А не:

```text
каким endpoint-ом, контейнером, ML-моделью или лицензированным сервисом это выполняется
```

## Capability

`Capability` — это бизнес-возможность платформы.

Примеры:

```text
pdf-stamp-recognition
  распознать штамп PDF, найти авторов, должности и подписантов

image-normalization
  обработать изображение, повернуть, улучшить, подготовить

document-preview-generation
  создать превью документа

file-virus-scan
  проверить файл

future-document-tool
  будущая серверная функция для сервиса ЭП
```

Capability должен быть понятен менеджеру, администратору и клиентскому приложению.

## Processor

`Processor` — это конкретная реализация capability.

Одна capability может быть выполнена разными processor-ами:

```text
Capability: pdf-stamp-recognition

Processor A:
  внешний ML REST-сервис

Processor B:
  локальный Docker-контейнер

Processor C:
  лицензированный распознаватель

Processor D:
  наш внутренний .NET-код
```

В MVP для одной capability предполагается один активный processor.

## Processor Instance

`Processor Instance` — это настроенный экземпляр processor в конкретной поставке.

Для внешнего HTTP processor-а instance может быть пулом endpoint-ов одного сервиса. Например, `pdf2txt-http-recognizer` может быть развёрнут в пяти Docker-контейнерах, а в админке для него будут прописаны пять endpoint-ов. Это остаётся одним processor instance, потому что все endpoint-ы выполняют один и тот же контракт.

Он содержит:

- включён или выключен;
- endpoint или пул endpoint-ов;
- ключи или license-настройки;
- timeout;
- общий лимит параллельности;
- лимит параллельности на endpoint, если используется пул;
- retry policy;
- health status;
- последнюю ошибку;
- runtime-настройки.

Для endpoint pool Worker выбирает endpoint на момент выполнения attempt. Выбранный endpoint сохраняется в истории attempt для диагностики и audit.

## Почему это важно

Разделение `Capability` и `Processor` даёт:

- стабильный API для сервиса ЭП;
- возможность менять реализацию без изменения клиента;
- разные реализации в разных поставках;
- будущую поддержку резервных processor-ов;
- понятную админку;
- единый подход к ML, не-ML и лицензированным сервисам.

## MVP-правило

В первом релизе:

- capability и processor описывает разработчик;
- администратор не создаёт новые capability с нуля;
- администратор управляет только тем, что уже есть в поставке;
- для одной capability активен один processor;
- модель должна позволять расширение в будущем.

## Пример для PDF

```text
Capability:
  pdf-stamp-recognition

Processor:
  pdf2txt-http-recognizer

Processor Type:
  ExternalHttpProcessor

Adapter:
  Pdf2TxtHttpProcessorAdapter

ResultStore:
  PdfStampRecognitionResultStore

Result:
  существующий JSON-контракт внешнего сервиса
```

## Пример для обработки изображений

```text
Capability:
  image-normalization

Processor:
  internal-image-normalizer

Processor Type:
  InternalProcessor

Result:
  обработанный файл + metadata JSON
```

## Пример для лицензированного сервиса

```text
Capability:
  document-conversion

Processor:
  licensed-document-converter

Processor Type:
  LicensedProcessor

Runtime Settings:
  licenseKeyRef
  endpoint or endpointPool
  poolConcurrencyLimit
  endpointConcurrencyLimit
  timeout
```
