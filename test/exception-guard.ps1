param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $project 'host\build.ps1') -Testing }
$profile = Join-Path $project ('dist\test-data\exception-guard-' + [DateTime]::UtcNow.Ticks)
New-Item -ItemType Directory -Path $profile | Out-Null
$process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList @('--test-unhandled-exception', ('--session-root="' + $profile + '"')) -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'UI exception guard did not terminate the test process' }
if ($process.ExitCode -ne 1) { throw 'UI exception did not produce a failing exit code' }
$details = Get-Content -LiteralPath (Join-Path $profile 'unhandled-exception.txt') -Raw
if (-not $details.Contains('Intentional UI exception: regression guard') -or -not $details.Contains('MdReader.ReaderForm')) { throw 'UI exception stack was not recorded' }
$report = [ordered]@{ok=$true;root=$profile;checks=@('Unhandled UI exception terminates the test without a Continue dialog','Process exits with failure code 1','Exception and UI stack are written in the isolated profile')}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $project 'dist\exception-guard-result.json') -Encoding UTF8
$report | ConvertTo-Json -Depth 5
