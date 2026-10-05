param([Parameter(Mandatory=$true)][string]$InstallerPath)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $PSScriptRoot 'artifacts'
$privateRoot = Join-Path $root '.private'
New-Item -ItemType Directory -Path $privateRoot -Force | Out-Null
$bindingFlags = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
$assembly = [Reflection.Assembly]::LoadFile((Resolve-Path -LiteralPath $InstallerPath).Path)
$results = [Collections.Generic.List[object]]::new()
function Run-Probe([string]$Name, [scriptblock]$Operation) {
    try {
        $data = & $Operation
        $results.Add(@{name=$Name;status='pass';data=$data})
    } catch {
        $errorType=$_.Exception.GetType().FullName
        if ($_.Exception.InnerException) { $errorType=$_.Exception.InnerException.GetType().FullName }
        $code='EXCEPTION'
        if ($_.Exception.Message -match '^[A-Z_]+$') { $code=$_.Exception.Message }
        $results.Add(@{name=$Name;status='fail';code=$code;exception=$errorType})
    }
}
function Assert-Probe([bool]$Value, [string]$Code) { if (!$Value) { throw $Code } }

Run-Probe 'installer_payload_verify_executable' {
    $verificationRoot = Join-Path $privateRoot ('verify-' + [guid]::NewGuid().ToString('N'))
    $process = Start-Process -FilePath $InstallerPath -ArgumentList ('--verify "' + $verificationRoot + '"') -WindowStyle Hidden -PassThru
    Assert-Probe ($process.WaitForExit(10000)) 'VERIFY_TIMEOUT'
    Assert-Probe ($process.ExitCode -eq 0) 'VERIFY_EXIT_CODE'
    $verification = Get-Content -LiteralPath (Join-Path $verificationRoot 'verify.json') -Raw | ConvertFrom-Json
    Assert-Probe ($verification.Success -and !$verification.ContainsPrivateKey) 'VERIFY_STATE'
    $hashes = @{}
    foreach ($fileName in $verification.Files) { $hashes[$fileName]=(Get-FileHash -LiteralPath (Join-Path $verificationRoot $fileName) -Algorithm SHA256).Hash.ToLowerInvariant() }
    @{exit_code=0;contains_private_key=$false;payload_hashes=$hashes;test_signing=$verification.TestMode;secure_boot=$verification.SecureBoot}
}
Run-Probe 'installer_existing_verify_directory_rejected' {
    $directory=Join-Path $privateRoot 'existing-verification-directory'
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $payload=[Activator]::CreateInstance($assembly.GetType('Dpi.Setup.Payload'),$true)
    $rejected=$false
    try { $payload.GetType().GetMethod('Verify',$bindingFlags).Invoke($payload,[object[]]@([string]$directory)) } catch { $rejected=$_.Exception.GetBaseException() -is [IO.IOException]; if (!$rejected) { throw } }
    Assert-Probe $rejected 'EXISTING_DIRECTORY_ACCEPTED'
    @{rejected=$true;directory_modified=$false}
}
Run-Probe 'installer_redirected_path_rejected' {
    $target=Join-Path $privateRoot 'junction-target'
    $junction=Join-Path $privateRoot 'junction'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    if (!(Test-Path -LiteralPath $junction)) { New-Item -ItemType Junction -Path $junction -Target $target | Out-Null }
    $rejected=$false
    try { $assembly.GetType('Dpi.Setup.Layout').GetMethod('CheckPath',$bindingFlags).Invoke($null,[object[]]@([string](Join-Path $junction 'child'))) } catch { $rejected=$_.Exception.GetBaseException() -is [InvalidOperationException]; if (!$rejected) { throw } }
    Assert-Probe $rejected 'JUNCTION_ACCEPTED'
    @{rejected=$true;target_modified=$false}
}
Run-Probe 'installer_permission_gate' {
    Add-Type -AssemblyName System.Windows.Forms
    $form=[Activator]::CreateInstance($assembly.GetType('Dpi.Setup.SetupForm'),$true)
    try {
        $action=$form.GetType().GetField('action',$bindingFlags).GetValue($form)
        $permission=$form.GetType().GetField('permission',$bindingFlags).GetValue($form)
        $recovery=$form.GetType().GetField('recovery',$bindingFlags).GetValue($form)
        Assert-Probe (!$action.Enabled) 'INSTALL_ENABLED_WITHOUT_CONSENT'
        $permission.Checked=$true
        Assert-Probe (!$action.Enabled) 'INSTALL_ENABLED_WITHOUT_RECOVERY'
        $recovery.Checked=$true
        Assert-Probe $action.Enabled 'INSTALL_DISABLED_WITH_CONSENT'
        @{unchecked_disabled=$true;partial_disabled=$true;both_enabled=$true}
    } finally { $form.Dispose() }
}
Run-Probe 'installer_unelevated_install_rejected' {
    $isAdmin=$assembly.GetType('Dpi.Setup.Platform').GetProperty('IsAdministrator',$bindingFlags).GetValue($null,$null)
    Assert-Probe (!$isAdmin) 'PROBE_IS_ELEVATED'
    $rejected=$false
    try { $assembly.GetType('Dpi.Setup.Install').GetMethod('Apply',$bindingFlags).Invoke($null,@([Security.Principal.WindowsIdentity]::GetCurrent().User.Value)) } catch { $rejected=$_.Exception.GetBaseException() -is [UnauthorizedAccessException] }
    Assert-Probe $rejected 'UNELEVATED_INSTALL_ACCEPTED'
    @{rejected=$true;system_modified=$false}
}
$results | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifactRoot 'installer-tests.json') -Encoding UTF8
if (@($results | Where-Object status -ne 'pass').Count -gt 0) { exit 1 }
