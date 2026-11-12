$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $project 'host\build.ps1') -Testing
$result = Join-Path $project 'dist\focus-routing-result.json'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList '--test-focus-routing' -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(30000)) { Stop-Process -Id $process.Id; throw 'Focus routing test timed out' }
if ($process.ExitCode -ne 0) { throw "Test process failed (exit $($process.ExitCode)); see dist/test-unhandled-exception.txt" }
if (-not (Test-Path -LiteralPath $result)) { throw 'Missing focus routing test result' }
$data = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
Write-Output ("Focus routing checks: {0}; failures: {1}" -f $data.checks.Count, @($data.checks | Where-Object { -not $_.ok }).Count)
if (-not $data.ok) { throw "Focus routing checks failed: $($data.error)" }
