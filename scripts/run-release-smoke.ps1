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

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Net.Http

function Resolve-PathStrict([string]$Path, [string]$Description) {
    if (-not (Test-Path -Path $Path)) {
        throw "$Description was not found: $Path"
    }

    return (Resolve-Path -Path $Path).Path
}

function Get-EnvFileValue([string]$Path, [string]$Name, [string]$DefaultValue) {
    if (-not (Test-Path -Path $Path)) {
        return $DefaultValue
    }

    $line = Get-Content -Path $Path |
        Where-Object { $_ -match "^\s*$([Regex]::Escape($Name))\s*=" } |
        Select-Object -First 1
    if (-not $line) {
        return $DefaultValue
    }

    return (($line -split "=", 2)[1]).Trim().Trim('"').Trim("'")
}

function Invoke-Compose([string[]]$Arguments) {
    & docker compose @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose command failed: docker compose $($Arguments -join ' ')"
    }
}

function Wait-HttpOk([string]$Uri, [string]$Name, [DateTimeOffset]$Deadline) {
    do {
        try {
            $response = Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Output "$Name OK"
                return
            }
        }
        catch {
            Start-Sleep -Seconds 2
        }
    } while ([DateTimeOffset]::Now -lt $Deadline)

    throw "Timed out waiting for $Name at $Uri"
}

function ConvertFrom-JsonSafe([string]$Json) {
    if ([string]::IsNullOrWhiteSpace($Json)) {
        return $null
    }

    try {
        return $Json | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Send-PdfUpload(
    [System.Net.Http.HttpClient]$Client,
    [string]$BaseUrl,
    [string]$ResolvedPdfPath,
    [string]$KeyId,
    [string]$Secret) {
    $content = [System.Net.Http.MultipartFormDataContent]::new()
    try {
        $fileBytes = [System.IO.File]::ReadAllBytes($ResolvedPdfPath)
        $fileContent = [System.Net.Http.ByteArrayContent]::new($fileBytes)
        $fileContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse("application/pdf")
        $content.Add($fileContent, "file", [System.IO.Path]::GetFileName($ResolvedPdfPath))

        $request = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::Post,
            "$BaseUrl/api/pdf-stamp-recognition/jobs")
        $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("ApiKey", "$KeyId.$Secret")
        $request.Content = $content

        return $Client.SendAsync($request).GetAwaiter().GetResult()
    }
    catch {
        $content.Dispose()
        throw
    }
}

function Send-AuthorizedGet(
    [System.Net.Http.HttpClient]$Client,
    [string]$Uri,
    [string]$KeyId,
    [string]$Secret) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $Uri)
    $request.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new("ApiKey", "$KeyId.$Secret")
    return $Client.SendAsync($request).GetAwaiter().GetResult()
}

if ([string]::IsNullOrWhiteSpace($ApiKeyId) -or [string]::IsNullOrWhiteSpace($ApiKeySecret)) {
    throw "ApiKeyId and ApiKeySecret are required."
}

$resolvedEnvFile = Resolve-PathStrict $EnvFile "Environment file"
$resolvedPdfPath = Resolve-PathStrict $PdfPath "PDF file"
$webPort = Get-EnvFileValue $resolvedEnvFile "CENTERALES_WEB_PORT" "8080"
$baseUrl = "http://127.0.0.1:$webPort"
$deadline = [DateTimeOffset]::Now.AddSeconds($TimeoutSeconds)
$composeArgs = @(
    "--env-file", $resolvedEnvFile,
    "-p", $ProjectName,
    "-f", "compose.yaml",
    "-f", "compose.prod.yaml"
)

$started = $false

try {
    Write-Output "Validating production Compose configuration..."
    Invoke-Compose ($composeArgs + @("config", "--quiet"))

    if (-not $SkipBuild) {
        Write-Output "Building release images..."
        Invoke-Compose ($composeArgs + @("build"))
    }

    Write-Output "Starting release stack..."
    Invoke-Compose ($composeArgs + @("up", "-d"))
    $started = $true

    Wait-HttpOk "$baseUrl/health/live" "live health" $deadline
    Wait-HttpOk "$baseUrl/health/ready" "ready health" $deadline

    $client = [System.Net.Http.HttpClient]::new()
    try {
        $client.Timeout = [TimeSpan]::FromSeconds(60)

        Write-Output "Uploading PDF through Public API..."
        $uploadResponse = Send-PdfUpload $client $baseUrl $resolvedPdfPath $ApiKeyId $ApiKeySecret
        $uploadBody = $uploadResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        $uploadStatus = [int]$uploadResponse.StatusCode
        if ($uploadStatus -ne 200 -and $uploadStatus -ne 202) {
            $errorJson = ConvertFrom-JsonSafe $uploadBody
            $errorCode = if ($errorJson -and $errorJson.error) { $errorJson.error.code } else { "unknown" }
            throw "Upload failed with HTTP $uploadStatus, error=$errorCode"
        }

        $uploadJson = ConvertFrom-JsonSafe $uploadBody
        if (-not $uploadJson -or -not $uploadJson.hash) {
            throw "Upload response did not include a hash."
        }

        if ($uploadStatus -eq 200) {
            if ($uploadJson.result -and $uploadJson.result.source -eq "fake-pdf2txt") {
                throw "Smoke failed: result source is fake-pdf2txt."
            }

            Write-Output "SMOKE_OK_RELEASE hash=$($uploadJson.hash) job=$($uploadJson.jobId) status=$($uploadJson.status) contract=$($uploadJson.contractVersion)"
            return
        }

        $hash = $uploadJson.hash
        $jobId = $uploadJson.jobId
        $lastPublicStatus = $uploadJson.status

        Write-Output "Polling result..."
        do {
            Start-Sleep -Seconds 2
            $resultResponse = Send-AuthorizedGet $client "$baseUrl/api/pdf-stamp-recognition/results/$hash" $ApiKeyId $ApiKeySecret
            $resultBody = $resultResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $resultStatus = [int]$resultResponse.StatusCode

            if ($resultStatus -eq 200) {
                $resultJson = ConvertFrom-JsonSafe $resultBody
                if (-not $resultJson) {
                    throw "Result response was not valid JSON."
                }

                if ($resultJson.result -and $resultJson.result.source -eq "fake-pdf2txt") {
                    throw "Smoke failed: result source is fake-pdf2txt."
                }

                if ($resultJson.status -ne "completed") {
                    throw "Result returned HTTP 200 but status was '$($resultJson.status)'."
                }

                if ($resultJson.contractVersion -ne "pdf2txt-recognize-json-v1") {
                    throw "Unexpected contract version '$($resultJson.contractVersion)'."
                }

                Write-Output "SMOKE_OK_RELEASE hash=$hash job=$($resultJson.jobId) status=$($resultJson.status) contract=$($resultJson.contractVersion)"
                return
            }

            if ($resultStatus -eq 202) {
                $pendingJson = ConvertFrom-JsonSafe $resultBody
                if ($pendingJson -and $pendingJson.status) {
                    $lastPublicStatus = $pendingJson.status
                }
                continue
            }

            $jobSummary = "unknown"
            if ($jobId) {
                $jobResponse = Send-AuthorizedGet $client "$baseUrl/api/jobs/$jobId" $ApiKeyId $ApiKeySecret
                $jobBody = $jobResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                $jobJson = ConvertFrom-JsonSafe $jobBody
                if ($jobJson -and $jobJson.status) {
                    $jobSummary = $jobJson.status
                }
            }

            throw "Result polling failed with HTTP $resultStatus, jobStatus=$jobSummary"
        } while ([DateTimeOffset]::Now -lt $deadline)

        throw "Timed out waiting for completed result. Last public status=$lastPublicStatus hash=$hash job=$jobId"
    }
    finally {
        if ($client) {
            $client.Dispose()
        }
    }
}
finally {
    if ($started -and -not $KeepRunning) {
        Write-Output "Stopping release stack..."
        Invoke-Compose ($composeArgs + @("down"))
    }
}
