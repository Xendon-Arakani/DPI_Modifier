param([Parameter(Mandatory=$true)][string]$PackagePath, [string]$ArtifactRoot = (Join-Path $PSScriptRoot 'artifacts'))

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'ADMIN_REQUIRED' }
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class PackageWindow {
    public delegate bool Callback(IntPtr window, IntPtr parameter);
    [StructLayout(LayoutKind.Sequential)] public struct Rectangle { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, Callback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rectangle rectangle);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint="SendMessageTimeout", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr ReadText(IntPtr window, uint message, IntPtr wparam, StringBuilder lparam, uint flags, uint timeout, out IntPtr result);
    public static IntPtr[] Children(IntPtr parent) {
        var result = new List<IntPtr>();
        EnumChildWindows(parent, (window, parameter) => { result.Add(window); return true; }, IntPtr.Zero);
        return result.ToArray();
    }
    public static string Text(IntPtr window) { var text = new StringBuilder(512); IntPtr result; ReadText(window, 13, new IntPtr(text.Capacity), text, 2, 3000, out result); return text.ToString(); }
    public static string Class(IntPtr window) { var text = new StringBuilder(256); GetClassName(window, text, text.Capacity); return text.ToString(); }
    public static bool Click(IntPtr window) { IntPtr result; return SendMessageTimeout(window, 245, IntPtr.Zero, IntPtr.Zero, 2, 3000, out result) != IntPtr.Zero; }
}
'@
function Assert-Workflow([bool]$Condition, [string]$Code) { if (!$Condition) { throw $Code } }
function Save-Artifact([string]$Name, $Value) { $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $ArtifactRoot $Name) -Encoding UTF8 }
function Read-State { Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json }
function Capture-Workflow([IntPtr]$Window) {
    $rectangle = [PackageWindow+Rectangle]::new()
    Assert-Workflow ([PackageWindow]::GetWindowRect($Window,[ref]$rectangle)) 'CAPTURE_RECT'
    $bitmap = [Drawing.Bitmap]::new($rectangle.Right-$rectangle.Left,$rectangle.Bottom-$rectangle.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $dc = $graphics.GetHdc()
        try { Assert-Workflow ([PackageWindow]::PrintWindow($Window,$dc,2)) 'CAPTURE_FAILED' } finally { $graphics.ReleaseHdc($dc) }
        $bitmap.Save((Join-Path $ArtifactRoot 'packaged-workflow.png'),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
$projectRoot = Split-Path -Parent $PSScriptRoot
$privateRoot = Join-Path $projectRoot '.private'
$archiveRoot = Join-Path $privateRoot ('progress-package-' + [guid]::NewGuid().ToString('N'))
$installedRoot = Join-Path $env:ProgramFiles 'DPI_Modifier'
$probeRoot = Join-Path $installedRoot ('progress-probe-' + [guid]::NewGuid().ToString('N'))
$installedApp = Join-Path $installedRoot 'DPI.exe'
$previousProcesses = @(Get-Process DPI -ErrorAction SilentlyContinue)
foreach ($previousProcess in $previousProcesses) {
    Assert-Workflow ([IO.Path]::GetFullPath($previousProcess.Path) -eq [IO.Path]::GetFullPath($installedApp)) 'UNEXPECTED_DPI_PROCESS'
    $previousProcess.Kill()
    $previousProcess.WaitForExit(5000) | Out-Null
}
$statePath = Join-Path $env:LOCALAPPDATA 'DPI\state.json'
$previousState = if (Test-Path -LiteralPath $statePath) { [IO.File]::ReadAllBytes($statePath) } else { $null }
$process = $null
$results = [Collections.Generic.List[object]]::new()
$output = [ordered]@{workflow_id='manual_rotation_reset';transitions=@();reset_index=$null;restored_state=$false;process_terminated=$false;temporary_install_files_removed=$false;previous_app_running=($previousProcesses.Count -gt 0);hardware_motion_observed=$false;fresh_install_tested=$false;reboot_requested=$false}
New-Item -ItemType Directory -Path $ArtifactRoot,$privateRoot -Force | Out-Null
try {
    Expand-Archive -LiteralPath $PackagePath -DestinationPath $archiveRoot
    $installer = Join-Path $archiveRoot 'DPI_Modifier_Setup.exe'
    $verification = Start-Process -FilePath $installer -ArgumentList ('--verify "' + $probeRoot + '"') -WindowStyle Hidden -PassThru
    Assert-Workflow ($verification.WaitForExit(10000)) 'PAYLOAD_VERIFY_TIMEOUT'
    Assert-Workflow ($verification.ExitCode -eq 0) 'PAYLOAD_VERIFY_FAILED'
    $verified = Get-Content -LiteralPath (Join-Path $probeRoot 'verify.json') -Raw | ConvertFrom-Json
    Assert-Workflow ($verified.Success -and !$verified.ContainsPrivateKey) 'PAYLOAD_INVALID'
    $payloadApp = Join-Path $probeRoot 'DPI.exe'
    $payloadHash = (Get-FileHash -LiteralPath $payloadApp -Algorithm SHA256).Hash.ToLowerInvariant()
    $installedHash = (Get-FileHash -LiteralPath $installedApp -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Workflow ($payloadHash -eq $installedHash) 'INSTALLED_PACKAGE_MISMATCH'
    $driver = Get-CimInstance Win32_SystemDriver -Filter "Name='DpiFilter'"
    Assert-Workflow ($driver.State -eq 'Running') 'DRIVER_NOT_RUNNING'
    $driverPath = $driver.PathName.Replace('\??\','')
    if ($driverPath.StartsWith('\SystemRoot\',[StringComparison]::OrdinalIgnoreCase)) { $driverPath = Join-Path $env:WINDIR $driverPath.Substring(12) }
    $driverHash = (Get-FileHash -LiteralPath $driverPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Workflow ($driverHash -eq $verified.DriverHash) 'INSTALLED_DRIVER_MISMATCH'
    $fixture = [ordered]@{Values=@(1600,1050,2150,1600,1200,2000,1600,1300,1900);Index=-1;BaseDpi=1600;LowerRangeDpi=550;UpperRangeDpi=550;DurationSeconds=105;CountdownSeconds=3;Automatic=$false;EnablePlus=$true}
    Save-Artifact 'packaged-workflow-input.json' ([ordered]@{workflow_id='manual_rotation_reset';package_sha256=(Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant();installer_sha256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant();payload_app_sha256=$payloadHash;installed_app_sha256=$installedHash;driver_sha256=$driverHash;driver_preinstalled=$true;fixture=$fixture;actions=@('next_18','reset')})
    New-Item -ItemType Directory -Path (Split-Path -Parent $statePath) -Force | Out-Null
    $fixture | ConvertTo-Json -Depth 4 -Compress | Set-Content -LiteralPath $statePath -Encoding UTF8
    $entry = Start-Process -FilePath (Join-Path $probeRoot 'Launch.exe') -WorkingDirectory $probeRoot -PassThru
    Assert-Workflow ($entry.WaitForExit(10000)) 'PACKAGE_ENTRY_TIMEOUT'
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $process = Get-Process DPI -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($process) { $process.Refresh() }
        if ($process -and $process.MainWindowHandle -ne 0) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    Assert-Workflow ($null -ne $process -and $process.MainWindowHandle -ne 0) 'APP_WINDOW_UNAVAILABLE'
    Assert-Workflow ([IO.Path]::GetFullPath($process.Path) -eq [IO.Path]::GetFullPath($installedApp)) 'APP_PATH_MISMATCH'
    Assert-Workflow ($process.Responding -and [PackageWindow]::IsWindowVisible($process.MainWindowHandle)) 'APP_NOT_RESPONDING'
    $controls = @([PackageWindow]::Children($process.MainWindowHandle))
    $next = @($controls | Where-Object { [PackageWindow]::Text($_) -like 'NEXT *' })
    $reset = @($controls | Where-Object { [PackageWindow]::Text($_) -like 'RESET *' })
    Assert-Workflow ($next.Count -eq 1 -and $reset.Count -eq 1) 'WORKFLOW_CONTROLS_UNAVAILABLE'
    $results.Add([ordered]@{id='packaged_executable_identity';status='pass';input_artifact='packaged-workflow-input.json';output_artifact='packaged-workflow-output.json';component_id='DPI.exe'})
    for ($step=0; $step -lt 18; $step++) {
        Assert-Workflow ([PackageWindow]::Click($next[0])) 'NEXT_ACTION_TIMEOUT'
        $state = Read-State
        Assert-Workflow ($state.Index -eq ($step % 9)) 'INDEX_MISMATCH'
        Assert-Workflow (@($state.Values | Where-Object { $_ -eq $state.BaseDpi }).Count -eq 3) 'SERIES_COMPOSITION'
        $target = [int]$state.Values[$state.Index]
        Assert-Workflow ($target -ge 1050 -and $target -le 2150) 'TARGET_OUT_OF_RANGE'
        $label = 'ENTRY ' + ($state.Index+1) + ' OF 9'
        Assert-Workflow (@($controls | Where-Object { [PackageWindow]::Text($_) -eq $label }).Count -eq 1) 'DISPLAY_INDEX_MISMATCH'
        Assert-Workflow (@($controls | Where-Object { [PackageWindow]::Class($_) -like '*EDIT*' -and [PackageWindow]::Text($_) -eq [string]$target }).Count -ge 1) 'DISPLAY_TARGET_MISMATCH'
        $output.transitions += [ordered]@{step=$step+1;index=$state.Index;target=$target;baseline=$state.BaseDpi;persistence_verified=$true;display_verified=$true}
    }
    Capture-Workflow $process.MainWindowHandle
    Assert-Workflow ([PackageWindow]::Click($reset[0])) 'RESET_ACTION_TIMEOUT'
    $state = Read-State
    Assert-Workflow ($state.Index -eq -1 -and $state.BaseDpi -eq 1600) 'RESET_STATE_MISMATCH'
    Assert-Workflow (@($controls | Where-Object { [PackageWindow]::Text($_) -eq 'READY' }).Count -eq 1) 'RESET_DISPLAY_MISMATCH'
    $output.reset_index = $state.Index
    $output.reset_target = $state.BaseDpi
    $output.packaged_entry_started = $true
    $output.installed_redirect = $true
    $output.executable_hash_verified = $true
    $results.Add([ordered]@{id='manual_rotation_reset';status='pass';input_artifact='packaged-workflow-input.json';output_artifact='packaged-workflow-output.json';image_artifact='packaged-workflow.png';component_id='DPI.exe'})
} catch {
    $code = if ($_.Exception.Message -cmatch '^[A-Z0-9_]+$') { $_.Exception.Message } else { 'EXCEPTION' }
    $results.Add([ordered]@{id='manual_rotation_reset';status='fail';code=$code;exception=$_.Exception.GetType().FullName})
} finally {
    if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit(5000) | Out-Null; $output.process_terminated = $process.HasExited }
    if ($null -ne $previousState) { [IO.File]::WriteAllBytes($statePath,$previousState) } elseif (Test-Path -LiteralPath $statePath) { Remove-Item -LiteralPath $statePath }
    $output.restored_state = if ($null -eq $previousState) { !(Test-Path -LiteralPath $statePath) } else { [Convert]::ToBase64String([IO.File]::ReadAllBytes($statePath)) -eq [Convert]::ToBase64String($previousState) }
    if (Test-Path -LiteralPath $probeRoot) {
        $resolvedProbe = (Resolve-Path -LiteralPath $probeRoot).Path
        $allowedRoot = [IO.Path]::GetFullPath($installedRoot).TrimEnd('\') + '\'
        Assert-Workflow ($resolvedProbe.StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolvedProbe) -match '^progress-probe-[a-f0-9]{32}$') 'CLEANUP_PATH_REJECTED'
        Remove-Item -LiteralPath $resolvedProbe -Recurse -Force
    }
    $output.temporary_install_files_removed = !(Test-Path -LiteralPath $probeRoot)
    Save-Artifact 'packaged-workflow-output.json' $output
    Save-Artifact 'packaged-workflow-tests.json' @($results)
}
if (@($results | Where-Object status -ne 'pass').Count -gt 0 -or !$output.restored_state -or !$output.temporary_install_files_removed) { exit 1 }
exit 0
