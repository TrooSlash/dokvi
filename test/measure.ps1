param([string]$Label, [ValidateRange(1,5)][int]$Repeats = 2, [switch]$SkipBuild,
  [ValidateSet('empty','plain','large','formats','tabs')][string[]]$Scenarios = @('empty','plain','large','formats','tabs'))
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'measurement-context.ps1')
$measurementContext = Get-MeasurementContext -Processes @(Get-CimInstance Win32_Process) -SessionId (Get-Process -Id $PID).SessionId
Assert-CleanMeasurementContext $measurementContext
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Label) { $Label = (Get-Content (Join-Path $project 'package.json') -Raw | ConvertFrom-Json).version }
if ($Label -notmatch '^[a-zA-Z0-9.-]+$') { throw 'Invalid benchmark label' }
if (-not $SkipBuild) { & (Join-Path $project 'host/build.ps1') -Testing }
$exe = Join-Path $project 'dist/MD-Reader-Test.exe'
$folder = Join-Path $project 'quality/benchmarks'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
function Measure-Tree([int]$rootId) {
  $inventory = Get-CimInstance Win32_Process | Select-Object ProcessId,ParentProcessId,Name,SessionId
  $context = Get-MeasurementContext -Processes $inventory -SessionId (Get-Process -Id $rootId).SessionId
  Assert-CleanMeasurementContext $context
  $ids = [Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($rootId)
  do { $added = $false; foreach ($entry in $inventory) { if ($ids.Contains([int]$entry.ParentProcessId) -and $ids.Add([int]$entry.ProcessId)) { $added = $true } } } while ($added)
  $perf = @{}; Get-CimInstance Win32_PerfFormattedData_PerfProc_Process | ForEach-Object { $perf[[int]$_.IDProcess] = $_ }
  $rows = foreach ($processId in $ids) {
    $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($process) { [pscustomobject]@{ name=$process.ProcessName; workingSet=$process.WorkingSet64; privateBytes=$process.PrivateMemorySize64; privateWorkingSet=if ($perf.ContainsKey($processId)) { [long]$perf[$processId].WorkingSetPrivate } else { 0 }; cpuSeconds=$process.TotalProcessorTime.TotalSeconds } }
  }
  [pscustomobject]@{ measurementContext=$context; workingSetMiB=[math]::Round(($rows | Measure-Object workingSet -Sum).Sum/1MB,1); privateWorkingSetMiB=[math]::Round(($rows | Measure-Object privateWorkingSet -Sum).Sum/1MB,1); privateMiB=[math]::Round(($rows | Measure-Object privateBytes -Sum).Sum/1MB,1); cpuSeconds=($rows | Measure-Object cpuSeconds -Sum).Sum; processes=@($rows) }
}
function Wait-Json([string]$path, $process) {
  $deadline = [DateTime]::UtcNow.AddSeconds(70)
  while (-not (Test-Path -LiteralPath $path)) {
    if ($process.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw "Probe did not produce $path" }
    Start-Sleep -Milliseconds 100
  }
  $value = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
  if (-not $value.ok) { throw $value.error }
  return $value
}
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$report = [ordered]@{ label=$Label; method='Optimized TESTING build, fresh isolated profile per run, full process tree, 30 CDP text insertions'; environment=@{windows=$os.Caption;build=$os.BuildNumber;cpu=$cpu.Name;logicalProcessors=[Environment]::ProcessorCount;ramGiB=[math]::Round($os.TotalVisibleMemorySize/1MB,1)}; runs=@() }
$report.environment.measurementContext = $measurementContext
$large = [Text.StringBuilder]::new()
for ($i=0; $i -lt 1200; $i++) { [void]$large.Append("## Section $i`n`n" + ('Document text. **Important point** and [link](#section-0). ')*6 + "`n`n") }
foreach ($iteration in 1..$Repeats) {
  foreach ($scenario in $Scenarios) {
    $root = Join-Path $project ('dist/test-data/benchmark-' + $Label + '-' + $scenario + '-' + [DateTime]::UtcNow.Ticks)
    New-Item -ItemType Directory -Path $root | Out-Null
    $arguments = @('--benchmark='+$scenario, '"--session-root='+$root+'"')
    if ($scenario -ne 'empty') {
      $text = switch ($scenario) { 'large' {$large.ToString()} 'formats' {[IO.File]::ReadAllText((Join-Path $project 'src/sample.md'))} default {"# Document`n`nPlain text. **Important point**.`n"} }
      $count = if ($scenario -eq 'tabs') { 20 } else { 1 }
      foreach ($index in 1..$count) { $file=Join-Path $root ("Document-$index.md"); [IO.File]::WriteAllText($file,$text,[Text.UTF8Encoding]::new($false)); $arguments+='"'+$file+'"' }
    }
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    try {
      $ready=Wait-Json (Join-Path $root 'ready.json') $process
      Start-Sleep -Seconds 2
      $first=Measure-Tree $process.Id; $clock=[Diagnostics.Stopwatch]::StartNew()
      Start-Sleep -Seconds 2
      $second=Measure-Tree $process.Id; $clock.Stop()
      [IO.File]::WriteAllText((Join-Path $root 'measure.done'),'done')
      $result=Wait-Json (Join-Path $root 'result.json') $process
      if(-not $process.WaitForExit(10000)) { throw 'Benchmark did not close' }
      $run=[pscustomobject]@{scenario=$scenario;iteration=$iteration;readyMs=[math]::Round($ready.readyMs);dpi=$ready.dpi;runtime=$ready.runtime;memory=$second;idleCpuPercent=[math]::Round(100*($second.cpuSeconds-$first.cpuSeconds)/$clock.Elapsed.TotalSeconds/[Environment]::ProcessorCount,2);input30Ms=[math]::Round($result.input30Ms);previewAfterInputMs=[math]::Round($result.previewAfterInputMs)}
      $report.runs+= $run
      $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $folder ($Label+'.json')) -Encoding UTF8
      Write-Output "$scenario #$iteration : ready=$($run.readyMs)ms, private=$($second.privateMiB)MiB, input=$($run.input30Ms)ms"
    } finally { if(-not $process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() } }
  }
}
