# Стартовый промпт после очистки контекста

Скопируй этот текст в новую сессию Codex:

```text
Продолжаем проект CenteralESServer.

Рабочая папка:
D:\Projects\DT1520\CenteralESServer

Опорные commits перед очисткой контекста:
- `a202d64 Record latest admin checkpoints in Obsidian`
- `7585e0b Expose MVP retention policy visibility`

Отвечай по-русски.

Обязательные правила:
- Не выводи секреты из db.env и logon.env.
- db.env, logon.env, .codex-local/ и test.pdf должны оставаться ignored.
- Не используй Entity Framework. Только явный SQL/Npgsql.
- Public API и Admin API не смешивать.
- Health/admin diagnostic не должны запускать обычную бизнес-обработку файла во внешнем processor-е.
- Перед изменениями читай существующие паттерны в src, tests, ESServer, .planning.
- Не откатывай незакоммиченные изменения без явного запроса пользователя.
- Docker сейчас не делать, если пользователь отдельно не попросит.

Сначала выполни:
1. git status --short
2. git log --oneline -5
3. git check-ignore -v db.env
4. git check-ignore -v logon.env
5. git check-ignore -v test.pdf
6. git check-ignore -v .codex-local

Прочитай:
- AGENTS.md
- ESServer/00 Обзор/Текущая точка реализации 2026-06-02.md
- ESServer/04 Админка/Админка MVP.md
- .planning/STATE.md
- .planning/ROADMAP.md
- .planning/REQUIREMENTS.md
- docs/START_PROMPT_HOME.md

Текущий статус:
- Public API baseline реализован.
- PostgreSQL-backed queue реализована.
- Worker processing/retry/heartbeat baseline реализован.
- Admin login/session/CSRF реализован.
- Admin single-job retry реализован.
- Admin support report реализован.
- Admin API keys management реализован.
- Admin users management реализован.
- Admin Storage read-only реализован.
- Admin Results read-only реализован.
- Admin Result Details показывает безопасный PDF summary без raw JSON payload.
- Admin Result Details имеет отдельный controlled `Debug JSON` download через `GET /api/admin/results/{resultIndexId}/payload`.
- Admin Settings read-only реализован.
- Admin Storage/Settings показывают read-only retention policy MVP без cleanup-действий.
- Admin Audit UI с фильтрами и safe details реализован.
- SQL migration runner без EF реализован.
- Отдельное тестовое WinForms-приложение для создания первого admin реализовано:
  src/Apps/CenteralES.Admin.Bootstrap.WinForms
- Backend smoke для WinForms bootstrap path реализован:
  .codex-local/run-admin-bootstrap-smoke.ps1
- Admin Services registry API реализован: `GET /api/admin/services` возвращает read-only список зарегистрированных MVP-сервисов без секретов и без активного вызова внешних processor-ов.
- WinForms test client расширен вкладкой `MVP сервисы`: получает текущий MVP service list через `GET /api/admin/services`, тестирует `/health/live`, `/health/ready`, passive processor status и опциональный Public PDF upload/polling при наличии API key и PDF.
- Docker Compose еще не реализован и сейчас исключен из работы.

Проверки последнего кодового checkpoint-а `Expose MVP retention policy visibility`:
- dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
  Результат: passed
- node --check src\Apps\CenteralES.Web\wwwroot\admin\app.js
  Результат: passed
- targeted storage/settings integration slice
  Результат: 6 passed
- full solution
  Результат: 50 unit + 54 integration passed
- git diff --check
  Результат: passed
- C:\Users\Admin\.dotnet\dotnet.exe отсутствует в текущем окружении; проверки выполнялись системным dotnet.

Проверки WinForms bootstrap smoke checkpoint:
- powershell -ExecutionPolicy Bypass -File .\.codex-local\run-admin-bootstrap-smoke.ps1
  Результат: 1 integration test passed
- dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
  Результат: passed
- dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
  Результат: 50 unit + 54 integration passed

Следующий логичный шаг без Docker:
Admin UI polish для Result Details/Debug JSON или cleanup dry-run planning без Docker.

Зачем:
- PDF summary уже закрыт безопасными счетчиками и excerpts;
- raw JSON отделен от обычного summary и доступен только через explicit debug download;
- raw payload и входные PDF нельзя показывать в обычном Admin UI;
- retention policy видна read-only в Storage/Settings, но cleanup worker и ручное удаление не включены;
- WinForms client использует `GET /api/admin/services`, поэтому registry-долг для текущего MVP закрыт.
```
