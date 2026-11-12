param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) {
  Push-Location $project
  try { node build.mjs; if ($LASTEXITCODE -ne 0) { throw 'UI build failed' } } finally { Pop-Location }
  & (Join-Path $project 'host\build.ps1') -Testing
}
$profile = Join-Path $project ('dist\test-data\tables-' + [DateTime]::UtcNow.Ticks)
New-Item -ItemType Directory -Path $profile | Out-Null
$exe = Join-Path $project 'dist\MD-Reader-Test.exe'
$process = Start-Process -FilePath $exe -ArgumentList @(('--session-root="' + $profile + '"'), '--test-table-layout') -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Table test timed out: $profile" }
if ($process.ExitCode -ne 0) { throw "Test process failed (exit $($process.ExitCode)); see dist/test-unhandled-exception.txt" }
$resultPath = Join-Path $profile 'table-layout.json'
if (-not (Test-Path -LiteralPath $resultPath)) { throw "Missing table result: $profile" }
$data = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
$data | Add-Member -NotePropertyName testExeSha256 -NotePropertyValue (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
$data | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding UTF8
Write-Output $resultPath
if (-not $data.ok) { throw $data.error }
Write-Output ("Passed {0} table layout cases" -f $data.checks.Count)
