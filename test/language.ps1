param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $project 'host\build.ps1') -Testing }
$root = Join-Path $project ('dist\test-data\language-' + [DateTime]::UtcNow.Ticks)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$results = @()
$cases = @(@('seed-en','ru-RU'),@('saved-en','ru-RU'),@('saved-ru','en-US'),@('system-en','en-US'),@('system-ru','ru-RU'),@('system-other','de-DE'),@('sample-seed','ru-RU'),@('sample-restore','en-US'))
foreach ($case in $cases) {
  $phase = $case[0]
  $process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList @('--language-phase=' + $phase, '--system-language=' + $case[1], ('"--session-root=' + $root + '"')) -PassThru -WindowStyle Hidden
  if (-not $process.WaitForExit(45000)) { Stop-Process -Id $process.Id; throw ('Language test timed out: ' + $phase) }
  if ($process.ExitCode -ne 0) { throw ('Language process failed: ' + $phase + '; diagnostics: ' + $root) }
  $result = Get-Content -LiteralPath (Join-Path $root ($phase + '.json')) -Raw -Encoding UTF8 | ConvertFrom-Json
  $results += $result
  if (-not $result.ok) { throw ($phase + ': ' + $result.error) }
}
$report = [ordered]@{ok=$true;launches=$cases.Count;checks=@($results | ForEach-Object {$_.checks});root=$root}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $project 'dist\language-result.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 5
