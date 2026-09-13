param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$diagnosticsDir = Join-Path $env:LOCALAPPDATA 'Markdown Studio Pro\Diagnostics'

if (-not (Test-Path -LiteralPath $diagnosticsDir)) {
    throw "No Markdown Studio Pro diagnostics directory was found at %LOCALAPPDATA%\Markdown Studio Pro\Diagnostics."
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    $OutputPath = Join-Path $desktop "MarkdownStudioPro-Diagnostics-$timestamp.zip"
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "MarkdownStudioPro-Diagnostics-$timestamp-$PID"
New-Item -ItemType Directory -Path $staging -Force | Out-Null

function Protect-Text([string]$Text) {
    if ([string]::IsNullOrEmpty($Text)) {
        return $Text
    }

    $result = $Text
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $result = [regex]::Replace(
            $result,
            [regex]::Escape($env:USERPROFILE),
            '%USERPROFILE%',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:USERNAME)) {
        $result = [regex]::Replace(
            $result,
            [regex]::Escape($env:USERNAME),
            '%USERNAME%',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    }

    # Never include explicit Markdown/text document paths in a support archive.
    $result = [regex]::Replace(
        $result,
        '(?i)[A-Z]:\\[^\r\n\"]+\.(md|markdown|txt)',
        '%DOCUMENT_PATH%')

    return $result
}

try {
    $logTarget = Join-Path $staging 'logs'
    New-Item -ItemType Directory -Path $logTarget -Force | Out-Null

    Get-ChildItem -Path $diagnosticsDir -File -Filter 'startup-*.log' |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 12 |
        ForEach-Object {
            $content = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
            $safeContent = Protect-Text $content
            Set-Content -LiteralPath (Join-Path $logTarget $_.Name) -Value $safeContent -Encoding UTF8
        }

    $systemLines = @(
        "Collected: $([DateTimeOffset]::Now.ToString('O'))",
        "OS: $([Environment]::OSVersion.VersionString)",
        "64-bit OS: $([Environment]::Is64BitOperatingSystem)",
        "64-bit collector process: $([Environment]::Is64BitProcess)",
        "PowerShell: $($PSVersionTable.PSVersion)"
    )
    Set-Content -LiteralPath (Join-Path $staging 'system.txt') -Value $systemLines -Encoding UTF8

    $eventTarget = Join-Path $staging 'windows-application-events.txt'
    try {
        $events = Get-WinEvent -FilterHashtable @{
            LogName = 'Application'
            StartTime = (Get-Date).AddDays(-2)
        } -ErrorAction Stop |
            Where-Object {
                $_.ProviderName -in @('Application Error', 'Windows Error Reporting') -and
                $_.Message -match 'Markdown Studio Pro|MarkdownStudioPro'
            } |
            Select-Object -First 20

        if ($events) {
            $eventText = foreach ($event in $events) {
                "[$($event.TimeCreated.ToString('O'))] Provider=$($event.ProviderName) Id=$($event.Id)`r`n$(Protect-Text $event.Message)`r`n"
            }
            Set-Content -LiteralPath $eventTarget -Value $eventText -Encoding UTF8
        } else {
            Set-Content -LiteralPath $eventTarget -Value 'No matching Application Error or Windows Error Reporting events were found in the last 48 hours.' -Encoding UTF8
        }
    }
    catch {
        Set-Content -LiteralPath $eventTarget -Value ("Windows event collection failed: " + (Protect-Text $_.Exception.Message)) -Encoding UTF8
    }

    $readme = @(
        'Markdown Studio Pro diagnostics package',
        '',
        'Contents:',
        '- startup logs from %LOCALAPPDATA%\Markdown Studio Pro\Diagnostics',
        '- minimal OS/PowerShell metadata',
        '- process-specific Application Error / Windows Error Reporting events when available',
        '',
        'Not collected:',
        '- Markdown document contents',
        '- recent-file lists',
        '- application settings',
        '- arbitrary files from the user profile'
    )
    Set-Content -LiteralPath (Join-Path $staging 'README.txt') -Value $readme -Encoding UTF8

    if (Test-Path -LiteralPath $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Force
    }

    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $OutputPath -CompressionLevel Optimal
    Write-Host "Diagnostics package created: $OutputPath"
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}
