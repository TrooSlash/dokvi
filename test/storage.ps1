param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $project 'host\build.ps1') -Testing }
$root = Join-Path $project ('dist\test-data\storage-' + [DateTime]::UtcNow.Ticks)
$exe = Join-Path $project 'dist\MD-Reader-Test.exe'
$process = Start-Process -FilePath $exe -ArgumentList @('--test-storage', ('"--session-root=' + $root + '"')) -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(30000)) { Stop-Process -Id $process.Id; throw 'Storage checks timed out' }
$result = Join-Path $root 'storage-result.json'
if (-not (Test-Path -LiteralPath $result)) { throw ('No storage result in ' + $root) }
$data = Get-Content -LiteralPath $result -Raw -Encoding UTF8 | ConvertFrom-Json
Copy-Item -LiteralPath $result -Destination (Join-Path $project 'dist\storage-result.json')
if ($process.ExitCode -ne 0 -or -not $data.ok) { throw $data.error }
$data | ConvertTo-Json -Depth 5
