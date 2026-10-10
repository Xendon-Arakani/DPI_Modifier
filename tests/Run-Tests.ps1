param([string]$Output = (Join-Path $PSScriptRoot 'artifacts\safety'))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $buildOutput = & dotnet build 'tests\SafetyTests\SafetyTests.csproj' -c Release --nologo -p:RestoreLockedMode=true 2>&1
    if ($LASTEXITCODE -ne 0) { $buildOutput | Write-Output; throw 'SAFETY_TEST_BUILD_FAILED' }
    & '.\tests\SafetyTests\bin\Release\net48\SafetyTests.exe' $Output 'vendor\RawAccel-1.7.1\rawaccel.sys'
    if ($LASTEXITCODE -ne 0) { throw 'SAFETY_TESTS_FAILED' }
    $sources = @(Get-ChildItem -LiteralPath 'src' -Filter '*.cs' -Recurse | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
    foreach ($source in $sources) {
        $text = Get-Content -LiteralPath $source.FullName -Raw
        if ($text -match '(?i)bcdedit|SetTestMode|AddRoot\s*\(|Store\.Add\s*\(') { throw ('BOOT_OR_CERTIFICATE_MUTATION_FOUND: ' + $source.Name) }
    }
    $vendorManifest = Get-Content -LiteralPath 'vendor\RawAccel-1.7.1\manifest.json' -Raw | ConvertFrom-Json
    foreach ($entry in $vendorManifest.files.PSObject.Properties) {
        if ((Get-FileHash -LiteralPath (Join-Path 'vendor\RawAccel-1.7.1' $entry.Name)).Hash -ne $entry.Value) { throw 'VENDOR_HASH_MISMATCH' }
    }
    @{ success=$true; no_boot_policy_mutation=$true; no_new_root_certificate_trust=$true; vendor_hashes_verified=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'source-audit.json') -Encoding UTF8
    Write-Output 'PASS: source safety audit and pinned dependency hashes.'
} finally { Pop-Location }
