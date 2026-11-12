param(
  [Parameter(Mandatory=$true)][int]$RootProcessId,
  [Parameter(Mandatory=$true)][string]$OutputFile,
  [switch]$RequireCleanAutomation
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'measurement-context.ps1')
$ownerSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$rootProcess = Get-Process -Id $RootProcessId
$rootStart = $rootProcess.StartTime.ToUniversalTime().Ticks
$logical = [Environment]::ProcessorCount
$initialContext = Get-MeasurementContext -Processes @(Get-CimInstance Win32_Process) -SessionId $rootProcess.SessionId
if ($RequireCleanAutomation) { Assert-CleanMeasurementContext $initialContext }
function Snapshot {
  $rootProcess.Refresh()
  if ($rootProcess.HasExited -or $rootProcess.StartTime.ToUniversalTime().Ticks -ne $rootStart) { throw 'Observed process ended' }
  $inventory = @(Get-CimInstance Win32_Process)
  $ids = [Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($RootProcessId)
  do {
    $added = $false
    foreach ($row in $inventory) { if ($ids.Contains([int]$row.ParentProcessId) -and $ids.Add([int]$row.ProcessId)) { $added=$true } }
  } while ($added)
  $rows = @(foreach ($row in $inventory) {
    if (-not $ids.Contains([int]$row.ProcessId)) { continue }
    $proc = Get-Process -Id $row.ProcessId -ErrorAction Stop
    $sid = (Invoke-CimMethod -InputObject $row -MethodName GetOwnerSid).Sid
    if ($sid -ne $ownerSid -or [math]::Abs(($proc.StartTime.ToUniversalTime()-$row.CreationDate.ToUniversalTime()).TotalSeconds) -gt 1) {
      throw "Process identity changed: PID $($row.ProcessId), name $($row.Name), owner $sid, expected owner $ownerSid, creation $($row.CreationDate.ToUniversalTime().ToString('o')), process start $($proc.StartTime.ToUniversalTime().ToString('o'))"
    }
    [pscustomobject]@{pid=$proc.Id;ppid=$row.ParentProcessId;name=$row.Name;session=$row.SessionId;ownerSid=$sid;startTicks=$proc.StartTime.ToUniversalTime().Ticks;cpuSeconds=$proc.TotalProcessorTime.TotalSeconds;workingSet=$proc.WorkingSet64;privateBytes=$proc.PrivateMemorySize64}
  })
  [pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');processes=$rows;measurementContext=(Get-MeasurementContext -Processes $inventory -SessionId $rootProcess.SessionId);workingSetMiB=[math]::Round(($rows | Measure-Object workingSet -Sum).Sum/1MB,1);privateMiB=[math]::Round(($rows | Measure-Object privateBytes -Sum).Sum/1MB,1)}
}
Start-Sleep -Seconds 30
$first=Snapshot
$clock=[Diagnostics.Stopwatch]::StartNew()
Start-Sleep -Seconds 10
$last=Snapshot
$clock.Stop()
$delta=0.0; $changed=@()
foreach ($p in $last.processes) {
  $prior=@($first.processes | Where-Object { $_.pid -eq $p.pid -and $_.startTicks -eq $p.startTicks })
  if ($prior.Count -ne 1) { $changed+='new:'+$p.pid; continue }
  $delta+=$p.cpuSeconds-$prior[0].cpuSeconds
}
foreach ($p in $first.processes) { if (-not @($last.processes | Where-Object { $_.pid -eq $p.pid -and $_.startTicks -eq $p.startTicks }).Count) { $changed+='exited:'+$p.pid } }
$result=[ordered]@{method='External observation only; 30 seconds idle then 10 seconds CPU; no in-process benchmark';rootPid=$RootProcessId;logicalProcessors=$logical;first=$first;last=$last;intervalSeconds=$clock.Elapsed.TotalSeconds;changedProcesses=$changed;cpuPercent=if($changed.Count -eq 0){[math]::Round(100*$delta/$clock.Elapsed.TotalSeconds/$logical,3)}else{$null}}
$result.initialContext = $initialContext
$result.automationClientObserved = [bool]($initialContext.automationClients.Count -or $first.measurementContext.automationClients.Count -or $last.measurementContext.automationClients.Count)
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputFile -Encoding UTF8
if ($RequireCleanAutomation) {
  Assert-CleanMeasurementContext $first.measurementContext
  Assert-CleanMeasurementContext $last.measurementContext
}
$result | ConvertTo-Json -Depth 8
