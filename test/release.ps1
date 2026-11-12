param([string]$RunDirectory)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $project
$version = (Get-Content -LiteralPath 'package.json' -Raw | ConvertFrom-Json).version
if (-not $RunDirectory) { $RunDirectory = Join-Path $project ('quality\logs\release-' + $version + '-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$run = [IO.Path]::GetFullPath($RunDirectory)
New-Item -ItemType Directory -Path $run -Force | Out-Null
& (Join-Path $project 'host\build.ps1') -Testing *> (Join-Path $run 'build.log')
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
$exe = Join-Path $project 'dist\MD-Reader-Test.exe'
$frozenHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$os = Get-CimInstance Win32_OperatingSystem
$cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
$summary = [ordered]@{version=$version;testExeSha256=$frozenHash;productionExeSha256=(Get-FileHash ("dist\Dokvi-$version.exe")).Hash;environment=@{windows=$os.Caption;build=$os.BuildNumber;cpu=$cpu.Name;logicalProcessors=[Environment]::ProcessorCount;ramGiB=[math]::Round($os.TotalVisibleMemorySize/1MB,2);userSid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value};tests=@()}
function Save-Summary { $summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $run 'summary.json') -Encoding UTF8 }
function Check-Frozen { if ((Get-FileHash -LiteralPath $exe).Hash -ne $frozenHash) { throw 'Test executable changed during release run' } }
function Run-App([string]$name,[string]$argument,[string]$relativeResult,[int]$timeoutSeconds) {
  Check-Frozen
  $result = Join-Path $project $relativeResult
  if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
  $clock = [Diagnostics.Stopwatch]::StartNew()
  $process = Start-Process -FilePath $exe -ArgumentList $argument -PassThru -WindowStyle Hidden
  $errorText = $null; $data = $null
  try {
    if (-not $process.WaitForExit($timeoutSeconds * 1000)) { throw "$name timed out" }
    if ($process.ExitCode -ne 0) { throw "$name process failed (exit $($process.ExitCode)); see dist/test-unhandled-exception.txt" }
    if (-not (Test-Path -LiteralPath $result)) { throw "$name did not write its result" }
    $data = Get-Content -LiteralPath $result -Raw -Encoding UTF8 | ConvertFrom-Json
    Copy-Item -LiteralPath $result -Destination (Join-Path $run ($name + '.json'))
    if (-not $data.ok) { throw $data.error }
  } catch { $errorText = $_.Exception.Message }
  finally { if (-not $process.HasExited) { if ($process.Path -ne $exe) { throw 'Unexpected process identity' }; Stop-Process -Id $process.Id; $process.WaitForExit() } }
  $clock.Stop()
  $status = if ($errorText -or -not $data.ok) {'FAIL'} else {'PASS'}
  $summary.tests += [ordered]@{name=$name;status=$status;checks=@($data.checks).Count;seconds=[math]::Round($clock.Elapsed.TotalSeconds,1);error=$errorText}
  Save-Summary
  Write-Output "$name $status ($(@($data.checks).Count) checks)"
  Check-Frozen
}
Save-Summary
Write-Output "Release run: $run"
$startupRoot = Join-Path $project ('dist\test-data\release-startup-' + [DateTime]::UtcNow.Ticks)
New-Item -ItemType Directory -Path $startupRoot | Out-Null
foreach ($index in 1..3) {
  $relative = [IO.Path]::Combine('dist','test-data',[IO.Path]::GetFileName($startupRoot),'startup.json')
  Run-App "startup-$index" ('--test-startup "--session-root=' + $startupRoot + '"') $relative 30
  Copy-Item -LiteralPath (Join-Path $startupRoot 'startup.png') -Destination (Join-Path $run "startup-$index.png")
}
Run-App 'native' '--test' 'dist\native-test-result.json' 300
Run-App 'focus-routing' '--test-focus-routing' 'dist\focus-routing-result.json' 30
Run-App 'click-taskbar' '--test-click-taskbar' 'dist\click-result.json' 120
Run-App 'taskbar-focus' '--test-taskbar-focus' 'dist\taskbar-focus-result.json' 100
Run-App 'input-focus' '--test-input-focus' 'dist\input-focus-result.json' 75
Run-App 'window' '--test-window' 'dist\window-result.json' 90
Run-App 'resize' '--test-resize' 'dist\resize-result.json' 90
Run-App 'shortcuts' '--test-shortcuts' 'dist\shortcut-result.json' 120
Run-App 'frame' '--test-frame' 'dist\frame-checks\result.json' 60
Run-App 'transition' '--test-transition' 'dist\transition-checks\result.json' 120
Copy-Item -LiteralPath (Join-Path $project 'dist\transition-checks') -Destination (Join-Path $run 'transition-frames') -Recurse
Run-App 'transition-repeat' '--test-transition-repeat' 'dist\transition-checks\result.json' 180
Copy-Item -LiteralPath (Join-Path $project 'dist\transition-checks') -Destination (Join-Path $run 'transition-repeat-frames') -Recurse
Run-App 'quality' '--test-quality' 'dist\quality-checks\result.json' 90
foreach ($suite in @('session','language','exception-guard')) {
  Check-Frozen
  & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot ($suite + '.ps1')) -SkipBuild *> (Join-Path $run ($suite + '.log'))
  $exitCode = $LASTEXITCODE
  $result = Join-Path $project ('dist\' + $suite + '-result.json')
  if ($exitCode -eq 0) { Copy-Item -LiteralPath $result -Destination (Join-Path $run ($suite + '.json')) }
  $summary.tests += [ordered]@{name=$suite;status=if($exitCode -eq 0){'PASS'}else{'FAIL'};log=$suite+'.log'}
  Save-Summary; Check-Frozen
  Write-Output "$suite exit=$exitCode"
}
Write-Output ('Completed: ' + $run)
if (@($summary.tests | Where-Object status -ne 'PASS').Count) { throw "Release checks failed; see $run" }
