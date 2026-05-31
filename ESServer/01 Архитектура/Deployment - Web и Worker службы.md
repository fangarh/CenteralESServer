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
