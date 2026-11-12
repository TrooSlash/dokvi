param([switch]$Testing)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $project
& (Join-Path $project 'scripts\icons.ps1')
$sdkVersion = '1.0.4314.35'
$sdk = Join-Path $project 'tools\webview2'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'lib\net462\Microsoft.Web.WebView2.Core.dll'))) {
  New-Item -ItemType Directory -Force -Path (Join-Path $project 'tools') | Out-Null
  $package = Join-Path $project 'tools\webview2.nupkg'
  Invoke-WebRequest -UseBasicParsing -Uri "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg" -OutFile $package
  [IO.Compression.ZipFile]::ExtractToDirectory($package, $sdk)
}
$stage = Join-Path $project 'build-native'
if (Test-Path -LiteralPath $stage) {
  if ([IO.Path]::GetFullPath($stage) -ne (Join-Path $project 'build-native')) { throw 'Invalid staging path' }
  Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'ui') -Destination $stage -Recurse
foreach ($name in @('Core', 'WinForms')) { Copy-Item -LiteralPath (Join-Path $sdk "lib\net462\Microsoft.Web.WebView2.$name.dll") -Destination $stage }
Copy-Item -LiteralPath (Join-Path $sdk 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $stage
Copy-Item -LiteralPath (Join-Path $sdk 'LICENSE.txt') -Destination (Join-Path $stage 'WEBVIEW2-LICENSE.txt')
$dist = Join-Path $project 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$bundle = Join-Path $dist 'bundle.zip'
if (Test-Path -LiteralPath $bundle) { Remove-Item -LiteralPath $bundle -Force }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $bundle, [IO.Compression.CompressionLevel]::Optimal, $false)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$version = (Get-Content -LiteralPath (Join-Path $project 'package.json') -Raw | ConvertFrom-Json).version
$name = if ($Testing) { 'MD-Reader-Test.exe' } else { "Dokvi-$version.exe" }
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', "/out:$dist\$name", "/win32icon:$project\ui\icon.ico", "/win32manifest:$PSScriptRoot\app.manifest", "/resource:$bundle,bundle.zip", "/resource:$project\src\locales\en.json,strings.en.json")
foreach ($reference in @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.dll', 'System.Web.Extensions.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll')) { $arguments += "/reference:$reference" }
foreach ($reference in @('Core', 'WinForms')) { $arguments += "/reference:$sdk\lib\net462\Microsoft.Web.WebView2.$reference.dll" }
if ($Testing) {
  $arguments += '/define:TESTING'; $arguments += "/resource:$project\test\native.test.js,native.test.js"
  foreach ($uiReference in @('UIAutomationClient', 'UIAutomationTypes', 'WindowsBase')) {
    $arguments += "/reference:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\$uiReference.dll"
  }
}
$arguments += Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native compilation failed' }
Get-Item -LiteralPath (Join-Path $dist $name) | Select-Object FullName, Length
