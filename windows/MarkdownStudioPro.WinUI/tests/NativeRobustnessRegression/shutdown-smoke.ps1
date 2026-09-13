param(
    [int]$Cycles = 5,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$exePath = Join-Path $projectRoot 'bin\Debug\net8.0-windows10.0.19041.0\win-x64\Markdown Studio Pro.exe'
$appDataPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Markdown Studio Pro'
$settingsPath = Join-Path $appDataPath 'settings.json'
$recentPath = Join-Path $appDataPath 'recent.json'
$backupPath = Join-Path ([System.IO.Path]::GetTempPath()) ('MSP_Etappe6_' + [guid]::NewGuid().ToString('N'))
$settingsBackupPath = Join-Path $backupPath 'settings.json'
$recentBackupPath = Join-Path $backupPath 'recent.json'
$hadSettings = Test-Path -LiteralPath $settingsPath
$hadRecent = Test-Path -LiteralPath $recentPath
$results = [System.Collections.Generic.List[object]]::new()

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Fresh Debug executable not found: $exePath"
}

$existingApp = Get-Process -Name 'Markdown Studio Pro' -ErrorAction SilentlyContinue
if ($existingApp) {
    throw 'Markdown Studio Pro is already running. Close it before the shutdown regression.'
}

[System.IO.Directory]::CreateDirectory($backupPath) | Out-Null

try {
    [System.IO.Directory]::CreateDirectory($appDataPath) | Out-Null
    if ($hadSettings) {
        [System.IO.File]::Copy($settingsPath, $settingsBackupPath)
    }
    if ($hadRecent) {
        [System.IO.File]::Copy($recentPath, $recentBackupPath)
    }

    $neutralSettings = [ordered]@{
        appearanceVersion = 2
        theme = 'light'
        focusMode = $false
        toolsOpen = $false
        spellcheck = $true
        autoSaveEnabled = $false
        autoSaveIntervalSeconds = 30
        wordGoal = 0
        editorWidth = 848
        fontSize = 20
        lineHeight = 1.45
        windowWidth = 1280
        windowHeight = 860
        windowX = $null
        windowY = $null
        reopenLastDocument = $false
        lastDocumentPath = $null
    }
    [System.IO.File]::WriteAllText(
        $settingsPath,
        ($neutralSettings | ConvertTo-Json -Compress),
        [System.Text.UTF8Encoding]::new($false))

    for ($cycle = 1; $cycle -le $Cycles; $cycle++) {
        $process = Start-Process -FilePath $exePath -PassThru
        $windowDeadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        do {
            Start-Sleep -Milliseconds 100
            $process.Refresh()
        } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $windowDeadline)

        if ($process.HasExited -or $process.MainWindowHandle -eq 0) {
            throw "Cycle $cycle did not produce a main window."
        }

        Start-Sleep -Seconds 2
        $stopwatch = [Diagnostics.Stopwatch]::StartNew()
        $closeSent = $process.CloseMainWindow()
        $exited = $process.WaitForExit($TimeoutSeconds * 1000)
        $stopwatch.Stop()
        $results.Add([pscustomobject]@{
            Cycle = $cycle
            ProcessId = $process.Id
            CloseSent = $closeSent
            Exited = $exited
            ExitMilliseconds = $stopwatch.ElapsedMilliseconds
        })

        if (-not $exited) {
            $confirmed = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
            if ($confirmed -and $confirmed.Path -eq $exePath) {
                Stop-Process -Id $process.Id -Force
                $process.WaitForExit()
            }
            throw "Cycle $cycle exceeded the $TimeoutSeconds second shutdown limit."
        }
    }
}
finally {
    if ($hadSettings) {
        [System.IO.File]::Copy($settingsBackupPath, $settingsPath, $true)
    }
    elseif (Test-Path -LiteralPath $settingsPath) {
        [System.IO.File]::Delete($settingsPath)
    }

    if ($hadRecent) {
        [System.IO.File]::Copy($recentBackupPath, $recentPath, $true)
    }
    elseif (Test-Path -LiteralPath $recentPath) {
        [System.IO.File]::Delete($recentPath)
    }

    if (Test-Path -LiteralPath $settingsBackupPath) {
        [System.IO.File]::Delete($settingsBackupPath)
    }
    if (Test-Path -LiteralPath $recentBackupPath) {
        [System.IO.File]::Delete($recentBackupPath)
    }
    if (Test-Path -LiteralPath $backupPath) {
        [System.IO.Directory]::Delete($backupPath)
    }
}

$results | Format-Table -AutoSize
if ($results.Count -ne $Cycles -or $results.Where({ -not $_.Exited }).Count -ne 0) {
    exit 1
}

Write-Output "$Cycles/$Cycles shutdown cycles passed"
