param([int[]]$CleanupProcessIds = @())

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$installedApp = Join-Path $env:ProgramFiles 'DPI_Modifier\DPI.exe'
foreach ($processId in $CleanupProcessIds) {
    $testProcess = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($testProcess -and $testProcess.Path -eq $installedApp) {
        Stop-Process -Id $processId -Force
    }
}
$testExe = Join-Path $PSScriptRoot 'TestHost\bin\Release\net48\TestHost.exe'
$artifactRoot = Join-Path $PSScriptRoot 'artifacts'
$testProcess = Start-Process -FilePath $testExe -ArgumentList ('"' + $artifactRoot + '"') -WindowStyle Hidden -Wait -PassThru -RedirectStandardError (Join-Path $projectRoot '.private\test-host.stderr')
@{ exit_code = $testProcess.ExitCode } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactRoot 'test-host-exit.json') -Encoding UTF8
exit $testProcess.ExitCode
