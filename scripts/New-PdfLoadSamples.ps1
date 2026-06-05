param(
    [string]$InputPdf = "test.pdf",
    [string]$OutputDirectory = ".codex-local/load-pdfs",
    [int]$Count = 100,
    [int]$MinMarkSize = 2,
    [int]$MaxMarkSize = 3,
    [int]$Seed = 0,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Resolve-PathStrict([string]$Path, [string]$Description) {
    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        throw "$Description was not found: $Path"
    }

    return (Resolve-Path -Path $Path).Path
}

function Format-PdfNumber([double]$Value) {
    return $Value.ToString("0.###", [Globalization.CultureInfo]::InvariantCulture)
}

function Get-LastRegexMatch([string]$Text, [string]$Pattern) {
    $matches = [Regex]::Matches($Text, $Pattern, [Text.RegularExpressions.RegexOptions]::Singleline)
    if ($matches.Count -eq 0) {
        return $null
    }

    return $matches[$matches.Count - 1]
}

function ConvertFrom-Latin1Bytes([byte[]]$Bytes) {
    return [Text.Encoding]::GetEncoding("ISO-8859-1").GetString($Bytes)
}

function ConvertTo-Latin1Bytes([string]$Text) {
    return [Text.Encoding]::GetEncoding("ISO-8859-1").GetBytes($Text)
}

function Expand-FlateData([byte[]]$Bytes) {
    $candidates = New-Object System.Collections.Generic.List[object]
    if ("System.IO.Compression.ZLibStream" -as [type]) {
        $candidates.Add([pscustomobject]@{ Type = "ZLib"; Bytes = $Bytes })
    }

    if ($Bytes.Length -gt 6) {
        $rawLength = $Bytes.Length - 6
        $rawBytes = New-Object byte[] $rawLength
        [Array]::Copy($Bytes, 2, $rawBytes, 0, $rawLength)
        $candidates.Add([pscustomobject]@{ Type = "Deflate"; Bytes = $rawBytes })
    }

    $candidates.Add([pscustomobject]@{ Type = "Deflate"; Bytes = $Bytes })

    for ($candidateIndex = 0; $candidateIndex -lt $candidates.Count; $candidateIndex++) {
        $candidate = $candidates[$candidateIndex]
        $inputStream = [IO.MemoryStream]::new($candidate.Bytes)
        $outputStream = [IO.MemoryStream]::new()
        try {
            if ($candidate.Type -eq "ZLib") {
                $decompressor = [IO.Compression.ZLibStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
            }
            else {
                $decompressor = [IO.Compression.DeflateStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
            }

            try {
                $decompressor.CopyTo($outputStream)
            }
            finally {
                $decompressor.Dispose()
            }

            return $outputStream.ToArray()
        }
        catch {
            if ($candidateIndex -eq ($candidates.Count - 1)) {
                throw "Could not decompress FlateDecode object stream."
            }
        }
        finally {
            $inputStream.Dispose()
            $outputStream.Dispose()
        }
    }
}

function Get-PdfStreamBytes([string]$ObjectBody) {
    $streamIndex = $ObjectBody.IndexOf("stream", [StringComparison]::Ordinal)
    $endStreamIndex = $ObjectBody.LastIndexOf("endstream", [StringComparison]::Ordinal)
    if ($streamIndex -lt 0 -or $endStreamIndex -lt $streamIndex) {
        return $null
    }

    $dataStart = $streamIndex + "stream".Length
    if ($dataStart -lt $ObjectBody.Length -and $ObjectBody[$dataStart] -eq "`r") {
        $dataStart++
    }

    if ($dataStart -lt $ObjectBody.Length -and $ObjectBody[$dataStart] -eq "`n") {
        $dataStart++
    }

    $lengthMatch = [Regex]::Match($ObjectBody, '/Length\s+(?<length>\d+)')
    if ($lengthMatch.Success) {
        $length = [int]$lengthMatch.Groups["length"].Value
        if ($length -gt 0 -and ($dataStart + $length) -le $ObjectBody.Length) {
            return ConvertTo-Latin1Bytes $ObjectBody.Substring($dataStart, $length)
        }
    }

    $dataEnd = $endStreamIndex
    if ($dataEnd -gt $dataStart -and $ObjectBody[$dataEnd - 1] -eq "`n") {
        $dataEnd--
    }

    if ($dataEnd -gt $dataStart -and $ObjectBody[$dataEnd - 1] -eq "`r") {
        $dataEnd--
    }

    return ConvertTo-Latin1Bytes $ObjectBody.Substring($dataStart, $dataEnd - $dataStart)
}

function Get-PdfPageBox([string]$PageObjectBody) {
    $boxMatch = [Regex]::Match(
        $PageObjectBody,
        '/(?:CropBox|MediaBox)\s*\[\s*(?<x0>[-+]?\d+(?:\.\d+)?)\s+(?<y0>[-+]?\d+(?:\.\d+)?)\s+(?<x1>[-+]?\d+(?:\.\d+)?)\s+(?<y1>[-+]?\d+(?:\.\d+)?)\s*\]')

    if (-not $boxMatch.Success) {
        return [pscustomobject]@{
            X0 = 0.0
            Y0 = 0.0
            X1 = 595.0
            Y1 = 842.0
        }
    }

    return [pscustomobject]@{
        X0 = [double]::Parse($boxMatch.Groups["x0"].Value, [Globalization.CultureInfo]::InvariantCulture)
        Y0 = [double]::Parse($boxMatch.Groups["y0"].Value, [Globalization.CultureInfo]::InvariantCulture)
        X1 = [double]::Parse($boxMatch.Groups["x1"].Value, [Globalization.CultureInfo]::InvariantCulture)
        Y1 = [double]::Parse($boxMatch.Groups["y1"].Value, [Globalization.CultureInfo]::InvariantCulture)
    }
}

function Add-ObjectStreamPages(
    [System.Collections.Generic.List[object]]$Pages,
    [string]$ObjectBody,
    [ref]$MaxObjectNumber) {
    if ($ObjectBody -notmatch '/Type\s*/ObjStm' -or $ObjectBody -notmatch '/Filter\s*/FlateDecode') {
        return
    }

    $firstMatch = [Regex]::Match($ObjectBody, '/First\s+(?<first>\d+)')
    $countMatch = [Regex]::Match($ObjectBody, '/N\s+(?<count>\d+)')
    if (-not $firstMatch.Success -or -not $countMatch.Success) {
        return
    }

    $streamBytes = Get-PdfStreamBytes $ObjectBody
    if ($null -eq $streamBytes) {
        return
    }

    $expanded = ConvertFrom-Latin1Bytes (Expand-FlateData $streamBytes)
    $first = [int]$firstMatch.Groups["first"].Value
    $count = [int]$countMatch.Groups["count"].Value
    if ($first -le 0 -or $first -ge $expanded.Length) {
        return
    }

    $header = $expanded.Substring(0, $first)
    $numberMatches = [Regex]::Matches($header, '\d+')
    if ($numberMatches.Count -lt ($count * 2)) {
        return
    }

    $entries = New-Object System.Collections.Generic.List[object]
    for ($index = 0; $index -lt $count; $index++) {
        $objectNumber = [int]$numberMatches[$index * 2].Value
        $offset = [int]$numberMatches[($index * 2) + 1].Value
        $entries.Add([pscustomobject]@{
            ObjectNumber = $objectNumber
            Offset = $offset
        })
        $MaxObjectNumber.Value = [Math]::Max($MaxObjectNumber.Value, $objectNumber)
    }

    for ($index = 0; $index -lt $entries.Count; $index++) {
        $entry = $entries[$index]
        $nextOffset = if ($index + 1 -lt $entries.Count) { $entries[$index + 1].Offset } else { $expanded.Length - $first }
        $length = $nextOffset - $entry.Offset
        if ($length -le 0) {
            continue
        }

        $body = $expanded.Substring($first + $entry.Offset, $length).Trim()
        if ($body -match '/Type\s*/Page(?!s)') {
            $Pages.Add([pscustomobject]@{
                ObjectNumber = $entry.ObjectNumber
                Generation = 0
                Body = $body
            })
        }
    }
}

function Set-PageContentsReference([string]$PageObjectBody, [string]$NewContentReference) {
    $arrayMatch = [Regex]::Match(
        $PageObjectBody,
        '/Contents\s*\[(?<items>.*?)\]',
        [Text.RegularExpressions.RegexOptions]::Singleline)
    if ($arrayMatch.Success) {
        $items = $arrayMatch.Groups["items"].Value.Trim()
        $replacement = "/Contents [$items $NewContentReference]"
        return $PageObjectBody.Remove($arrayMatch.Index, $arrayMatch.Length).Insert($arrayMatch.Index, $replacement)
    }

    $singleMatch = [Regex]::Match($PageObjectBody, '/Contents\s+(?<ref>\d+\s+\d+\s+R)')
    if ($singleMatch.Success) {
        $oldReference = $singleMatch.Groups["ref"].Value
        $replacement = "/Contents [$oldReference $NewContentReference]"
        return $PageObjectBody.Remove($singleMatch.Index, $singleMatch.Length).Insert($singleMatch.Index, $replacement)
    }

    $dictionaryEnd = $PageObjectBody.LastIndexOf(">>", [StringComparison]::Ordinal)
    if ($dictionaryEnd -lt 0) {
        throw "Could not find the end of the selected page dictionary."
    }

    return $PageObjectBody.Insert($dictionaryEnd, " /Contents $NewContentReference ")
}

function Get-PdfMetadata([byte[]]$PdfBytes) {
    $text = ConvertFrom-Latin1Bytes $PdfBytes
    $objectMatches = [Regex]::Matches(
        $text,
        '(?s)(?<number>\d+)\s+(?<generation>\d+)\s+obj\s*(?<body>.*?)\s*endobj')

    if ($objectMatches.Count -eq 0) {
        throw "No PDF objects were found."
    }

    $pages = New-Object System.Collections.Generic.List[object]
    $maxObjectNumber = 0
    $xrefStreamRoot = $null
    foreach ($match in $objectMatches) {
        $objectNumber = [int]$match.Groups["number"].Value
        $generation = [int]$match.Groups["generation"].Value
        $body = $match.Groups["body"].Value
        $maxObjectNumber = [Math]::Max($maxObjectNumber, $objectNumber)

        if ($body -match '/Type\s*/Page(?!s)') {
            $pages.Add([pscustomobject]@{
                ObjectNumber = $objectNumber
                Generation = $generation
                Body = $body
            })
        }

        Add-ObjectStreamPages $pages $body ([ref]$maxObjectNumber)

        if ($body -match '/Type\s*/XRef') {
            $rootInXref = [Regex]::Match($body, '/Root\s+(?<number>\d+)\s+(?<generation>\d+)\s+R')
            if ($rootInXref.Success) {
                $xrefStreamRoot = [pscustomobject]@{
                    ObjectNumber = [int]$rootInXref.Groups["number"].Value
                    Generation = [int]$rootInXref.Groups["generation"].Value
                }
            }
        }
    }

    if ($pages.Count -eq 0) {
        throw "No page objects were found. The PDF may use an unsupported structure."
    }

    $startXrefMatch = Get-LastRegexMatch $text 'startxref\s+(?<offset>\d+)\s*%%EOF'
    if ($null -eq $startXrefMatch) {
        throw "Could not find startxref."
    }

    $rootObjectNumber = $null
    $rootGeneration = $null
    $trailerMatch = Get-LastRegexMatch $text 'trailer\s*<<(.*?)>>\s*startxref'
    if ($null -ne $trailerMatch) {
        $trailer = $trailerMatch.Groups[1].Value
        if ($trailer -match '/Encrypt\b') {
            throw "Encrypted PDFs are not supported."
        }

        $rootMatch = [Regex]::Match($trailer, '/Root\s+(?<number>\d+)\s+(?<generation>\d+)\s+R')
        if ($rootMatch.Success) {
            $rootObjectNumber = [int]$rootMatch.Groups["number"].Value
            $rootGeneration = [int]$rootMatch.Groups["generation"].Value
        }
    }

    if ($null -eq $rootObjectNumber -and $null -ne $xrefStreamRoot) {
        $rootObjectNumber = $xrefStreamRoot.ObjectNumber
        $rootGeneration = $xrefStreamRoot.Generation
    }

    if ($null -eq $rootObjectNumber) {
        throw "Could not find /Root in trailer or XRef stream."
    }

    return [pscustomobject]@{
        Pages = $pages.ToArray()
        MaxObjectNumber = $maxObjectNumber
        PreviousXrefOffset = [int64]$startXrefMatch.Groups["offset"].Value
        RootObjectNumber = $rootObjectNumber
        RootGeneration = $rootGeneration
    }
}

function New-LoadSamplePdf(
    [byte[]]$InputBytes,
    [object]$Metadata,
    [Random]$Random,
    [int]$Index,
    [string]$OutputPath) {
    $page = $Metadata.Pages[$Random.Next(0, $Metadata.Pages.Count)]
    $pageBox = Get-PdfPageBox $page.Body
    $markSize = $Random.Next($MinMarkSize, $MaxMarkSize + 1)
    $offsetX = $Random.Next(-1, 2)
    $offsetY = $Random.Next(-1, 2)
    $centerX = (($pageBox.X0 + $pageBox.X1) / 2.0) + $offsetX
    $centerY = (($pageBox.Y0 + $pageBox.Y1) / 2.0) + $offsetY
    $x = $centerX - ($markSize / 2.0)
    $y = $centerY - ($markSize / 2.0)
    $red = $Random.Next(0, 1000) / 1000.0
    $green = $Random.Next(0, 1000) / 1000.0
    $blue = $Random.Next(0, 1000) / 1000.0

    $contentObjectNumber = $Metadata.MaxObjectNumber + 1
    $contentReference = "$contentObjectNumber 0 R"
    $contentStream = @"
q
$(Format-PdfNumber $red) $(Format-PdfNumber $green) $(Format-PdfNumber $blue) rg
$(Format-PdfNumber $x) $(Format-PdfNumber $y) $(Format-PdfNumber $markSize) $(Format-PdfNumber $markSize) re f
Q
% centerales-load-sample $Index $(New-Guid)
"@
    $contentLength = [Text.Encoding]::ASCII.GetByteCount($contentStream)
    $contentObject = @"
$contentObjectNumber 0 obj
<< /Length $contentLength >>
stream
$contentStream
endstream
endobj
"@

    $updatedPageBody = Set-PageContentsReference $page.Body $contentReference
    $pageObject = @"
$($page.ObjectNumber) $($page.Generation) obj
$updatedPageBody
endobj
"@

    $stream = [IO.MemoryStream]::new()
    try {
        $stream.Write($InputBytes, 0, $InputBytes.Length)
        $ascii = [Text.Encoding]::ASCII

        $pageOffset = $stream.Position
        $pageBytes = $ascii.GetBytes($pageObject)
        $stream.Write($pageBytes, 0, $pageBytes.Length)

        $contentOffset = $stream.Position
        $contentBytes = $ascii.GetBytes($contentObject)
        $stream.Write($contentBytes, 0, $contentBytes.Length)

        $xrefOffset = $stream.Position
        $newSize = $contentObjectNumber + 1
        $xref = @"
xref
$($page.ObjectNumber) 1
$($pageOffset.ToString("0000000000", [Globalization.CultureInfo]::InvariantCulture)) $($page.Generation.ToString("00000", [Globalization.CultureInfo]::InvariantCulture)) n 
$contentObjectNumber 1
$($contentOffset.ToString("0000000000", [Globalization.CultureInfo]::InvariantCulture)) 00000 n 
trailer
<< /Size $newSize /Root $($Metadata.RootObjectNumber) $($Metadata.RootGeneration) R /Prev $($Metadata.PreviousXrefOffset) >>
startxref
$xrefOffset
%%EOF
"@
        $xrefBytes = $ascii.GetBytes($xref)
        $stream.Write($xrefBytes, 0, $xrefBytes.Length)

        [IO.File]::WriteAllBytes($OutputPath, $stream.ToArray())
    }
    finally {
        $stream.Dispose()
    }
}

if ($Count -le 0) {
    throw "Count must be greater than zero."
}

if ($MinMarkSize -le 0 -or $MaxMarkSize -lt $MinMarkSize) {
    throw "Mark size range is invalid."
}

$resolvedInput = Resolve-PathStrict $InputPdf "Input PDF"
$resolvedOutputDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$inputBytes = [IO.File]::ReadAllBytes($resolvedInput)
$metadata = Get-PdfMetadata $inputBytes
$random = if ($Seed -eq 0) { [Random]::new() } else { [Random]::new($Seed) }
$baseName = [IO.Path]::GetFileNameWithoutExtension($resolvedInput)

for ($index = 1; $index -le $Count; $index++) {
    $outputPath = Join-Path $resolvedOutputDirectory ("{0}-load-{1:000}.pdf" -f $baseName, $index)
    if ((Test-Path -Path $outputPath) -and -not $Force) {
        throw "Output file already exists: $outputPath. Use -Force to overwrite."
    }

    New-LoadSamplePdf $inputBytes $metadata $random $index $outputPath
}

Write-Output "Created $Count PDF load samples in $resolvedOutputDirectory"
