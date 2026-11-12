param([switch]$SkipBuild, [switch]$ButtonStates, [switch]$KeyboardNavigation, [switch]$DialogKeyboard)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $SkipBuild) { & (Join-Path $project 'host\build.ps1') -Testing; if ($LASTEXITCODE -ne 0) { throw 'Build failed' } }
$suite = if ($DialogKeyboard) { 'dialog-keyboard-checks' } elseif ($KeyboardNavigation) { 'keyboard-navigation-checks' } elseif ($ButtonStates) { 'button-state-checks' } else { 'native-menu-checks' }
$argument = if ($DialogKeyboard) { '--test-dialog-keyboard' } elseif ($KeyboardNavigation) { '--test-keyboard-navigation' } elseif ($ButtonStates) { '--test-button-states' } else { '--test-native-menu' }
$result = Join-Path $project "dist\$suite\result.json"
if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
$process = Start-Process -FilePath (Join-Path $project 'dist\MD-Reader-Test.exe') -ArgumentList $argument -PassThru -WindowStyle Hidden
if (-not $process.WaitForExit(240000)) { Stop-Process -Id $process.Id; throw "$suite timed out" }
if ($process.ExitCode -ne 0) { throw "Test process failed (exit $($process.ExitCode)); see dist/test-unhandled-exception.txt" }
if (-not (Test-Path -LiteralPath $result)) { throw 'Missing native menu test result' }
$data = Get-Content -LiteralPath $result -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $data.ok) { throw ($data | ConvertTo-Json -Depth 8) }
Write-Output ("Passed {0}: {1} checks" -f $suite,$data.checks.Count)
