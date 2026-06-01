# Стартовый промпт после очистки контекста

Скопируй этот текст в новую сессию Codex после очистки контекста:

```text
Продолжаем проект CenteralESServer.

Репозиторий: D:\Projects\2026\CenteralESServer
GitHub: https://github.com/fangarh/CenteralESServer.git
Текущий последний checkpoint: 516d691 Add temporary storage hard limit guard
Дата handoff: 2026-06-01

Работай по-русски. Не выводи секреты из db.env. db.env и test.pdf должны оставаться ignored. Не используй Entity Framework: только явный SQL/Npgsql. Используй локальный .NET SDK:

C:\Users\Admin\.dotnet\dotnet.exe

Перед продолжением:
1. Перейди в D:\Projects\2026\CenteralESServer.
2. Выполни git status --short.
3. Проверь git log -1 --oneline, ожидаемый checkpoint: 516d691.
4. Проверь, что db.env игнорируется: git check-ignore -v db.env.
5. Проверь, что test.pdf игнорируется: git check-ignore -v test.pdf.
6. Прочитай:
   - AGENTS.md
   - .planning/STATE.md
   - .planning/ROADMAP.md
   - .planning/REQUIREMENTS.md
   - .planning/HANDOFF.json
   - .planning/.continue-here.md
   - ESServer/04 Админка/Админка MVP.md
   - ESServer/06 Эксплуатация/Безопасность и доступ.md
   - ESServer/06 Эксплуатация/Retry и дедупликация.md
   - ESServer/05 Данные и хранение/Файлы и Storage.md

Текущий статус:
- Phase 1 backend checkpoint complete.
- Phase 2 in progress: Security, Retry, Health, Admin Actions.
- Public PDF flow работает: upload -> PostgreSQL queue -> Worker -> pdf2txt adapter/fake -> result/status.
- Public API защищен API key: Authorization: ApiKey <keyId>.<secret>.
- Admin API защищен session cookie; state-changing Admin endpoint-ы требуют X-CSRF-Token.
- Есть backend manual retry: POST /api/admin/jobs/{jobId}/retry.
- Есть append-only audit table admin_audit_events; manual retry пишет manual_retry_job.
- Есть temporary storage hard/min-free guard: POST /api/pdf-stamp-recognition/jobs возвращает 503 temporary_storage_full при превышении лимита.
- Health endpoints: /health/live и /health/ready.
- Worker heartbeat и job heartbeat реализованы.
- test.pdf локальный и ignored, можно использовать для ручного pdf2txt smoke.

Последняя проверка перед handoff:
- dotnet build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal прошел, только известные MSB3101 warnings по obj cache.
- dotnet test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal прошел: unit 40/40, integration 22/22.
- .codex-local/run-local-smoke.ps1 прошел: SMOKE_OK с fake-pdf2txt.
- После smoke dotnet процессов не осталось.

Следующий логичный шаг для MVP:
1. Support report MVP для Job Details.
2. Либо расширение audit/read API для админки.
3. После этого перейти к Docker Compose delivery MVP: Web, Worker, PostgreSQL, shared local storage, init command первого admin.

Для следующей реализации начни с малого вертикального slice. Если выбираешь support report:
- добавить backend endpoint под admin session, например GET /api/admin/jobs/{jobId}/support-report;
- не включать входной PDF, raw secrets и большие payload;
- включить jobId, subjectId, capability, hash, status, attempts, diagnostics excerpt, worker/processor passive state, result index reference;
- покрыть integration tests;
- обновить ESServer и .planning;
- прогнать build/test/smoke;
- сделать отдельный commit.
```
