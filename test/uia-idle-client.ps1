param(
  [Parameter(Mandatory=$true)][int]$RootProcessId,
  [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase
$process = Get-Process -Id $RootProcessId
$expectedExe = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\dist\MD-Reader-Test.exe'))
if ($process.Path -ne $expectedExe -or -not $process.MainWindowHandle) { throw 'Not an isolated Dokvi test window' }
$timer = [Diagnostics.Stopwatch]::StartNew()
$root = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
$initial = $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
Start-Sleep -Seconds 2
$elements = $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition)
$result = @{rootPid=$RootProcessId;clientPid=$PID;initialNodes=$initial.Count;nodes=$elements.Count;queryMs=$timer.Elapsed.TotalMilliseconds;method='Read-only UI Automation descendant query, repeat after provider activation; then retain references, no input or polling';tail=@($elements | Select-Object -Last 12 | ForEach-Object { @{name=$_.Current.Name;type=$_.Current.ControlType.ProgrammaticName;class=$_.Current.ClassName;offscreen=$_.Current.IsOffscreen} })}
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'uia-client.json'), ($result | ConvertTo-Json))
$deadline = [DateTime]::UtcNow.AddSeconds(180)
while (-not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'uia-exit.signal')) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 250 }
