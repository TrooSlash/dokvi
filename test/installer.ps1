$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = (Get-Content -LiteralPath (Join-Path $project 'package.json') -Raw | ConvertFrom-Json).version
$testRoot = Join-Path $project ('dist\test-data\installer-' + [DateTime]::UtcNow.Ticks)
$installDirectory = Join-Path $testRoot 'Проверка установки Dokvi'
$appKey = 'Software\Dokvi.InstallTest'
$progId = 'Dokvi.InstallTest.Markdown'
$exeName = 'Dokvi-InstallTest.exe'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Dokvi Installer Test.lnk'
$desktop = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Dokvi Installer Test.lnk'
if ([Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($appKey) -or (Test-Path -LiteralPath $shortcut) -or (Test-Path -LiteralPath $desktop)) { throw 'Existing installer test state found; refusing to overwrite it' }
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$checks = [Collections.Generic.List[string]]::new()
function Assert([bool]$Condition, [string]$Label) {
  if (-not $Condition) { throw $Label }
  $checks.Add($Label); Write-Output "PASS $Label"
}
function Read-Value([string]$Key, [string]$Name) {
  $opened = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($Key)
  if (-not $opened) { return $null }
  try { return $opened.GetValue($Name) } finally { $opened.Dispose() }
}
function Hash-File([string]$Path) {
  $sha = [Security.Cryptography.SHA256]::Create()
  $stream = [IO.File]::OpenRead($Path)
  try { return [BitConverter]::ToString($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
}
function Association-Snapshot {
  $snapshot = [ordered]@{}
  foreach ($extension in @('.md','.markdown','.mdown','.mkd','.txt')) {
    foreach ($key in @("Software\Classes\$extension", "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$extension\UserChoice", "Software\Classes\$extension\OpenWithProgids")) {
      $opened = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key)
      $values = [ordered]@{}
      if ($opened) {
        foreach ($name in ($opened.GetValueNames() | Sort-Object)) {
          if ($name -ne $progId) { $values[$name] = @([string]$opened.GetValueKind($name), $opened.GetValue($name)) }
        }
        $opened.Dispose()
      }
      $snapshot[$key] = $values
    }
  }
  return ($snapshot | ConvertTo-Json -Depth 8 -Compress)
}
function Run-Installer([string]$Path, [string]$LogName, [string[]]$Extra, [bool]$MustSucceed = $true) {
  $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/LANG=russian',('/DIR="' + $installDirectory + '"'),('/LOG="' + (Join-Path $testRoot $LogName) + '"')) + $Extra
  $process = Start-Process -FilePath $Path -ArgumentList $arguments -WindowStyle Hidden -PassThru
  if (-not $process.WaitForExit(60000)) { throw "Installer timed out: $LogName (PID $($process.Id))" }
  if ($MustSucceed -and $process.ExitCode -ne 0) { throw "Installer exited with $($process.ExitCode): $LogName" }
  return $process.ExitCode
}
function Wait-Probe([scriptblock]$Condition) {
  $path = Join-Path $installDirectory 'test-data\shell\shell-probe.json'
  $deadline = [DateTime]::UtcNow.AddSeconds(40)
  while ([DateTime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $path) {
      $state = Get-Content -LiteralPath $path -Encoding UTF8 -Raw | ConvertFrom-Json
      if (& $Condition $state) { return $state }
    }
    Start-Sleep -Milliseconds 150
  }
  throw 'Windows Shell launch did not produce the expected document state'
}
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class DokviInstallerShell {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  struct ShellInfo {
    public int size; public uint mask; public IntPtr window;
    public string verb, file, parameters, directory; public int show;
    public IntPtr instance, idList; public string className;
    public IntPtr classKey; public uint hotKey; public IntPtr icon, process;
  }
  [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  static extern bool ShellExecuteEx(ref ShellInfo info);
  public static void Open(string file, string className) {
    var info = new ShellInfo { size = Marshal.SizeOf(typeof(ShellInfo)), mask = 0x401,
      verb = "open", file = file, className = className, show = 0 };
    if (!ShellExecuteEx(ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
  }
}
'@
$before = Association-Snapshot
$installed = $false
try {
  & (Join-Path $project 'host\build.ps1') -Testing
  & (Join-Path $project 'installer\build.ps1') -Testing
  # A tiny valid older-version binary exercises real file replacement on upgrade.
  $oldSource = Join-Path $testRoot 'old.cs'
  [IO.File]::WriteAllText($oldSource, 'using System.Reflection; [assembly:AssemblyVersion("2.3.9.0")][assembly:AssemblyFileVersion("2.3.9.0")] class Old { static void Main() {} }')
  $oldBinary = Join-Path $testRoot 'Old.exe'
  & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:winexe /platform:x64 "/out:$oldBinary" $oldSource
  if ($LASTEXITCODE -ne 0) { throw 'Old version fixture compilation failed' }
  & (Join-Path $project 'installer\build.ps1') -Testing -Version '2.3.9' -Binary $oldBinary
  $setup = Join-Path $project "dist\Dokvi-Test-Setup-$version.exe"
  $older = Join-Path $project 'dist\Dokvi-Test-Setup-2.3.9.exe'
  $exe = Join-Path $installDirectory $exeName
  Assert ((Run-Installer $setup '00-runtime-failure.log' @('/TEST-RUNTIME-FAILURE=1') $false) -ne 0) 'Failed prerequisite installation stops setup (simulated failure; shared runtime untouched)'
  Assert (-not (Test-Path -LiteralPath $exe) -and $null -eq (Read-Value $appKey 'InstallDir')) 'Failed prerequisite leaves no installed application or file registration'
  $null = Run-Installer $older '01-install.log' @('/TASKS=""')
  $installed = $true
  Assert ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -eq '2.3.9.0') 'Fresh per-user installation to a path with Cyrillic and spaces'
  Assert ((Test-Path -LiteralPath $shortcut) -and -not (Test-Path -LiteralPath $desktop)) 'Start menu shortcut created; desktop shortcut is opt-in'
  $keepFile = Join-Path $installDirectory 'User document.md'
  [IO.File]::WriteAllText($keepFile, '# Keep this document')
  $null = Run-Installer $setup '02-upgrade.log' @('/TASKS=desktopicon')
  Assert ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -eq "$version.0") 'Upgrade replaces the older executable in the same location'
  $uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\Dokvi.InstallTest_is1'
  Assert ((Read-Value $appKey 'Version') -eq $version -and (Read-Value $uninstallKey 'DisplayVersion') -eq $version -and (Read-Value $uninstallKey 'DisplayName') -eq 'Dokvi Installer Test') 'Upgrade updates the same entry in Installed Apps'
  Assert ((Read-Value 'Software\RegisteredApplications' 'Dokvi Installer Test') -eq "$appKey\Capabilities") 'RegisteredApplications points to per-user capabilities'
  Assert ((Read-Value "Software\Classes\$progId\DefaultIcon" '') -eq ('"' + $exe + '",0')) 'Markdown documents use the installed Dokvi icon'
  foreach ($extension in @('.md','.markdown','.mdown','.mkd')) {
    Assert ((Read-Value "$appKey\Capabilities\FileAssociations" $extension) -eq $progId -and $null -ne (Read-Value "Software\Classes\$extension\OpenWithProgids" $progId)) "Windows registers $extension in Default Apps and Open with"
  }
  $expectedCommand = '"' + $exe + '" "--session-root=' + $installDirectory + '\test-data\shell" --shell-probe "%1"'
  Assert ((Read-Value "Software\Classes\$progId\shell\open\command" '') -eq $expectedCommand) 'Open command quotes both executable and document paths'
  $wsh = New-Object -ComObject WScript.Shell
  Assert ($wsh.CreateShortcut($shortcut).TargetPath -eq $exe -and $wsh.CreateShortcut($desktop).TargetPath -eq $exe) 'Both shortcuts target the stable installed executable'
  $null = Run-Installer $setup '03-repair.log' @('/TASKS=desktopicon')
  Assert ((Hash-File $exe) -eq (Hash-File (Join-Path $project 'dist\MD-Reader-Test.exe'))) 'Reinstalling the same version repairs the installed binary'
  Assert ((Run-Installer $older '04-downgrade.log' @() $false) -ne 0) 'Older installer refuses to downgrade a newer installation'
  $first = Join-Path $testRoot 'Первый документ & пробел.md'
  $second = Join-Path $testRoot 'Второй [документ].md'
  [IO.File]::WriteAllText($first, '# Первый документ')
  [IO.File]::WriteAllText($second, '# Второй документ')
  [DokviInstallerShell]::Open($first, $progId)
  $state = Wait-Probe { param($s) $s.paths -contains $first -and $s.title.EndsWith(' — Dokvi') }
  $firstProcess = [int]$state.pid
  Assert ($state.title.EndsWith(' — Dokvi')) 'Windows Shell opens a Unicode document using the registered handler'
  [DokviInstallerShell]::Open($second, $progId)
  $state = Wait-Probe { param($s) $s.paths -contains $second -and $s.paths.Count -eq 2 }
  Assert ([int]$state.pid -eq $firstProcess) 'A second Shell launch opens a tab in the existing process'
  [DokviInstallerShell]::Open($first, $progId)
  $state = Wait-Probe { param($s) $s.title.StartsWith([IO.Path]::GetFileName($first)) }
  Assert ($state.paths.Count -eq 2 -and [int]$state.pid -eq $firstProcess) 'Opening the same file again activates its existing tab'
  Assert ((Run-Installer $setup '05-running-app.log' @() $false) -ne 0) 'Update refuses to replace a running application'
  $busyUninstall = Start-Process -FilePath (Join-Path $installDirectory 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru
  if (-not $busyUninstall.WaitForExit(20000)) { throw 'Busy uninstall did not return' }
  Assert ($busyUninstall.ExitCode -ne 0 -and (Test-Path -LiteralPath $exe)) 'Uninstall refuses to remove a running application'
  [IO.File]::WriteAllText((Join-Path $installDirectory 'test-data\shell\shell-probe.stop'), 'stop')
  $appProcess = Get-Process -Id $firstProcess -ErrorAction SilentlyContinue
  if ($appProcess -and -not $appProcess.WaitForExit(20000)) { throw 'Shell probe did not close normally' }
  Assert ((Association-Snapshot) -eq $before) 'Installation, update and Shell launches preserve existing user defaults and other handlers'
  $uninstaller = Join-Path $installDirectory 'unins000.exe'
  $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $testRoot '06-uninstall.log') + '"')) -WindowStyle Hidden -PassThru
  if (-not $process.WaitForExit(45000) -or $process.ExitCode -ne 0) { throw 'Test uninstall failed' }
  $installed = $false
  Assert (-not (Test-Path -LiteralPath $exe) -and -not (Test-Path -LiteralPath $shortcut) -and -not (Test-Path -LiteralPath $desktop)) 'Uninstall removes the application and both shortcuts'
  Assert ($null -eq (Read-Value $appKey 'InstallDir') -and $null -eq (Read-Value 'Software\RegisteredApplications' 'Dokvi Installer Test') -and $null -eq (Read-Value "Software\Classes\$progId\shell\open\command" '')) 'Uninstall removes only the installer-owned registrations'
  Assert ((Association-Snapshot) -eq $before) 'Uninstall preserves existing file associations and other applications'
  Assert (([IO.File]::ReadAllText($keepFile)) -eq '# Keep this document' -and (Test-Path -LiteralPath (Join-Path $installDirectory 'test-data\shell\session'))) 'Uninstall preserves user-created files and the saved session'
  [ordered]@{ok=$true;checks=$checks;root=$testRoot;os=[Environment]::OSVersion.VersionString;defaultChoiceChanged=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $project 'dist\installer-result.json') -Encoding UTF8
} catch {
  [ordered]@{ok=$false;checks=$checks;error=$_.ToString();root=$testRoot;installed=$installed} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $project 'dist\installer-result.json') -Encoding UTF8
  $originalError = $_
  if ($installed -and (Test-Path -LiteralPath (Join-Path $installDirectory 'unins000.exe'))) {
    $probeRoot = Join-Path $installDirectory 'test-data\shell'
    if (Test-Path -LiteralPath $probeRoot) {
      [IO.File]::WriteAllText((Join-Path $probeRoot 'shell-probe.stop'),'stop')
      Get-Process -Name 'Dokvi-InstallTest' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $installDirectory $exeName) } | ForEach-Object {
        if (-not $_.WaitForExit(15000)) { Stop-Process -Id $_.Id -Force }
      }
    }
    $cleanup = Start-Process -FilePath (Join-Path $installDirectory 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru
    if (-not $cleanup.WaitForExit(30000) -or $cleanup.ExitCode -ne 0) { Write-Warning 'Test installer cleanup needs attention; see the result report' }
  }
  throw $originalError
}
