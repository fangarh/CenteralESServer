# Стартовый промпт для продолжения в офисе

Скопируй этот текст в новую сессию после `git pull`:

```text
Продолжаем проект CenteralESServer.

Репозиторий: D:\Projects\DT1520\CenteralESServer
GitHub: https://github.com/fangarh/CenteralESServer

Важно:
- Отвечай по-русски.
- Не выводи секреты из db.env.
- db.env должен оставаться в .gitignore.
- Работаем интерактивно по GSD, без автономного/yolo режима.
- EF не используем, только явный SQL/Npgsql.
- Docker считаем основным способом поставки, Windows Service не нужен.
- Админка не вызывает внешние сервисы, кроме health/check-проверок.

Текущее состояние:
- Последний запушенный commit перед этим handoff: 3832ed1 Process worker jobs from temporary storage.
- Phase 1: Walking Skeleton PDF Processing, in progress.
- Web API уже пишет jobs в PostgreSQL queue.
- Worker забирает job, читает PDF из temporary storage, передает stream recognizer-у, сохраняет result, завершает job и чистит временный файл.
- FakePdfStampRecognizer используется как временная заглушка, пока внешний pdf2txt недоступен.
- Public API:
  - POST /api/pdf-stamp-recognition/jobs
  - GET /api/pdf-stamp-recognition/results/{hash}
  - GET /api/jobs/{jobId}
- PostgreSQL bootstrap/schema уже есть.
- db.env содержит локальную строку PostgreSQL, база может создаваться bootstrapper-ом.
- Все тесты на последнем checkpoint проходили: dotnet test CenteralESServer.sln = 24 tests, 0 failed.

Что сделать в начале:
1. Перейди в D:\Projects\DT1520\CenteralESServer.
2. Выполни git pull.
3. Проверь git status.
4. Проверь, что db.env существует и игнорируется git:
   git check-ignore -v db.env
5. Запусти:
   dotnet test CenteralESServer.sln
6. Если тесты зеленые, продолжай Phase 1.

Следующий логичный шаг:
- Реализовать первый реальный pdf2txt HTTP adapter boundary рядом с fake adapter, но с учетом того, что https://pdf2txt.selectel.dt1520.ru/recognize_json/ может быть недоступен.
- Нужно добавить конфигурацию processor endpoint pool:
  - endpointPool
  - poolConcurrencyLimit
  - endpointConcurrencyLimit
  - timeout
- Adapter должен:
  - принимать PDF stream;
  - отправлять multipart/form-data на endpoint;
  - сохранять сырой успешный JSON как payload;
  - нормализовать ошибки без утечки raw external errors в Public API;
  - быть покрыт тестами через fake HttpMessageHandler/локальный handler, без зависимости от внешнего сервиса.
- После этого можно будет переключать Worker между FakePdfStampRecognizer и HttpPdfStampRecognizer через конфиг.
```
