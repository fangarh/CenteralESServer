# AGENTS.md

## Проект

CenteralESServer — .NET 9 серверная платформа для desktop-клиента сервиса ЭП. MVP-сценарий: асинхронное распознавание PDF через `pdf2txt` с очередью, retry, result/status API и минимальной админской видимостью.

Рабочий путь:

```text
D:\Projects\2026\CenteralESServer
```

## Обязательные правила

- Отвечай пользователю по-русски.
- Не выводи секреты из `db.env` и `logon.env`.
- `db.env`, `logon.env`, `.codex-local/` и `test.pdf` должны оставаться ignored.
- Для основной локальной учётки Admin использовать ignored `logon.env` как источник истины; при обновлении БД синхронизировать login/password из него и не hardcode-ить значения.
- Не использовать Entity Framework. Только явный SQL/Npgsql.
- Использовать локальный SDK: `C:\Users\Admin\.dotnet\dotnet.exe`.
- Перед кодом читать существующие паттерны в `src`, `tests`, `ESServer`, `.planning`.
- Если меняется архитектурное решение, сначала обновить `ESServer`, затем `.planning`.
- Health/admin diagnostic не должны запускать обычную бизнес-обработку файла во внешнем processor-е.
- Public API и Admin API не смешивать.

## Документация и источник истины

Главный источник архитектуры:

```text
ESServer/
```

Текущее состояние:

```text
.planning/STATE.md
.planning/ROADMAP.md
.planning/REQUIREMENTS.md
.planning/HANDOFF.json
.planning/.continue-here.md
docs/START_PROMPT_OFFICE.md
```

## Технологии

- .NET 9
- ASP.NET Core Minimal API
- PostgreSQL
- Npgsql
- xUnit
- Docker Compose как целевой MVP delivery path

## Проверки

Использовать:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
C:\Users\Admin\.dotnet\dotnet.exe test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
```

Локальный smoke:

```powershell
powershell -ExecutionPolicy Bypass -File .\.codex-local\run-local-smoke.ps1
```

Smoke может требовать escalation, потому что пишет во временное хранилище и запускает локальные Web/Worker процессы.

## Известные предупреждения

`dotnet build` может выводить MSB3101 warnings по `obj\...\*.AssemblyReference.cache` из-за sandbox permissions. Если сборка завершилась успешно и ошибок нет, эти warnings не блокируют работу.

## Следующий фокус

Phase 2 продолжается после checkpoint `516d691 Add temporary storage hard limit guard`.

Логичные следующие шаги:

- Support report MVP для Job Details.
- Read API для audit/admin visibility.
- Затем Docker Compose delivery MVP.
