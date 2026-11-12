param(
  [Parameter(Mandatory=$true)][int]$RootProcessId,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Choose a new output directory; previous evidence must not be overwritten' }
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
& (Join-Path $PSScriptRoot 'idle-observe.ps1') -RootProcessId $RootProcessId -OutputFile (Join-Path $OutputDirectory 'before.json') | Out-Null
$clientScript = Join-Path $PSScriptRoot 'uia-idle-client.ps1'
$client = Start-Process -FilePath powershell -WindowStyle Hidden -PassThru -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$clientScript+'"'),'-RootProcessId',$RootProcessId,'-OutputDirectory',('"'+$OutputDirectory+'"')) -RedirectStandardError (Join-Path $OutputDirectory 'client-error.txt')
try {
  $deadline = [DateTime]::UtcNow.AddSeconds(60)
  $resultFile = Join-Path $OutputDirectory 'uia-client.json'
  while (-not (Test-Path -LiteralPath $resultFile) -and -not $client.HasExited -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
  $status = if (Test-Path -LiteralPath $resultFile) { 'OBSERVED' } elseif ($client.HasExited) { 'ERROR' } else { 'TIMEOUT' }
  @{status=$status;timeoutSeconds=60;rootPid=$RootProcessId;clientPid=$client.Id} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'outcome.json') -Encoding UTF8
  if ($status -eq 'OBSERVED') {
    & (Join-Path $PSScriptRoot 'idle-observe.ps1') -RootProcessId $RootProcessId -OutputFile (Join-Path $OutputDirectory 'with-client.json') | Out-Null
  }
} finally {
  [IO.File]::WriteAllText((Join-Path $OutputDirectory 'uia-exit.signal'),'')
  if (-not $client.WaitForExit(5000)) { $client.Kill(); $client.WaitForExit() }
}
& (Join-Path $PSScriptRoot 'idle-observe.ps1') -RootProcessId $RootProcessId -OutputFile (Join-Path $OutputDirectory 'after.json') | Out-Null
if ($status -ne 'OBSERVED') { throw "UI Automation query: $status. See outcome.json; post-client idle measurement was preserved." }
