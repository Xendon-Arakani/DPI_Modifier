param([ValidatePattern('^[0-9A-Za-z][0-9A-Za-z.-]*$')][string]$Version = '0.1.0-progress.2')

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputRoot = Join-Path $projectRoot ('release\' + $Version)
if (Test-Path -LiteralPath $outputRoot) { throw 'RELEASE_EXISTS' }
$projects = @('src\Dpi\DPI.csproj', 'src\Launch\Launch.csproj', 'src\Uninstall\Uninstall.csproj', 'src\Setup\DPI_Modifier_Setup.csproj', 'tests\TestHost\TestHost.csproj')
$seedManifest = Get-Content -LiteralPath (Join-Path $projectRoot 'src\Setup\Dpi.Payload.hashes.json') -Raw | ConvertFrom-Json
foreach ($property in $seedManifest.PSObject.Properties) {
    $filePath = Join-Path $projectRoot ('src\Setup\Dpi.Payload.' + $property.Name)
    if ((Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash -ne $property.Value) { throw 'PAYLOAD_HASH_MISMATCH' }
}
$builds = @()
foreach ($relativeProject in $projects) {
    $projectPath = Join-Path $projectRoot $relativeProject
    $buildOutput = & dotnet build $projectPath -c Release --nologo -p:RestoreLockedMode=true 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'BUILD_FAILED' }
    $builds += @{ name=$relativeProject; status='pass'; exit_code=0 }
}
$testArtifacts = Join-Path $projectRoot 'tests\artifacts'
$runtimeTestPath = Join-Path $testArtifacts 'runtime-tests.json'
if (Test-Path -LiteralPath $runtimeTestPath) {
    $runtimeTests = Get-Content -LiteralPath $runtimeTestPath -Raw | ConvertFrom-Json
    if (@($runtimeTests | Where-Object status -ne 'pass').Count -gt 0) { throw 'RUNTIME_TEST_FAILED' }
}
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$sourceRoot = Join-Path $outputRoot 'source'
New-Item -ItemType Directory -Path $sourceRoot | Out-Null
$sourceFiles = @('global.json','Directory.Build.props','Build-Release.ps1','requirements.json','dependencies.json','provenance.json','release-status.json')
$sourceFiles += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -File -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | ForEach-Object { $_.FullName.Substring($projectRoot.Length + 1) })
$sourceFiles += @('tests\Run-Tests.ps1','tests\Installer-Probe.ps1','tests\Probe-Driver.py','tests\Inspect-PE.py','tests\Test-ManagedContracts.ps1','tests\Test-PackagedWorkflow.ps1','tests\TestHost\Program.cs','tests\TestHost\TestHost.csproj','tests\TestHost\app.manifest','tests\TestHost\packages.lock.json')
foreach ($relativePath in $sourceFiles) {
    $targetPath = Join-Path $sourceRoot $relativePath
    New-Item -ItemType Directory -Path (Split-Path -Parent $targetPath) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot $relativePath) -Destination $targetPath
}
$installerPath = Join-Path $outputRoot 'DPI_Modifier_Setup.exe'
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Setup\bin\Release\net48\DPI_Modifier_Setup.exe') -Destination $installerPath
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\Setup\bin\Release\net48\DPI_Modifier_Setup.exe.config') -Destination ($installerPath + '.config')
foreach ($name in @('requirements.json','dependencies.json','provenance.json','release-status.json')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination (Join-Path $outputRoot $name)
}
$evidenceRoot = Join-Path $outputRoot 'tests'
New-Item -ItemType Directory -Path $evidenceRoot | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $testArtifacts -File -ErrorAction SilentlyContinue) {
    if ($file.Name -notin @('app-inspection.json')) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $evidenceRoot $file.Name)
    }
}
if (!(Test-Path -LiteralPath $runtimeTestPath)) {
    @{name='runtime_tests';status='unverified';code='TEST_ARTIFACTS_UNAVAILABLE'} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidenceRoot 'runtime-tests.json') -Encoding UTF8
}
$builds | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidenceRoot 'build-tests.json') -Encoding UTF8

Add-Type -AssemblyName System.IO.Compression
function Write-FixedZip([string]$Target, [string]$Root, [string[]]$Files) {
    $stream = [IO.File]::Create($Target)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            foreach ($relative in $Files | Sort-Object -CaseSensitive) {
                $entry = $archive.CreateEntry($relative.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
                $inputStream = [IO.File]::OpenRead((Join-Path $Root $relative))
                $outputStream = $entry.Open()
                try { $inputStream.CopyTo($outputStream) } finally { $inputStream.Dispose(); $outputStream.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}
$sourceSnapshot = Join-Path $outputRoot ('DPI_Modifier-' + $Version + '-source.zip')
Write-FixedZip $sourceSnapshot $sourceRoot $sourceFiles
$package = Join-Path $outputRoot ('DPI_Modifier-' + $Version + '-windows-x64.zip')
$packageFiles = @('DPI_Modifier_Setup.exe','DPI_Modifier_Setup.exe.config','requirements.json','dependencies.json','provenance.json','release-status.json')
Write-FixedZip $package $outputRoot $packageFiles
$inventory = @(Get-ChildItem -LiteralPath $outputRoot -File -Recurse | ForEach-Object {
    [ordered]@{ path=$_.FullName.Substring($outputRoot.Length+1).Replace('\','/'); sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes=$_.Length }
})
$inventory | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'hashes.json') -Encoding UTF8
[ordered]@{ version=$Version; source_snapshot=$sourceSnapshot; executable=$installerPath; package=$package } | ConvertTo-Json -Depth 4
