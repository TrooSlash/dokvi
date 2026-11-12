$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'measurement-context.ps1')
function ProcessRow([string]$name, [int]$session) {
  [pscustomobject]@{Name=$name;ProcessId=42;ParentProcessId=1;SessionId=$session}
}
function Check([bool]$condition, [string]$description) {
  if (-not $condition) { throw $description }
  Write-Output "PASS: $description"
}
$empty = Get-MeasurementContext -Processes @() -SessionId 2
Assert-CleanMeasurementContext $empty
Check ($empty.automationClients.Count -eq 0) 'An empty inventory permits measurement'
$readers = Get-MeasurementContext -Processes @((ProcessRow 'Narrator.exe' 2),(ProcessRow 'nvda.exe' 2)) -SessionId 2
Assert-CleanMeasurementContext $readers
Check ($readers.screenReaders.Count -eq 2) 'Screen readers remain enabled and are recorded'
$otherSession = Get-MeasurementContext -Processes @((ProcessRow 'codex-computer-use.exe' 3)) -SessionId 2
Assert-CleanMeasurementContext $otherSession
Check ($otherSession.automationClients.Count -eq 0) 'Another Windows session does not block measurement'
$swift = Get-MeasurementContext -Processes @((ProcessRow 'codex-computer-use-swift.exe' 2)) -SessionId 2
Assert-CleanMeasurementContext $swift
Check ($swift.otherHelpers.Count -eq 1) 'The separate desktop helper is recorded without blaming it for the UIA loop'
$active = Get-MeasurementContext -Processes @((ProcessRow 'CODEX-COMPUTER-USE.EXE' 2),(ProcessRow 'Narrator.exe' 2)) -SessionId 2
$blocked = $false
try { Assert-CleanMeasurementContext $active } catch { $blocked = $_.Exception.Message.StartsWith('MEASUREMENT_ENVIRONMENT:') }
Check ($blocked -and $active.screenReaders.Count -eq 1) 'An active Computer Use client blocks a clean measurement even with a screen reader'
Check ($active.automationClients[0].ProcessId -eq 42) 'The report identifies the interfering client'
