$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $project 'host\build.ps1') -Testing
$result = Join-Path $project 'dist\native-test-result.json'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList '--test' -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(300000)) { Stop-Process -Id $process.Id; throw 'Native test process timed out' }
if ($process.ExitCode -ne 0) { throw "Test process failed (exit $($process.ExitCode)); see dist/test-unhandled-exception.txt" }
if (-not (Test-Path -LiteralPath $result)) { throw 'No native test result; see native-test-error.txt' }
Get-Content -LiteralPath $result -Raw
if (-not (Get-Content -LiteralPath $result -Raw | ConvertFrom-Json).ok) { throw 'Native regression suite failed' }
