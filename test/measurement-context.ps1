function Get-MeasurementContext {
  param([object[]]$Processes, [int]$SessionId)
  $relevant = @($Processes | Where-Object {
    $_.SessionId -eq $SessionId -and $_.Name -in @('codex-computer-use.exe', 'codex-computer-use-swift.exe', 'Narrator.exe', 'nvda.exe')
  } | Select-Object ProcessId, ParentProcessId, Name, SessionId)
  [pscustomobject]@{
    sessionId = $SessionId
    automationClients = @($relevant | Where-Object Name -eq 'codex-computer-use.exe')
    screenReaders = @($relevant | Where-Object Name -in @('Narrator.exe', 'nvda.exe'))
    otherHelpers = @($relevant | Where-Object Name -eq 'codex-computer-use-swift.exe')
  }
}

function Assert-CleanMeasurementContext {
  param($Context)
  if ($Context.automationClients.Count) {
    throw 'MEASUREMENT_ENVIRONMENT: Computer Use is still connected. Finish UI actions and release the CUA session before measuring. Keep the screen reader and application accessibility enabled.'
  }
}
