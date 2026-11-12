param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) {
  Push-Location $project
  try { node build.mjs; if ($LASTEXITCODE -ne 0) { throw 'UI build failed' } } finally { Pop-Location }
  & (Join-Path $project 'host\build.ps1') -Testing
}
$profile = Join-Path $project ('dist\test-data\pdf-tables-' + [DateTime]::UtcNow.Ticks)
New-Item -ItemType Directory -Path $profile | Out-Null
$process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList @(('--session-root="' + $profile + '"'), '--test-pdf-tables') -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(90000)) { $process.Kill(); throw "PDF test timed out: $profile" }
if ($process.ExitCode -ne 0) { throw "PDF test process failed (exit $($process.ExitCode)); diagnostics: $profile" }
$manifest = Join-Path $profile 'pdf-tables\manifest.json'
Write-Output $manifest
if (-not (Test-Path -LiteralPath $manifest)) { throw 'Missing PDF manifest' }
$data = Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$data | Add-Member -NotePropertyName testExeSha256 -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $project 'dist\MD-Reader-Test.exe') -Algorithm SHA256).Hash
$data | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifest -Encoding UTF8
if (-not $data.ok) { throw $data.error }
Write-Output ("Exported {0} PDF cases; inspect the files with verify-pdf-tables.py" -f $data.cases.Count)
