param([ValidatePattern('^[0-9A-Za-z][0-9A-Za-z.-]*$')][string]$Version = '0.4.0')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$outputRoot = Join-Path $projectRoot ('release\' + $Version)
if (Test-Path -LiteralPath $outputRoot) { throw 'RELEASE_EXISTS' }
$releaseStatus = Get-Content -LiteralPath (Join-Path $projectRoot 'release-status.json') -Raw | ConvertFrom-Json
if ($releaseStatus.version -ne $Version) { throw 'RELEASE_VERSION_MISMATCH' }
Push-Location $projectRoot
try {
    $vendorRoot = Join-Path $projectRoot 'vendor\RawAccel-1.7.1'
    $vendorManifest = Get-Content -LiteralPath (Join-Path $vendorRoot 'manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $vendorManifest.files.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path $vendorRoot $entry.Name)).Hash -ne $entry.Value) { throw 'VENDOR_HASH_MISMATCH' }
    }
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $vendorRoot 'rawaccel.sys')
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*CN=Microsoft Windows Hardware Compatibility Publisher*') { throw 'DRIVER_SIGNATURE_INVALID' }
    $builds = @()
    foreach ($project in @('src\Dpi\DPI.csproj','src\Launch\Launch.csproj','src\Uninstall\Uninstall.csproj')) {
        $log = & dotnet build $project -c Release --nologo -p:RestoreLockedMode=true 2>&1
        if ($LASTEXITCODE -ne 0) { $log | Write-Output; throw 'BUILD_FAILED' }
        $builds += @{name=$project;status='pass'}
    }
    $payloadRoot = Join-Path $projectRoot 'src\Setup\Payload'
    New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
    foreach ($app in @(@{project='Dpi';name='DPI.exe'},@{project='Launch';name='Launch.exe'},@{project='Uninstall';name='Uninstall.exe'})) {
        foreach ($suffix in @('', '.config')) {
            Copy-Item -LiteralPath (Join-Path $projectRoot ('src\' + $app.project + '\bin\Release\net48\' + $app.name + $suffix)) -Destination (Join-Path $payloadRoot ($app.name + $suffix)) -Force
        }
    }
    foreach ($file in @('rawaccel.sys','wrapper.dll','Newtonsoft.Json.dll','Newtonsoft.Json-LICENSE.txt')) {
        Copy-Item -LiteralPath (Join-Path $vendorRoot $file) -Destination (Join-Path $payloadRoot $file) -Force
    }
    Copy-Item -LiteralPath (Join-Path $vendorRoot 'LICENSE') -Destination (Join-Path $payloadRoot 'RawAccel-LICENSE.txt') -Force
    $payloadNames = @('DPI.exe','DPI.exe.config','Launch.exe','Launch.exe.config','Uninstall.exe','Uninstall.exe.config','rawaccel.sys','wrapper.dll','Newtonsoft.Json.dll','RawAccel-LICENSE.txt','Newtonsoft.Json-LICENSE.txt')
    if (@(Get-ChildItem -LiteralPath $payloadRoot -File | Where-Object Name -notin ($payloadNames + 'hashes.json')).Count -ne 0) { throw 'UNEXPECTED_PAYLOAD_FILE' }
    $hashes = [ordered]@{}
    foreach ($file in $payloadNames) { $hashes[$file] = (Get-FileHash -LiteralPath (Join-Path $payloadRoot $file)).Hash }
    $hashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payloadRoot 'hashes.json') -Encoding UTF8
    $log = & dotnet build 'src\Setup\DPI_Modifier_Setup.csproj' -c Release --nologo -p:RestoreLockedMode=true 2>&1
    if ($LASTEXITCODE -ne 0) { $log | Write-Output; throw 'SETUP_BUILD_FAILED' }
    $builds += @{name='src\Setup\DPI_Modifier_Setup.csproj';status='pass'}
    & '.\tests\Run-Tests.ps1'

    New-Item -ItemType Directory -Path $outputRoot | Out-Null
    $installer = Join-Path $outputRoot 'DPI_Modifier_Setup.exe'
    Copy-Item -LiteralPath 'src\Setup\bin\Release\net48\DPI_Modifier_Setup.exe' -Destination $installer
    Copy-Item -LiteralPath 'src\Setup\bin\Release\net48\DPI_Modifier_Setup.exe.config' -Destination ($installer + '.config')
    $verifyRoot = Join-Path $outputRoot 'verification\payload'
    New-Item -ItemType Directory -Path (Split-Path -Parent $verifyRoot) -Force | Out-Null
    $process = Start-Process -FilePath $installer -ArgumentList @('--verify', ('"' + $verifyRoot + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'PACKAGED_PAYLOAD_VERIFICATION_FAILED' }
    foreach ($file in $payloadNames) {
        if ((Get-FileHash -LiteralPath (Join-Path $verifyRoot $file)).Hash -ne $hashes[$file]) { throw 'PACKAGED_PAYLOAD_IDENTITY_FAILED' }
    }
    $evidence = Join-Path $outputRoot 'tests'
    New-Item -ItemType Directory -Path $evidence | Out-Null
    Copy-Item -LiteralPath 'tests\artifacts\safety\safety-tests.json','tests\artifacts\safety\source-audit.json' -Destination $evidence
    $builds | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'build-tests.json') -Encoding UTF8
    @{success=$true;fresh_built_application_embedded=$true;all_payload_hashes_match=$true;driver_authenticode_valid=$true;legacy_driver_bundled=$false;local_certificate_bundled=$false;boot_test_performed=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'package-tests.json') -Encoding UTF8
    foreach ($name in @('requirements.json','dependencies.json','provenance.json','release-status.json')) { Copy-Item -LiteralPath $name -Destination (Join-Path $outputRoot $name) }

    $sourceFiles = @('.gitignore','.gitattributes','global.json','Directory.Build.props','Build-Release.ps1','requirements.json','dependencies.json','provenance.json','release-status.json')
    $sourceFiles += @(Get-ChildItem -LiteralPath 'src','vendor\RawAccel-1.7.1','tests\SafetyTests' -File -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj|Payload)\\' } | ForEach-Object { $_.FullName.Substring($projectRoot.Length+1) })
    $sourceFiles += 'tests\Run-Tests.ps1'
    $sourceRoot = Join-Path $outputRoot 'source'
    foreach ($relative in $sourceFiles) {
        $target = Join-Path $sourceRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $projectRoot $relative) -Destination $target
    }
    Add-Type -AssemblyName System.IO.Compression
    function Write-FixedZip([string]$Target, [string]$Root, [string[]]$Files) {
        $stream = [IO.File]::Create($Target)
        try {
            $zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
            try {
                foreach ($relative in $Files | Sort-Object -CaseSensitive) {
                    $entry = $zip.CreateEntry($relative.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
                    $inputFile = [IO.File]::OpenRead((Join-Path $Root $relative)); $outputFile = $entry.Open()
                    try { $inputFile.CopyTo($outputFile) } finally { $inputFile.Dispose(); $outputFile.Dispose() }
                }
            } finally { $zip.Dispose() }
        } finally { $stream.Dispose() }
    }
    $sourceZip = Join-Path $outputRoot ('DPI_Modifier-' + $Version + '-source.zip')
    Write-FixedZip $sourceZip $sourceRoot $sourceFiles
    $package = Join-Path $outputRoot ('DPI_Modifier-' + $Version + '-windows-x64.zip')
    Write-FixedZip $package $outputRoot @('DPI_Modifier_Setup.exe','DPI_Modifier_Setup.exe.config','requirements.json','dependencies.json','provenance.json','release-status.json')
    $inventory = @(Get-ChildItem -LiteralPath $outputRoot -File -Recurse | ForEach-Object { [ordered]@{path=$_.FullName.Substring($outputRoot.Length+1).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant();bytes=$_.Length} })
    $inventory | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'hashes.json') -Encoding UTF8
    [ordered]@{version=$Version;installer=$installer;package=$package;source=$sourceZip;downloads_locked=$releaseStatus.downloads_locked;publish_allowed=$releaseStatus.publish_allowed;boot_test_performed=$false} | ConvertTo-Json
} finally { Pop-Location }
