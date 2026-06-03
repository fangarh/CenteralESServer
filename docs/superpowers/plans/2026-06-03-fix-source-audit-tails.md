# Source Audit Tails Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the concrete source-audit findings: sanitize Admin Job Details diagnostics, remove stray test artifacts, document/guard MVP fake recognizer usage, and make queue hash-alias registration less coupled.

**Architecture:** Keep Public API and Admin API boundaries separate. Treat raw processor diagnostics as internal storage data; Admin responses may expose only bounded safe excerpts. Keep fake recognizer available for local smoke/Compose demo, but make the production boundary explicit and test-covered.

**Tech Stack:** .NET 9, ASP.NET Core Minimal API, xUnit, PostgreSQL/Npgsql, static Admin UI assets.

---

## File Structure

- Modify `src/Shared/CenteralES.Infrastructure/Processing/PostgresAdminReadStoreHelpers.cs`
  - Owns safe excerpt normalization. Reuse it for all Admin diagnostic excerpts.
- Modify `src/Shared/CenteralES.Infrastructure/Processing/PostgresAdminJobReadStore.cs`
  - Stop carrying raw excerpts in Admin job details models, or sanitize before returning the domain read model.
- Modify `src/Modules/Admin/AdminProcessingJobReadModels.cs`
  - Rename `RawErrorExcerpt` to `Excerpt` in admin-facing read model if the value is sanitized.
- Modify `src/Apps/CenteralES.Web/ApiMappings.cs`
  - Map only sanitized `Excerpt` into `AdminAttemptDiagnosticsResponse`.
- Modify `tests/Integration/WebApiContractTests.cs`
  - Add contract coverage that Admin Job Details does not return long/raw diagnostics and does not include known raw payload text.
- Modify `tests/err/err.png` or `.gitignore`
  - Prefer deleting the stray artifact if it is not intentionally used. If it must stay local, add a narrow ignore rule.
- Modify `src/Shared/CenteralES.Infrastructure/Processing/PostgresProcessingJobQueue.cs`
  - Replace `RegisterContentHashesAsync` reuse of `CreateProcessingJobCommand` with a dedicated helper input.
- Modify `src/Modules/Processing/Queue/CreateProcessingJobCommand.cs`
  - Keep `RegisterProcessingContentHashesCommand`, no temporary-file placeholder.
- Modify `tests/Integration/PostgresProcessingJobQueueTests.cs`
  - Add direct coverage for registering hash aliases on an existing subject.
- Modify `compose.yaml`, `.env.example`, and `ESServer/01 Архитектура/Deployment - Web и Worker службы.md`
  - Make fake recognizer behavior explicit and add the exact production override path.

---

### Task 1: Sanitize Admin Job Details Diagnostics

**Files:**
- Modify: `src/Modules/Admin/AdminProcessingJobReadModels.cs`
- Modify: `src/Shared/CenteralES.Infrastructure/Processing/PostgresAdminJobReadStore.cs`
- Modify: `src/Apps/CenteralES.Web/ApiMappings.cs`
- Test: `tests/Integration/WebApiContractTests.cs`

- [ ] **Step 1: Write a failing contract test**

Add an integration test that seeds a blocked job with a deliberately long/raw diagnostic string and asserts that `/api/admin/jobs/{jobId}` returns a bounded `diagnostics.excerpt`, not the raw stored value.

Use a raw string with recognizable sensitive text:

```csharp
var rawExcerpt = string.Concat(
    "secret-token-value ",
    new string('x', 2500));
```

Expected assertions:

```csharp
Assert.DoesNotContain("secret-token-value", body, StringComparison.OrdinalIgnoreCase);
var excerpt = payload.GetProperty("diagnostics").GetProperty("excerpt").GetString();
Assert.NotNull(excerpt);
Assert.True(excerpt!.Length <= 2003);
```

- [ ] **Step 2: Run the new test and confirm it fails**

Run:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe test tests\Integration\CenteralES.IntegrationTests.csproj --no-build --no-restore -v:minimal --filter FullyQualifiedName~Admin_job_details
```

Expected: FAIL because current mapping passes `job.RawErrorExcerpt` through to `diagnostics.excerpt`.

- [ ] **Step 3: Rename/read-model the sanitized value**

In `AdminProcessingJobReadModels.cs`, change:

```csharp
string? RawErrorExcerpt,
```

to:

```csharp
string? Excerpt,
```

This makes the model name match the Admin API contract and prevents accidental raw reuse.

- [ ] **Step 4: Sanitize at read-store boundary**

In `PostgresAdminJobReadStore.GetJobAsync`, replace the raw reader assignment with:

```csharp
reader.IsDBNull(18)
    ? null
    : PostgresAdminReadStoreHelpers.ToSafeExcerpt(reader.GetString(18)),
```

The SQL may still read `d.raw_error_excerpt`; the returned admin read model must not carry a raw value.

- [ ] **Step 5: Update API mapping**

In `ApiMappings.ToAdminJobDetailsResponse`, replace:

```csharp
job.RawErrorExcerpt,
```

with:

```csharp
job.Excerpt,
```

- [ ] **Step 6: Update support report mapping**

In `PostgresAdminJobReadStore.GetJobSupportReportAsync`, replace:

```csharp
PostgresAdminReadStoreHelpers.ToSafeExcerpt(job.RawErrorExcerpt)
```

with:

```csharp
job.Excerpt
```

The support report should not re-sanitize a value that is already sanitized.

- [ ] **Step 7: Run focused integration tests**

Run:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe test tests\Integration\CenteralES.IntegrationTests.csproj --no-build --no-restore -v:minimal --filter FullyQualifiedName~Admin
```

Expected: PASS.

- [ ] **Step 8: Commit**

```powershell
git add src\Modules\Admin\AdminProcessingJobReadModels.cs src\Shared\CenteralES.Infrastructure\Processing\PostgresAdminJobReadStore.cs src\Apps\CenteralES.Web\ApiMappings.cs tests\Integration\WebApiContractTests.cs
git commit -m "fix: sanitize admin job diagnostics excerpt"
```

---

### Task 2: Remove or Ignore Stray Test Artifact

**Files:**
- Delete or ignore: `tests/err/err.png`
- Optional modify: `.gitignore`

- [ ] **Step 1: Confirm the file is unreferenced**

Run:

```powershell
rg -n "err\.png|tests\\err|tests/err" .
```

Expected: no production/test references except possible git status output.

- [ ] **Step 2: Remove the artifact if unreferenced**

Use PowerShell native deletion:

```powershell
Remove-Item -LiteralPath tests\err\err.png
```

If the empty directory remains and is not needed:

```powershell
Remove-Item -LiteralPath tests\err
```

- [ ] **Step 3: If the artifact must remain local, add a narrow ignore**

Only if the file is intentionally generated locally, add to `.gitignore`:

```gitignore
# Local diagnostic screenshots
tests/err/
```

- [ ] **Step 4: Verify status**

Run:

```powershell
git status --short
```

Expected: no `?? tests/err/`.

- [ ] **Step 5: Commit only if `.gitignore` changed**

```powershell
git add .gitignore
git commit -m "chore: ignore local diagnostic screenshots"
```

---

### Task 3: Decouple Hash Alias Registration from CreateProcessingJobCommand

**Files:**
- Modify: `src/Shared/CenteralES.Infrastructure/Processing/PostgresProcessingJobQueue.cs`
- Modify: `src/Modules/Processing/Queue/CreateProcessingJobCommand.cs`
- Test: `tests/Integration/PostgresProcessingJobQueueTests.cs`
- Test: `tests/Unit/SubmitPdfStampRecognitionJobHandlerTests.cs`

- [ ] **Step 1: Add direct integration coverage**

Add a test to `PostgresProcessingJobQueueTests`:

```csharp
[Fact]
public async Task RegisterContentHashes_adds_aliases_to_existing_subject()
{
    var connectionString = IntegrationTestDatabase.TryReadConnectionString();
    if (connectionString is null)
    {
        return;
    }

    var bootstrapper = new PostgresDatabaseBootstrapper();
    await bootstrapper.EnsureDatabaseAsync(connectionString, CancellationToken.None);
    await using var dataSource = NpgsqlDataSource.Create(connectionString);
    await bootstrapper.ApplySchemaAsync(dataSource, CancellationToken.None);
    await ResetProcessingTablesAsync(dataSource, CancellationToken.None);

    var now = DateTimeOffset.UtcNow;
    var queue = new PostgresProcessingJobQueue(dataSource);
    var canonical = $"sha256:{Guid.NewGuid():N}";
    var alias = $"gost-r-34.11-2012-256:{Guid.NewGuid():N}";
    var enqueued = await queue.EnqueueAsync(
        new CreateProcessingJobCommand(
            PdfStampRecognitionConstants.Capability,
            canonical,
            $"temp/{Guid.NewGuid():N}.pdf",
            now),
        CancellationToken.None);

    await queue.RegisterContentHashesAsync(
        new RegisterProcessingContentHashesCommand(
            enqueued.SubjectId,
            PdfStampRecognitionConstants.Capability,
            now.AddSeconds(1),
            [new ProcessingContentHash(ContentHashAlgorithms.GostR34112012_256, alias)]),
        CancellationToken.None);

    var current = await queue.GetCurrentByHashAsync(
        PdfStampRecognitionConstants.Capability,
        alias,
        CancellationToken.None);

    Assert.NotNull(current);
    Assert.Equal(enqueued.JobId, current.JobId);
}
```

- [ ] **Step 2: Run the new test**

Run:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe test tests\Integration\CenteralES.IntegrationTests.csproj --no-build --no-restore -v:minimal --filter FullyQualifiedName~RegisterContentHashes_adds_aliases
```

Expected: PASS with current behavior. This locks behavior before refactor.

- [ ] **Step 3: Introduce a private helper that does not need CreateProcessingJobCommand**

In `PostgresProcessingJobQueue.cs`, replace the current command-shaped helper:

```csharp
private static IReadOnlyList<ProcessingContentHash> ResolveContentHashes(CreateProcessingJobCommand command)
{
    if (command.ContentHashes is { Count: > 0 })
    {
        return command.ContentHashes;
    }

    var algorithm = command.ContentHash.Split(':', 2)[0];
    return [new ProcessingContentHash(algorithm, command.ContentHash)];
}
```

with two explicit helpers:

```csharp
private static IReadOnlyList<ProcessingContentHash> ResolveContentHashes(
    string contentHash,
    IReadOnlyList<ProcessingContentHash>? contentHashes)
{
    if (contentHashes is { Count: > 0 })
    {
        return contentHashes;
    }

    var algorithm = contentHash.Split(':', 2)[0];
    return [new ProcessingContentHash(algorithm, contentHash)];
}

private static IReadOnlyList<ProcessingContentHash> NormalizeContentHashes(
    IReadOnlyList<ProcessingContentHash> contentHashes)
{
    return contentHashes
        .Where(hash => !string.IsNullOrWhiteSpace(hash.Algorithm)
            && !string.IsNullOrWhiteSpace(hash.HashValue))
        .GroupBy(hash => hash.HashValue, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToArray();
}
```

- [ ] **Step 4: Replace placeholder command reuse**

Replace this pattern:

```csharp
new CreateProcessingJobCommand(
    command.Capability,
    command.ContentHashes[0].HashValue,
    string.Empty,
    command.RegisteredAt,
    command.ContentHashes)
```

with a dedicated call that passes `command.ContentHashes` directly into alias upsert logic.

Change `UpsertContentHashesAsync` signature from:

```csharp
private static async Task UpsertContentHashesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid subjectId,
    CreateProcessingJobCommand command,
    CancellationToken cancellationToken)
```

to:

```csharp
private static async Task UpsertContentHashesAsync(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    Guid subjectId,
    string capability,
    DateTimeOffset createdAt,
    IReadOnlyList<ProcessingContentHash> contentHashes,
    CancellationToken cancellationToken)
```

Inside the helper, use:

```csharp
var hashes = NormalizeContentHashes(contentHashes);
```

and replace parameter sources:

```csharp
insert.Parameters.AddWithValue("capability", capability);
insert.Parameters.AddWithValue("created_at", createdAt);
```

Update enqueue call sites to pass:

```csharp
command.Capability,
command.CreatedAt,
ResolveContentHashes(command.ContentHash, command.ContentHashes),
```

Update `RegisterContentHashesAsync` to pass:

```csharp
command.Capability,
command.RegisteredAt,
command.ContentHashes,
```

- [ ] **Step 5: Run queue tests**

Run:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe test tests\Integration\CenteralES.IntegrationTests.csproj --no-build --no-restore -maxcpucount:1 -v:minimal --filter FullyQualifiedName~PostgresProcessingJobQueueTests
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src\Shared\CenteralES.Infrastructure\Processing\PostgresProcessingJobQueue.cs tests\Integration\PostgresProcessingJobQueueTests.cs
git commit -m "refactor: decouple content hash alias registration"
```

---

### Task 4: Make Fake Recognizer Boundary Explicit for Delivery

**Files:**
- Modify: `.env.example`
- Modify: `compose.yaml`
- Modify: `ESServer/01 Архитектура/Deployment - Web и Worker службы.md`
- Optional modify: `docs/START_PROMPT_OFFICE.md`

- [ ] **Step 1: Clarify `.env.example`**

Keep Fake for local self-contained smoke, but add exact production override:

```dotenv
# Local demo default. For real pdf2txt processing set:
# CENTERALES_PDF_RECOGNIZER=Http
# CENTERALES_PDF2TXT_ENDPOINT=https://your-pdf2txt-host/recognize_json/
CENTERALES_PDF_RECOGNIZER=Fake
CENTERALES_PDF2TXT_ENDPOINT=
```

- [ ] **Step 2: Add a Compose comment near worker env**

In `compose.yaml`, above `PdfStampRecognition__Recognizer`, add:

```yaml
      # Fake keeps local Compose self-contained. Set CENTERALES_PDF_RECOGNIZER=Http
      # and CENTERALES_PDF2TXT_ENDPOINT for real pdf2txt processing.
```

- [ ] **Step 3: Update architecture docs first if the delivery stance changes**

If the desired delivery stance is "Compose MVP must use real pdf2txt by default", update `ESServer/01 Архитектура/Deployment - Web и Worker службы.md` before changing code/config.

If the stance remains "self-contained demo defaults to Fake", document that this is demo-only and production must override it.

- [ ] **Step 4: Run build**

Run:

```powershell
C:\Users\Admin\.dotnet\dotnet.exe build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
```

Expected: PASS, known MSB3101 warnings are acceptable.

- [ ] **Step 5: Commit**

```powershell
git add .env.example compose.yaml "ESServer\01 Архитектура\Deployment - Web и Worker службы.md"
git commit -m "docs: clarify fake recognizer compose boundary"
```

---

### Task 5: Re-run Verification and Record Remaining MVP Deferrals

**Files:**
- Modify: `.planning/STATE.md`
- Modify: `.planning/ROADMAP.md`
- Modify: `.planning/REQUIREMENTS.md`
- Optional modify: `ESServer/05 Данные и хранение/Файлы и Storage.md`

- [ ] **Step 1: Run build**

```powershell
C:\Users\Admin\.dotnet\dotnet.exe build CenteralESServer.sln --no-restore -maxcpucount:1 -v:minimal
```

Expected: PASS. Known `obj` permission warnings do not block.

- [ ] **Step 2: Run tests**

```powershell
C:\Users\Admin\.dotnet\dotnet.exe test CenteralESServer.sln --no-build --no-restore -maxcpucount:1 -v:minimal
```

Expected: unit and integration tests pass. If `Complete_rejects_job_that_is_not_processing` flakes again, add a separate investigation task before merging.

- [ ] **Step 3: Update planning**

Record:

- Admin Job Details diagnostics excerpt is sanitized.
- Stray test artifact removed or ignored.
- Hash alias registration no longer reuses job creation command placeholders.
- Compose fake recognizer remains demo default, or real recognizer is now required, depending on Task 4 decision.
- Active cleanup/dry-run cleanup remains explicitly deferred unless implemented in a separate phase.

- [ ] **Step 4: Commit planning update**

```powershell
git add .planning\STATE.md .planning\ROADMAP.md .planning\REQUIREMENTS.md
git commit -m "docs: record source audit tail remediation"
```

---

## Final Verification Checklist

- [ ] `rg -n "TODO|FIXME|HACK|NotImplementedException|Skip\\s*=" src tests` returns no production TODO/stub markers and no skipped tests.
- [ ] `rg -n "rawErrorExcerpt" src\Apps\CenteralES.Web tests\Integration\WebApiContractTests.cs` shows no Admin response field and only intentional storage/internal references.
- [ ] `git status --short` has no `?? tests/err/`.
- [ ] Build passes with local SDK.
- [ ] Full test suite passes with local SDK.
- [ ] Any Docker Compose runtime validation remains tracked under `DEPLOY-01` until a Docker-capable machine runs `docker compose config/build/up`.
