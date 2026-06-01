# Стартовый промпт для продолжения из дома

Скопируй этот текст в новую сессию Codex дома:

```text
Продолжаем проект CenteralESServer.

Репозиторий: D:\Projects\2026\CenteralESServer
GitHub: https://github.com/fangarh/CenteralESServer.git
Текущий последний рабочий checkpoint: ac03151 Refactor web API endpoint composition
Последний handoff/context checkpoint: 267e598 Update handoff after web API refactor
Дата handoff: 2026-06-01

Работай по-русски. Не выводи секреты из db.env. db.env, .codex-local/ и test.pdf должны оставаться ignored. Не используй Entity Framework: только явный SQL/Npgsql. Используй локальный .NET SDK:

C:\Users\Admin\.dotnet\dotnet.exe

Перед продолжением:
1. Перейди в D:\Projects\2026\CenteralESServer.
2. Выполни git status --short.
3. Выполни git log --oneline -5. В истории должны быть:
   - ac03151 Refactor web API endpoint composition
   - 267e598 Update handoff after web API refactor
4. Проверь ignored:
   - git check-ignore -v db.env
   - git check-ignore -v test.pdf
   - git check-ignore -v .codex-local
5. Прочитай:
   - AGENTS.md
   - .planning/STATE.md
   - .planning/ROADMAP.md
   - .planning/REQUIREMENTS.md
   - .planning/HANDOFF.json
   - .planning/.continue-here.md
   - docs/START_PROMPT_HOME.md
   - ESServer/04 Админка/Админка MVP.md
   - ESServer/06 Эксплуатация/Безопасность и доступ.md
   - ESServer/06 Эксплуатация/Retry и дедупликация.md
   - ESServer/05 Данные и хранение/Файлы и Storage.md

Текущий статус:
- Phase 1 backend checkpoint complete.
- Phase 2 in progress: Security, Retry, Health, Admin Actions.
- Public PDF flow работает: upload -> PostgreSQL queue -> Worker -> fake/http pdf2txt -> result/status.
- Public API защищен API key: Authorization: ApiKey <keyId>.<secret>.
- Admin API защищен session cookie; state-changing Admin endpoint-ы требуют X-CSRF-Token.
- Manual retry backend complete: POST /api/admin/jobs/{jobId}/retry.
- Support report MVP complete: GET /api/admin/jobs/{jobId}/support-report.
- Temporary storage hard/min-free guard complete: upload возвращает 503 temporary_storage_full при превышении лимита.
- Health endpoints: /health/live и /health/ready.
- Worker heartbeat и job heartbeat реализованы.
- После code audit Web API разнесён из Program.cs по endpoint groups/contracts/auth/mappings; Program.cs теперь маленький composition root.
- ProcessingJobStatusMapper централизует status string conversion.
- Manual retry result теперь typed records, а не enum + nullable payload.

Последняя проверка:
- C:\Users\Admin\.dotnet\dotnet.exe build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal прошёл, только известные MSB3101 warnings по obj cache.
- C:\Users\Admin\.dotnet\dotnet.exe test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal прошёл: unit 40/40, integration 24/24.
- powershell -ExecutionPolicy Bypass -File .\.codex-local\run-local-smoke.ps1 прошёл: SMOKE_OK с fake-pdf2txt.
- Smoke может требовать escalation; без escalation readiness может вернуть 503 из-за sandbox delete restrictions в .codex-local.
- После smoke dotnet процессов не осталось.

Следующий логичный шаг выбери из трёх:
1. Read API для audit/admin visibility:
   - добавить read-only GET /api/admin/audit под admin session;
   - фильтры action, targetType, targetId, actor, date, limit;
   - не раскрывать raw secrets и большие payload;
   - покрыть integration tests;
   - обновить ESServer и .planning;
   - прогнать build/test/smoke;
   - отдельный commit.
2. Продолжить code-audit refactor на PostgresProcessingJobQueue:
   - не менять внешний IProcessingJobQueue contract;
   - выделить SQL helper methods для insert/update subject/job/retry/diagnostics;
   - сохранить transaction semantics;
   - покрыть существующими tests, добавить focused tests только если поведение меняется;
   - прогнать build/test/smoke;
   - отдельный commit.
3. Docker Compose delivery MVP:
   - Web, Worker, PostgreSQL, shared local storage;
   - config examples без секретов;
   - init command первого admin;
   - smoke для compose path.
```
