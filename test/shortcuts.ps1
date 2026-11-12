param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $project 'host\build.ps1') -Testing }
$result = Join-Path $project 'dist\shortcut-result.json'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList '--test-shortcuts' -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(120000)) { $process.Kill(); throw 'Shortcut test timed out' }
if ($process.ExitCode -ne 0) { throw "Test process failed (exit $($process.ExitCode)); see dist/test-unhandled-exception.txt" }
if (-not (Test-Path -LiteralPath $result)) { throw 'Missing shortcut test result' }
$data = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
if (-not $data.ok) { throw $data.error }
Write-Output ("Passed {0} keyboard assertions" -f $data.checks.Count)
