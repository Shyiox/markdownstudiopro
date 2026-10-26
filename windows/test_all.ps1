param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot 'MarkdownStudioPro.WinUI')
function Invoke-Check([string]$Label, [string]$Executable, [string[]]$Arguments) {
    Write-Host "==> $Label"
    $lines = @(& $Executable @Arguments 2>&1)
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        $lines | Select-Object -Last 50 | ForEach-Object { Write-Host $_ }
        throw "$Label failed (exit $code)"
    }
    $last = $lines | Select-Object -Last 1
    Write-Host "PASS $Label : $last"
}
foreach ($suite in @('roundtrip','interaction','stage5','stage7','performance','extensions')) {
    Invoke-Check $suite 'node' @("tests/$suite.test.mjs")
}
foreach ($name in @('SaveSessionRegression','NativeRobustnessRegression','AssetRegression','DocumentFileWriterRegression','WebViewSecurityRegression')) {
    Invoke-Check $name 'dotnet' @('run','--project',"tests/$name/$name.csproj",'-c','Debug')
}
if (-not $SkipBuild) {
    Invoke-Check 'Windows win-x64 build' 'dotnet' @('build','MarkdownStudioPro.WinUI.csproj','-c','Debug','-r','win-x64')
}
Write-Host 'Alle angeforderten automatisierten Tests erfolgreich.'
