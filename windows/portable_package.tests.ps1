# Behavioral regression for portable WinUI 3 packaging.
# A missing XBF or PRI caused the Release executable to crash on startup.
$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'build_portable_winui3.ps1'
if (-not (Test-Path -LiteralPath $validator -PathType Leaf)) {
    throw "Missing portable-package validator: $validator"
}
$fixture = Join-Path ([System.IO.Path]::GetTempPath()) ('msp-package-contract-' + [Guid]::NewGuid().ToString('N'))
$required = @(
    'Markdown Studio Pro.exe',
    'WebView2Loader.dll',
    'App\editor.html',
    'Assets\markdown_studio_icon.ico',
    'Assets\markdown_studio_icon.png',
    'App.xbf',
    'MainWindow.xbf',
    'Markdown Studio Pro.pri'
)
function Test-PackageValidation([bool] $expectedGood, [string] $case) {
    $previousPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5 turns a child process's expected stderr into a
        # terminating NativeCommandError when ErrorActionPreference is Stop.
        $ErrorActionPreference = 'Continue'
        $output = @(& powershell.exe -NoProfile -NonInteractive -File $validator -VerifyOnly -Directory $fixture 2>&1)
        $nativeCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousPreference
    }
    $actualGood = ($nativeCode -eq 0)
    if ($actualGood -ne $expectedGood) {
        throw "$case : expected valid=$expectedGood, got exit=$nativeCode; $($output | Select-Object -Last 4)"
    }
    if ($expectedGood -and (($output -join [Environment]::NewLine) -notmatch 'PORTABLE_VERIFIED=')) {
        throw "$case : validator did not acknowledge the package"
    }
    Write-Output "PASS $case"
}
try {
    [void](New-Item -ItemType Directory -Path $fixture -ErrorAction Stop)
    foreach ($entry in $required) {
        $file = Join-Path $fixture $entry
        [void](New-Item -ItemType Directory -Path (Split-Path $file) -Force)
        Set-Content -LiteralPath $file -Value 'fixture bytes' -NoNewline
    }
    Test-PackageValidation $true 'all required WinUI files are accepted'
    foreach ($missing in @('MainWindow.xbf', 'App.xbf', 'Markdown Studio Pro.pri')) {
        $file = Join-Path $fixture $missing
        Remove-Item -LiteralPath $file -ErrorAction Stop
        Test-PackageValidation $false "missing $missing is rejected"
        Set-Content -LiteralPath $file -Value 'fixture bytes' -NoNewline
    }
    $empty = Join-Path $fixture 'MainWindow.xbf'
    [System.IO.File]::WriteAllBytes($empty, [byte[]]@())
    Test-PackageValidation $false 'empty XBF is rejected'
    Write-Output 'Portable package regression: 5/5 passed'
}
finally {
    if (Test-Path -LiteralPath $fixture -PathType Container) {
        Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction Stop
    }
}
