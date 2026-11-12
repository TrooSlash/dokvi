$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $project 'host\build.ps1') -Testing
$result = Join-Path $project 'dist\quality-checks\result.json'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList '--test-quality' -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(60000)) { Stop-Process -Id $process.Id; throw 'Quality test timed out' }
if ($process.ExitCode -ne 0) { throw "Test process failed (exit $($process.ExitCode)); see dist/test-unhandled-exception.txt" }
if (-not (Test-Path -LiteralPath $result)) { throw 'Missing quality test result' }
$data = Get-Content -LiteralPath $result -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $data.ok) { throw $data.error }
Write-Output ("Passed {0} quality cases" -f $data.checks.Count)
