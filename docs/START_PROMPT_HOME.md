# Стартовый промпт для продолжения из дома

Скопируй этот текст в новую сессию Codex дома:

```text
Продолжаем проект CenteralESServer.

Рабочая папка:
D:\Projects\DT1520\CenteralESServer

GitHub:
https://github.com/fangarh/CenteralESServer.git

Ветка:
main

Последний сохраненный context/handoff commit:
605ab06 Save current project state in Obsidian

Последние важные checkpoint commits:
- dd33ff3 Add admin audit filters
- 1f4dfdd Add admin settings UI
- ecde808 Add admin results UI
- a4c1627 Add SQL migration runner and split admin UI helpers

Работай по-русски. Не выводи секреты из db.env и logon.env.
db.env, logon.env, .codex-local/ и test.pdf должны оставаться ignored.
Не используй Entity Framework: только явный SQL/Npgsql.

Перед продолжением:
1. Перейди в D:\Projects\DT1520\CenteralESServer.
2. Выполни git status --short. Рабочая копия должна быть чистой.
3. Выполни git log --oneline -5. В истории должны быть:
   - 605ab06 Save current project state in Obsidian
   - dd33ff3 Add admin audit filters
   - 1f4dfdd Add admin settings UI
4. Проверь ignored:
   - git check-ignore -v db.env
   - git check-ignore -v logon.env
   - git check-ignore -v test.pdf
   - git check-ignore -v .codex-local
5. Прочитай:
   - AGENTS.md
   - ESServer/00 Обзор/Текущая точка реализации 2026-06-02.md
   - ESServer/00 Обзор/Текущая точка обсуждения.md
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
- Admin Settings read-only реализован.
- Admin Audit UI с фильтрами и safe details реализован.
- SQL migration runner без EF реализован.
- Отдельное тестовое WinForms-приложение для создания первого admin реализовано:
  src/Apps/CenteralES.Admin.Bootstrap.WinForms
- Backend smoke для WinForms bootstrap path реализован:
  .codex-local/run-admin-bootstrap-smoke.ps1
- ADMIN-03 закрыт для текущего MVP-набора dangerous actions.
- Docker Compose еще не реализован.

Текущая версия admin static assets:
20260602-11

Последняя полная проверка после Audit UI:
- node --check src\Apps\CenteralES.Web\wwwroot\admin\app.js
- dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
- dotnet test tests\Integration\CenteralES.IntegrationTests.csproj --no-build --no-restore --filter "Admin_ui|Admin_audit|Admin_settings|Admin_storage|Admin_results|Admin_result_details" -v:minimal
  Результат: 18 passed
- dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
  Результат: 40 unit + 44 integration passed
- HTTP/Playwright smoke для /admin и /api/admin/audit прошел.
- Ожидаемая console error до логина: 401 /api/admin/auth/me.

Проверка после WinForms bootstrap smoke:
- powershell -ExecutionPolicy Bypass -File .\.codex-local\run-admin-bootstrap-smoke.ps1
  Результат: 1 integration test passed
- dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
  Результат: passed
- dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
  Результат: 50 unit + 49 integration passed
- C:\Users\Admin\.dotnet\dotnet.exe отсутствует в текущем окружении; проверки выполнялись системным dotnet.

Следующий логичный шаг без Docker:
Raw JSON controlled debug endpoint.

Зачем:
- PDF summary уже закрыт безопасными счетчиками и excerpts;
- если нужен raw JSON, это должен быть отдельный controlled/debug endpoint;
- WinForms bootstrap path покрыт backend smoke без запуска GUI.

После этого следующий крупный блок:
Docker Compose Delivery MVP:
- Dockerfile для Web;
- Dockerfile для Worker;
- docker-compose с PostgreSQL;
- shared temporary storage volume;
- env examples без секретов;
- smoke-инструкции для офиса.
```
