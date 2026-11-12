param([Parameter(Mandatory=$true)][string]$RunDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'measurement-context.ps1')
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $project
$run = [IO.Path]::GetFullPath($RunDirectory)
$baseline = Get-Content -LiteralPath (Join-Path $run 'summary.json') -Raw | ConvertFrom-Json
$exe = Join-Path $project 'dist\MD-Reader-Test.exe'
if ((Get-FileHash -LiteralPath $exe).Hash -ne $baseline.testExeSha256) { throw 'Expected the frozen release-test build' }
$logical = [Environment]::ProcessorCount
$report = [ordered]@{version=$baseline.version;testExeSha256=$baseline.testExeSha256;method='Fresh isolated test profile per state; 30 seconds idle, then a measured 10-second CPU interval; no video; CDP paced text insertion in the large-document case';logicalProcessors=$logical;powerPlan=(powercfg /getactivescheme | Out-String).Trim();backgroundProcesses=@(Get-Process | Where-Object MainWindowHandle -ne 0 | Select-Object Id,ProcessName);runs=@()}
function Save-Report { $report | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $run 'performance.json') -Encoding UTF8 }
$report.measurementContext = Get-MeasurementContext -Processes @(Get-CimInstance Win32_Process) -SessionId (Get-Process -Id $PID).SessionId
Save-Report
try { Assert-CleanMeasurementContext $report.measurementContext } catch {
  $report.status = 'BLOCKED_ENVIRONMENT'; Save-Report; throw
}
function Wait-Probe([string]$path,$process) {
  $limit=[DateTime]::UtcNow.AddSeconds(130)
  while (-not (Test-Path -LiteralPath $path)) {
    $process.Refresh()
    if ($process.HasExited -or [DateTime]::UtcNow -gt $limit) { throw "Missing probe $path" }
    Start-Sleep -Milliseconds 100
  }
  $data=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
  if (-not $data.ok) { throw $data.error }
  return $data
}
function Snapshot([int]$rootId) {
  $rows=@(Get-CimInstance Win32_Process)
  $ids=[Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($rootId)
  do { $added=$false; foreach ($row in $rows) { if ($ids.Contains([int]$row.ParentProcessId) -and $ids.Add([int]$row.ProcessId)) { $added=$true } } } while ($added)
  $metrics=@(foreach ($row in $rows) {
    if (-not $ids.Contains([int]$row.ProcessId)) { continue }
    try {
      $proc=Get-Process -Id $row.ProcessId -ErrorAction Stop
      if ([math]::Abs(($proc.StartTime.ToUniversalTime()-$row.CreationDate.ToUniversalTime()).TotalSeconds) -gt 1) { continue }
      $owner=(Invoke-CimMethod -InputObject $row -MethodName GetOwnerSid).Sid
      if ($owner -ne $baseline.environment.userSid) { throw 'Unexpected process owner in benchmark tree' }
      [pscustomobject]@{pid=[int]$row.ProcessId;ppid=[int]$row.ParentProcessId;name=$row.Name;session=$row.SessionId;ownerSid=$owner;startUtc=$proc.StartTime.ToUniversalTime().ToString('o');startTicks=$proc.StartTime.ToUniversalTime().Ticks;cpuSeconds=$proc.TotalProcessorTime.TotalSeconds;workingSet=$proc.WorkingSet64;privateBytes=$proc.PrivateMemorySize64}
    } catch [Microsoft.PowerShell.Commands.ProcessCommandException] { }
  })
  [pscustomobject]@{utc=[DateTime]::UtcNow.ToString('o');processes=$metrics;measurementContext=(Get-MeasurementContext -Processes $rows -SessionId (Get-Process -Id $rootId).SessionId);workingSetMiB=[math]::Round(($metrics | Measure-Object workingSet -Sum).Sum/1MB,1);privateMiB=[math]::Round(($metrics | Measure-Object privateBytes -Sum).Sum/1MB,1)}
}
function Measure-Idle([int]$rootId) {
  Start-Sleep -Seconds 30
  $first=Snapshot $rootId
  Assert-CleanMeasurementContext $first.measurementContext
  $watch=[Diagnostics.Stopwatch]::StartNew()
  Start-Sleep -Seconds 10
  $last=Snapshot $rootId
  Assert-CleanMeasurementContext $last.measurementContext
  $watch.Stop()
  $changed=@(); $delta=0.0
  foreach ($p in $last.processes) {
    $prior=@($first.processes | Where-Object { $_.pid -eq $p.pid -and $_.startTicks -eq $p.startTicks })
    if ($prior.Count -ne 1) { $changed+='new:'+$p.pid; continue }
    $delta += $p.cpuSeconds-$prior[0].cpuSeconds
  }
  foreach ($p in $first.processes) { if (-not @($last.processes | Where-Object { $_.pid -eq $p.pid -and $_.startTicks -eq $p.startTicks }).Count) { $changed+='exited:'+$p.pid } }
  [pscustomobject]@{first=$first;last=$last;intervalSeconds=$watch.Elapsed.TotalSeconds;changedProcesses=$changed;cpuPercent=if($changed.Count -eq 0){[math]::Round(100*$delta/$watch.Elapsed.TotalSeconds/$logical,3)}else{$null};cpuForSurvivingProcessesPercent=[math]::Round(100*$delta/$watch.Elapsed.TotalSeconds/$logical,3)}
}
$large=[Text.StringBuilder]::new()
for ($i=0;$i -lt 1200;$i++) { [void]$large.Append("## Section $i`n`n"+('Document text. **Important point** and [link](#section-0). ')*6+"`n`n") }
foreach ($scenario in @('empty','plain','formats','large','tabs','release')) {
  $root=Join-Path $project ('dist\test-data\release-perf-'+$scenario+'-'+[DateTime]::UtcNow.Ticks)
  New-Item -ItemType Directory -Path $root | Out-Null
  $arguments=@('--benchmark='+$scenario,'"--session-root='+$root+'"'); $fixtures=@()
  if ($scenario -ne 'empty') {
    $text=switch($scenario){'large'{$large.ToString()} 'release'{$large.ToString()} 'formats'{[IO.File]::ReadAllText((Join-Path $project 'src\sample.md'))} default{"# Document`n`nPlain text. **Important point**.`n"}}
    $count=if($scenario -eq 'tabs'){20}else{1}
    foreach ($index in 1..$count) {
      $path=Join-Path $root "Document-$index.md"
      [IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false)); $arguments+='"'+$path+'"'
      $fixtures += [pscustomobject]@{name=[IO.Path]::GetFileName($path);bytes=(Get-Item -LiteralPath $path).Length;sha256=(Get-FileHash -LiteralPath $path).Hash}
    }
  }
  if ($scenario -eq 'large') { $arguments+='--benchmark-paced' }
  $process=Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
  try {
    $ready=Wait-Probe (Join-Path $root 'ready.json') $process
    $idle=Measure-Idle $process.Id
    [IO.File]::WriteAllText((Join-Path $root 'measure.done'),'done')
    $released=$null; $after=$null; $orphans=@()
    if ($scenario -eq 'release') {
      $released=Wait-Probe (Join-Path $root 'released.json') $process
      $after=Measure-Idle $process.Id
      foreach ($old in $idle.last.processes | Where-Object { $_.pid -ne $process.Id }) {
        $survivor=Get-Process -Id $old.pid -ErrorAction SilentlyContinue
        if ($survivor -and $survivor.StartTime.ToUniversalTime().Ticks -eq $old.startTicks) { $orphans += $old }
      }
      [IO.File]::WriteAllText((Join-Path $root 'release.done'),'done')
    }
    $result=Wait-Probe (Join-Path $root 'result.json') $process
    if (-not $process.WaitForExit(15000)) { throw 'Benchmark process did not close' }
    if ($process.ExitCode -ne 0) { throw 'Benchmark process failed; see its isolated profile diagnostics' }
    $report.runs += [ordered]@{scenario=$scenario;ready=$ready;fixtures=$fixtures;rootPid=$process.Id;ownerSid=$baseline.environment.userSid;idle=$idle;input=$result;afterLastTab=$after;survivingOriginalChildren=$orphans;status=if($orphans.Count){'FAIL'}elseif($idle.changedProcesses.Count -or ($after -and $after.changedProcesses.Count)){'PARTIAL'}else{'PASS'}}
    Save-Report
    Write-Output "$scenario : private=$($idle.last.privateMiB) MiB, CPU=$($idle.cpuPercent)%, ready=$([math]::Round($ready.readyMs))ms"
  } catch {
    $status=if ($_.Exception.Message.StartsWith('MEASUREMENT_ENVIRONMENT:')) { 'BLOCKED_ENVIRONMENT' } else { 'FAIL' }
    $report.runs += [ordered]@{scenario=$scenario;status=$status;error=$_.Exception.Message}; Save-Report
    Write-Output "$scenario $status`: $($_.Exception.Message)"
  } finally {
    if (-not $process.HasExited) { if ($process.Path -ne $exe) { throw 'Unexpected benchmark process' }; Stop-Process -Id $process.Id; $process.WaitForExit() }
  }
}
if ((Get-FileHash -LiteralPath $exe).Hash -ne $baseline.testExeSha256) { throw 'Frozen test build changed' }
