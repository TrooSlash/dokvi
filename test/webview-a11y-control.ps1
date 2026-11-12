param([switch]$Launch)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage = Join-Path $project ('dist\test-data\webview-control-' + [DateTime]::UtcNow.Ticks)
$sdk = Join-Path $project 'tools\webview2'
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($part in @('Core', 'WinForms')) {
  Copy-Item -LiteralPath (Join-Path $sdk "lib\net462\Microsoft.Web.WebView2.$part.dll") -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $sdk 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $sdk 'LICENSE.txt') -Destination (Join-Path $stage 'WEBVIEW2-LICENSE.txt')
$executable = Join-Path $stage 'WebViewControl.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$arguments = @('/nologo', '/utf8output', '/target:winexe', '/platform:x64', '/optimize+', ('/out:' + $executable))
foreach ($reference in @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll')) {
  $arguments += '/reference:' + $reference
}
foreach ($part in @('Core', 'WinForms')) { $arguments += '/reference:' + (Join-Path $stage "Microsoft.Web.WebView2.$part.dll") }
$arguments += Join-Path $PSScriptRoot 'WebViewAccessibilityControl.cs'
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'WebView2 control compilation failed.' }
$result = [ordered]@{ executable = $executable; profile = $stage }
if ($Launch) {
  # This window is the visible test target, not a background helper.
  $process = Start-Process -FilePath $executable -ArgumentList ('"' + $stage + '"') -WindowStyle Normal -PassThru
  $result.pid = $process.Id
}
$result | ConvertTo-Json
