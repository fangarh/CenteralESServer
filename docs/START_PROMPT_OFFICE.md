# Стартовый промпт после очистки контекста

Скопируй этот текст в новую сессию Codex после очистки контекста:

```text
Продолжаем проект CenteralESServer.

Репозиторий: D:\Projects\2026\CenteralESServer
GitHub: https://github.com/fangarh/CenteralESServer.git
Текущий последний git checkpoint: 24f212f Fix source audit remediation tails
Дата handoff: 2026-06-03

Работай по-русски. Не выводи секреты из db.env. db.env и test.pdf должны оставаться ignored. Не используй Entity Framework: только явный SQL/Npgsql. Используй локальный .NET SDK:

C:\Users\Admin\.dotnet\dotnet.exe

Перед продолжением:
1. Перейди в D:\Projects\2026\CenteralESServer.
2. Выполни git status --short.
3. Проверь git log -1 --oneline. Если последним коммитом является handoff commit, проверь, что в истории есть checkpoint `24f212f Fix source audit remediation tails`.
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
- Public PDF flow работает: upload -> PostgreSQL queue -> Worker -> pdf2txt adapter -> result/status.
- Public API защищен API key: Authorization: ApiKey <keyId>.<secret>.
- Admin API защищен session cookie; state-changing Admin endpoint-ы требуют X-CSRF-Token.
- Есть backend manual retry: POST /api/admin/jobs/{jobId}/retry.
- Есть append-only audit table admin_audit_events; manual retry пишет manual_retry_job.
- Есть temporary storage hard/min-free guard: POST /api/pdf-stamp-recognition/jobs возвращает 503 temporary_storage_full при превышении лимита.
- Есть support report MVP: GET /api/admin/jobs/{jobId}/support-report под admin session, без CSRF, без входного PDF, temporaryFileKey, raw secrets и raw result payload.
- После code audit Web API разнесён из Program.cs по endpoint groups/contracts/auth/mappings; Program.cs теперь маленький composition root. Manual retry result больше не enum+nullable payload, а typed records.
- Health endpoints: /health/live и /health/ready.
- Worker heartbeat и job heartbeat реализованы.
- test.pdf локальный и ignored, можно использовать для ручного real pdf2txt smoke.
- Docker Compose delivery path проверен без fake recognizer: локальная ignored `.env` задавала `CENTERALES_PDF_RECOGNIZER=Http` и endpoint реального `pdf2txt`; `docker compose -p centerales-real-smoke config --quiet`, `build`, `up -d`, `/health/live`, `/health/ready` и Public upload/polling через ignored `test.pdf` прошли успешно. Worker логировал `HttpPdfStampRecognizer`, POST во внешний `/recognize_json/`, HTTP 200 и completed job.

Последняя проверка перед handoff:
- docker compose config/build/up прошли для project `centerales-real-smoke`.
- PostgreSQL healthy, migrator completed, Web/Worker started.
- `/health/live` и `/health/ready` вернули 200.
- Public upload ignored `test.pdf` -> Worker -> external `pdf2txt` `/recognize_json/` -> result polling вернул `completed`, contract `pdf2txt-recognize-json-v1`.
- Compose stack остановлен через `docker compose -p centerales-real-smoke down`.

Следующий логичный шаг:
1. Закоммитить текущий documentation/planning checkpoint.
2. При необходимости прогнать финальный `dotnet build` / `dotnet test`.
3. Затем выбрать следующий backlog item: refactor `PostgresProcessingJobQueue` или полировка Admin MVP.
```
