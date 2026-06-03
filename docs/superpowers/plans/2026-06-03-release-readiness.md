# Release Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prepare the MVP for repeatable release validation with real `pdf2txt`, not the fake recognizer.

**Architecture:** Keep `compose.yaml` as the local/demo baseline, add a production override that forces `Http` recognizer configuration for both Web and Worker, and provide a smoke script that exercises the release path without seeding secrets directly into the database. Release documentation becomes the operator-facing source of truth.

**Tech Stack:** Docker Compose, PowerShell, ASP.NET Core Minimal API, PostgreSQL, explicit SQL/Npgsql, existing Admin/Public APIs.

---

## File Structure

- Create `compose.prod.yaml`: production Compose override that forces `Http` recognizer and requires `CENTERALES_PDF2TXT_ENDPOINT`.
- Create `.env.production.example`: committed production environment template; real `.env.production` remains ignored.
- Modify `.gitignore`: unignore `.env.production.example` while keeping `.env`, `.env.*`, `db.env`, `test.pdf`, and `.codex-local/` ignored.
- Create `scripts/run-release-smoke.ps1`: repeatable release smoke that starts Compose with the production override, checks health, uploads a PDF using an operator-supplied Public API key, polls for completion, and optionally tears the stack down.
- Create `docs/RELEASE_RUNBOOK.md`: release runbook covering env setup, first admin bootstrap, API key creation, smoke execution, backup/restore minimum, and volume lifecycle.
- Modify `ESServer/00 Обзор/Итоги Docker Compose baseline 2026-06-02.md`: reference the production override and release smoke script.
- Modify `.planning/STATE.md`, `.planning/ROADMAP.md`, `.planning/REQUIREMENTS.md`, `.planning/HANDOFF.json`, and `docs/START_PROMPT_OFFICE.md`: record release-readiness progress after implementation and verification.

---

### Task 1: Production Compose Configuration

**Files:**
- Create: `compose.prod.yaml`
- Create: `.env.production.example`
- Modify: `.gitignore`

- [x] **Step 1: Add `compose.prod.yaml`**

Create a Compose override with this content:

```yaml
services:
  web:
    environment:
      PdfStampRecognition__Recognizer: Http
      PdfStampRecognition__Processor__endpointPool__0: ${CENTERALES_PDF2TXT_ENDPOINT:?Set CENTERALES_PDF2TXT_ENDPOINT in .env.production or environment}

  worker:
    environment:
      PdfStampRecognition__Recognizer: Http
      PdfStampRecognition__Processor__endpointPool__0: ${CENTERALES_PDF2TXT_ENDPOINT:?Set CENTERALES_PDF2TXT_ENDPOINT in .env.production or environment}
```

- [x] **Step 2: Add `.env.production.example`**

Create a production template with placeholders and no secrets:

```text
# Copy to .env.production for production-like Docker Compose runs. Do not commit .env.production.

CENTERALES_POSTGRES_DB=centerales
CENTERALES_POSTGRES_USER=centerales
CENTERALES_POSTGRES_PASSWORD=replace_with_strong_password

CENTERALES_PDF2TXT_ENDPOINT=https://your-pdf2txt-host/recognize_json/

CENTERALES_WEB_PORT=8080
CENTERALES_ALLOWED_HOSTS=localhost;127.0.0.1
```

- [x] **Step 3: Update `.gitignore` exception**

Keep `.env.*` ignored but add:

```gitignore
!.env.production.example
```

- [x] **Step 4: Verify production config requires endpoint**

Run:

```powershell
docker compose --env-file .env.production.example -f compose.yaml -f compose.prod.yaml config --quiet
```

Expected: success with the placeholder endpoint.

---

### Task 2: Release Smoke Script

**Files:**
- Create: `scripts/run-release-smoke.ps1`

- [x] **Step 1: Create script parameters**

The script must accept:

```powershell
param(
    [string]$ProjectName = "centerales-release-smoke",
    [string]$EnvFile = ".env.production",
    [string]$PdfPath = "test.pdf",
    [Parameter(Mandatory = $true)]
    [string]$ApiKeyId,
    [Parameter(Mandatory = $true)]
    [string]$ApiKeySecret,
    [int]$TimeoutSeconds = 300,
    [switch]$SkipBuild,
    [switch]$KeepRunning
)
```

- [x] **Step 2: Implement Compose lifecycle**

The script must run:

```powershell
docker compose --env-file $EnvFile -p $ProjectName -f compose.yaml -f compose.prod.yaml config --quiet
docker compose --env-file $EnvFile -p $ProjectName -f compose.yaml -f compose.prod.yaml build
docker compose --env-file $EnvFile -p $ProjectName -f compose.yaml -f compose.prod.yaml up -d
```

When `-SkipBuild` is passed, skip only the build step.

- [x] **Step 3: Implement health checks**

Poll:

```text
http://127.0.0.1:{CENTERALES_WEB_PORT}/health/live
http://127.0.0.1:{CENTERALES_WEB_PORT}/health/ready
```

Both must return HTTP 200 before upload.

- [x] **Step 4: Implement Public upload and result polling**

Upload with:

```text
POST /api/pdf-stamp-recognition/jobs
Authorization: ApiKey <ApiKeyId>.<ApiKeySecret>
multipart file=<PdfPath>
```

Poll:

```text
GET /api/pdf-stamp-recognition/results/{hash}
```

Expected final result:

```text
HTTP 200
status=completed
contractVersion=pdf2txt-recognize-json-v1
result.source is not fake-pdf2txt
```

- [x] **Step 5: Avoid secret leakage**

The script must never echo the API key secret, `.env.production` contents, PostgreSQL password, or raw result payload.

- [x] **Step 6: Cleanup**

Unless `-KeepRunning` is passed, run:

```powershell
docker compose --env-file $EnvFile -p $ProjectName -f compose.yaml -f compose.prod.yaml down
```

---

### Task 3: Release Runbook

**Files:**
- Create: `docs/RELEASE_RUNBOOK.md`
- Modify: `ESServer/00 Обзор/Итоги Docker Compose baseline 2026-06-02.md`

- [x] **Step 1: Document production env setup**

Include exact commands:

```powershell
Copy-Item .env.production.example .env.production
notepad .env.production
```

- [x] **Step 2: Document first admin and API key flow**

State that the first admin is created with the WinForms bootstrap/test client, then a Public API key is created in Admin UI/API with capability:

```text
pdf-stamp-recognition
```

- [x] **Step 3: Document release smoke**

Include:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-release-smoke.ps1 -EnvFile .env.production -PdfPath .\test.pdf -ApiKeyId "<key-id>" -ApiKeySecret "<secret>"
```

- [x] **Step 4: Document volume lifecycle and backup**

Cover:

```text
centerales-postgres-data
centerales-temporary-storage
pg_dump before update
docker compose down does not remove volumes
docker compose down -v deletes persisted data
```

---

### Task 4: Verification and Planning Checkpoint

**Files:**
- Modify: `.planning/STATE.md`
- Modify: `.planning/ROADMAP.md`
- Modify: `.planning/REQUIREMENTS.md`
- Modify: `.planning/HANDOFF.json`
- Modify: `docs/START_PROMPT_OFFICE.md`

- [x] **Step 1: Run static checks**

Run:

```powershell
docker compose --env-file .env.production.example -f compose.yaml -f compose.prod.yaml config --quiet
git diff --check
```

- [x] **Step 2: Run build/test gate**

Run:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
C:\Users\Admin\.dotnet\dotnet.exe test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
```

- [x] **Step 3: Update planning docs**

Record that release readiness now has:

```text
production Compose override
production env example
repeatable release smoke script
release runbook
```

- [ ] **Step 4: Commit**

Commit:

```powershell
git add compose.prod.yaml .env.production.example .gitignore scripts/run-release-smoke.ps1 docs/RELEASE_RUNBOOK.md "ESServer/00 Обзор/Итоги Docker Compose baseline 2026-06-02.md" .planning/STATE.md .planning/ROADMAP.md .planning/REQUIREMENTS.md .planning/HANDOFF.json docs/START_PROMPT_OFFICE.md docs/superpowers/plans/2026-06-03-release-readiness.md
git commit -m "Prepare release readiness workflow"
```

---

## Self-Review

- Spec coverage: Covers release production Compose without fake, repeatable smoke, release runbook, secret/config hygiene, and final verification gate.
- Placeholder scan: `.env.production.example` intentionally contains placeholder values and is documented as a template, not executable production secrets.
- Type consistency: Script parameters and commands use the same file names throughout: `compose.prod.yaml`, `.env.production`, `.env.production.example`, and `scripts/run-release-smoke.ps1`.
