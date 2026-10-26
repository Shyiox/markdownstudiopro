<#
Build and validate the complete WinUI 3 self-contained Release directory.
Do NOT use "dotnet publish -o" unverified: it can omit generated .xbf/.pri assets,
causing MainWindow.InitializeComponent to fail on Windows startup.
Usage:
  powershell -NoProfile -File .\build_portable_winui3.ps1
  powershell -NoProfile -File .\build_portable_winui3.ps1 -VerifyOnly -Directory "C:\path\to\portable"
#>
[CmdletBinding()]
param(
    [switch] $VerifyOnly,
    [string] $Directory
)
$ErrorActionPreference = 'Stop'
function Assert-MspPortableDirectory([string] $root) {
    if ([string]::IsNullOrWhiteSpace($root) -or
        -not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Portable directory not found: $root"
    }
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
    foreach ($relative in $required) {
        $file = Join-Path $root $relative
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
            throw "Incomplete WinUI package; missing: $relative"
        }
        if ((Get-Item -LiteralPath $file -ErrorAction Stop).Length -eq 0) {
            throw "Incomplete WinUI package; empty: $relative"
        }
    }
    Write-Output "PORTABLE_VERIFIED=$root"
}
if ($VerifyOnly) {
    Assert-MspPortableDirectory $Directory
    exit 0
}
if (-not [string]::IsNullOrWhiteSpace($Directory)) {
    throw '-Directory requires -VerifyOnly; builds always use the project Release output.'
}
$project = Join-Path $PSScriptRoot 'MarkdownStudioPro.WinUI\MarkdownStudioPro.WinUI.csproj'
$buildRoot = Join-Path $PSScriptRoot 'MarkdownStudioPro.WinUI\bin\Release\net10.0-windows10.0.19041.0\win-x64'
& dotnet build $project -c Release -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw "Release build failed with exit code $LASTEXITCODE"
}
Assert-MspPortableDirectory $buildRoot
Write-Output "PORTABLE_SOURCE=$buildRoot"
