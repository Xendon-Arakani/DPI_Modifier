param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'))

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$flags = [Reflection.BindingFlags]'Static,Instance,Public,NonPublic'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$original = [Reflection.Assembly]::LoadFile((Join-Path $projectRoot 'src\Setup\Dpi.Payload.DPI.exe'))
$recovered = [Reflection.Assembly]::LoadFile((Join-Path $projectRoot 'src\Dpi\bin\Release\net48\DPI.next.exe'))
$removal = [Reflection.Assembly]::LoadFile((Join-Path $projectRoot 'src\Setup\Dpi.Payload.Uninstall.exe'))
$results = [Collections.Generic.List[object]]::new()

function Assert-Contract([bool]$Condition, [string]$Code) {
    if (!$Condition) { throw $Code }
}

function Test-Contract([string]$Name, [scriptblock]$Test) {
    try {
        $data = & $Test
        $results.Add([ordered]@{name=$Name;status='pass';data=$data})
    } catch {
        $errorObject = $_.Exception.GetBaseException()
        $code = 'EXCEPTION'
        if ($errorObject.Message -match '^[A-Z_]+$') { $code = $errorObject.Message }
        $results.Add([ordered]@{name=$Name;status='fail';code=$code;exception=$errorObject.GetType().FullName})
    }
}

function Invoke-Contract($Target, [string]$Name, [object[]]$Arguments) {
    return $Target.GetType().GetMethod($Name,$flags).Invoke($Target,$Arguments)
}

Test-Contract 'managed_source_distinct_assemblies' {
    Assert-Contract ($original.Location -ne $recovered.Location) 'ASSEMBLY_LOCATIONS_IDENTICAL'
    Assert-Contract ($original -ne $recovered) 'ASSEMBLY_BINDING_IDENTICAL'
    @{distinct=$true;original_sha256=(Get-FileHash -LiteralPath $original.Location -Algorithm SHA256).Hash.ToLowerInvariant();recovered_sha256=(Get-FileHash -LiteralPath $recovered.Location -Algorithm SHA256).Hash.ToLowerInvariant()}
}

Test-Contract 'managed_configuration_contract' {
    $originalType = $original.GetType('Dpi.SeriesSource')
    $recoveredType = $recovered.GetType('Dpi.SeriesSource')
    $count = 0
    foreach ($baseline in @(-1,0,1,800,1600,9999,10000)) {
        foreach ($lower in @(-1,0,1,550,9999)) {
            foreach ($upper in @(-1,0,1,550,9999)) {
                $arguments = [object[]]@([int]$baseline,[int]$lower,[int]$upper)
                $left = $originalType.GetMethod('IsConfigurationValid',$flags).Invoke($null,$arguments)
                $right = $recoveredType.GetMethod('IsConfigurationValid',$flags).Invoke($null,$arguments)
                Assert-Contract ($left -eq $right) 'CONFIGURATION_CONTRACT_MISMATCH'
                $count++
            }
        }
    }
    @{cases=$count;equivalent=$true}
}

Test-Contract 'managed_timing_contract' {
    $originalType = $original.GetType('Dpi.SeriesSource')
    $recoveredType = $recovered.GetType('Dpi.SeriesSource')
    $count = 0
    foreach ($duration in @(-1,0,1,105,86400,86401,[int]::MaxValue)) {
        foreach ($countdown in @(-1,0,1,3,105,86400,86401)) {
            $arguments = [object[]]@([int]$duration,[int]$countdown)
            $left = $originalType.GetMethod('IsTimingConfigurationValid',$flags).Invoke($null,$arguments)
            $right = $recoveredType.GetMethod('IsTimingConfigurationValid',$flags).Invoke($null,$arguments)
            Assert-Contract ($left -eq $right) 'TIMING_CONTRACT_MISMATCH'
            $count++
        }
    }
    @{cases=$count;equivalent=$true}
}

Test-Contract 'managed_deadline_contract' {
    $left = [Activator]::CreateInstance($original.GetType('Dpi.DeadlineClock'),$true)
    $right = [Activator]::CreateInstance($recovered.GetType('Dpi.DeadlineClock'),$true)
    foreach ($clock in @($left,$right)) { Invoke-Contract $clock 'Start' ([object[]]@(105000,[long]100,$true)) | Out-Null }
    $count = 0
    foreach ($now in @([long]100,[long]200,[long]103000,[long]104999,[long]105100,[long]200000)) {
        foreach ($method in @('Remaining','NextDelay')) {
            $arguments = if ($method -eq 'Remaining') { [object[]]@($now) } else { [object[]]@(3000,$now) }
            Assert-Contract ((Invoke-Contract $left $method $arguments) -eq (Invoke-Contract $right $method $arguments)) 'DEADLINE_CONTRACT_MISMATCH'
            $count++
        }
    }
    foreach ($clock in @($left,$right)) {
        Invoke-Contract $clock 'Pause' ([object[]]@([long]200)) | Out-Null
        Invoke-Contract $clock 'Resume' ([object[]]@([long]10000)) | Out-Null
    }
    Assert-Contract ((Invoke-Contract $left 'Remaining' ([object[]]@([long]10200))) -eq (Invoke-Contract $right 'Remaining' ([object[]]@([long]10200)))) 'DEADLINE_RESUME_MISMATCH'
    @{cases=$count+1;equivalent=$true}
}

Test-Contract 'driver_handle_failure_recovery' {
    $controllerType = $original.GetType('Dpi.KernelMouseController')
    $controller = [Activator]::CreateInstance($controllerType,$true)
    $handleField = $controllerType.GetField('handle',$flags)
    try {
        $handleField.SetValue($controller,[Microsoft.Win32.SafeHandles.SafeFileHandle]::new([IntPtr]::Zero,$false))
        $accepted = Invoke-Contract $controller 'Apply' ([object[]]@(1600,1600))
        Assert-Contract (!$accepted) 'INVALID_HANDLE_ACCEPTED'
        Assert-Contract ($null -eq $handleField.GetValue($controller)) 'INVALID_HANDLE_RETAINED'
        $accepted = Invoke-Contract $controller 'Apply' ([object[]]@(1600,1600))
        Assert-Contract $accepted 'HANDLE_RECOVERY_FAILED'
        $controller.Dispose()
        Assert-Contract ($null -eq $handleField.GetValue($controller)) 'DISPOSED_HANDLE_RETAINED'
        $accepted = Invoke-Contract $controller 'Apply' ([object[]]@(1600,1600))
        Assert-Contract $accepted 'DISPOSE_REOPEN_FAILED'
        @{failure_injected=$true;failure_rejected=$true;handle_cleared=$true;reopened=$true;restored_target=1600}
    } finally { $controller.Dispose() }
}

Test-Contract 'removal_unelevated_rejected' {
    $administrator = $removal.GetType('Dpi.Setup.Platform').GetProperty('IsAdministrator',$flags).GetValue($null,$null)
    Assert-Contract (!$administrator) 'PROBE_IS_ELEVATED'
    $rejected = $false
    try { $removal.GetType('Dpi.Setup.Remove').GetMethod('Apply',$flags).Invoke($null,[object[]]@($false)) | Out-Null }
    catch { $rejected = $_.Exception.GetBaseException() -is [UnauthorizedAccessException]; if (!$rejected) { throw } }
    Assert-Contract $rejected 'REMOVAL_WITHOUT_ADMIN_ACCEPTED'
    @{rejected=$true;system_modified=$false;reboot_requested=$false}
}

Test-Contract 'mouse_filter_list_contract' {
    $assembly = [Reflection.Assembly]::LoadFile((Join-Path $projectRoot 'src\Setup\bin\Release\net48\DPI_Modifier_Setup.exe'))
    $layout = $assembly.GetType('Dpi.Setup.Layout')
    $originalFilters = [string[]]@('other_filter','mouclass','last_filter')
    $added = $layout.GetMethod('AddFilter',$flags).Invoke($null,[object[]]@(,$originalFilters))
    Assert-Contract (($added -join '|') -eq 'other_filter|DpiFilter|mouclass|last_filter') 'FILTER_ORDER_CHANGED'
    $removed = $layout.GetMethod('RemoveFilter',$flags).Invoke($null,[object[]]@(,[string[]]$added))
    Assert-Contract (($removed -join '|') -eq ($originalFilters -join '|')) 'OTHER_FILTERS_REMOVED'
    @{order_preserved=$true;unrelated_filters_preserved=$true;registry_modified=$false}
}

$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'managed-contract-tests.json') -Encoding UTF8
if (@($results | Where-Object status -ne 'pass').Count -gt 0) { exit 1 }
