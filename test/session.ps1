param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $project 'host\build.ps1') -Testing }
$root = Join-Path $project ('dist\test-data\session-' + [DateTime]::UtcNow.Ticks)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$results = @()
foreach($phase in @('seed','recover','normal','blank','missing','empty')) {
  $exe = Join-Path $project 'dist\MD-Reader-Test.exe'
  $process = Start-Process -FilePath $exe -ArgumentList @('--session-phase=' + $phase, ('"--session-root=' + $root + '"')) -PassThru -WindowStyle Hidden
  $resultFile = Join-Path $root ($phase + '.json')
  $deadline = [DateTime]::UtcNow.AddSeconds(60)
  while(-not (Test-Path -LiteralPath $resultFile) -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200; $process.Refresh() }
  if(-not (Test-Path -LiteralPath $resultFile)) { if(-not $process.HasExited) { Stop-Process -Id $process.Id }; throw ('Missing result for phase ' + $phase + ' in ' + $root) }
  $result = Get-Content -LiteralPath $resultFile -Raw -Encoding UTF8 | ConvertFrom-Json
  $results += $result
  if(-not $result.ok) { if(-not $process.HasExited) { Stop-Process -Id $process.Id }; throw ($phase + ': ' + $result.error) }
  if($phase -eq 'seed') {
    # This instance and its test profile are created solely by this test.
    if($process.Path -ne $exe) { throw 'Unexpected process before forced termination' }
    Stop-Process -Id $process.Id -Force
    $process.WaitForExit()
    [IO.File]::WriteAllText((Join-Path $root 'A.md'),'# External A')
    [IO.File]::WriteAllText((Join-Path $root 'B.md'),'# External B')
  } elseif(-not $process.WaitForExit(10000)) { Stop-Process -Id $process.Id; throw ('Phase did not exit: ' + $phase) }
  if ($phase -ne 'seed' -and $process.ExitCode -ne 0) { throw ('Session process failed: ' + $phase + '; diagnostics: ' + $root) }
}
$report = [ordered]@{ok=$true;method='Real process termination and five fresh launches, including pristine window close and missing-file recovery';checks=@($results | ForEach-Object {$_.checks});root=$root}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $project 'dist\session-result.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 5
