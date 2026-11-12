$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& (Join-Path $project 'host\build.ps1') -Testing
$result = Join-Path $project 'dist\resize-result.json'
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList '--test-resize' -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(90000)) { Stop-Process -Id $process.Id; throw 'Resize test timed out' }
if ($process.ExitCode -ne 0) { throw "Test process failed (exit $($process.ExitCode)); see dist/test-unhandled-exception.txt" }
if (-not (Test-Path -LiteralPath $result)) { throw 'Missing resize test result' }
$data = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
$failed = @($data.checks | Where-Object { -not $_.ok })
Write-Output ("Resize checks: {0}, failures: {1}" -f $data.checks.Count, $failed.Count)
if (-not $data.ok) { $failed | ConvertTo-Json -Depth 4; throw "Resize checks failed: $($data.error)" }
