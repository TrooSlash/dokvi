param([switch]$Testing, [string]$Version, [string]$Binary, [string]$SignCommand)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -Global -ErrorAction Stop
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Version) { $Version = (Get-Content -LiteralPath (Join-Path $project 'package.json') -Raw | ConvertFrom-Json).version }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch' }
if (-not $Binary) { $Binary = Join-Path $project $(if ($Testing) { 'dist\MD-Reader-Test.exe' } else { "dist\Dokvi-$Version.exe" }) }
$Binary = (Resolve-Path -LiteralPath $Binary).Path
$downloadFolder = Join-Path $project 'tools\installer-downloads'
New-Item -ItemType Directory -Force -Path $downloadFolder | Out-Null
function Get-VerifiedDownload([string]$Url, [string]$Path, [string]$Publisher, [string]$Hash) {
  if (-not (Test-Path -LiteralPath $Path)) { Invoke-WebRequest -UseBasicParsing $Url -OutFile $Path }
  $signature = Get-AuthenticodeSignature -LiteralPath $Path
  if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch $Publisher) { throw "Invalid publisher signature: $Path" }
  if ($Hash) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($Path)
    try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') } finally { $stream.Dispose(); $sha.Dispose() }
    if ($actual -ne $Hash) { throw "Download checksum mismatch: $Path" }
  }
}
$compilerFolder = Join-Path $project 'tools\inno'
$compiler = Join-Path $compilerFolder 'ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
  $download = Join-Path $downloadFolder 'innosetup-7.1.1-x64.exe'
  Get-VerifiedDownload 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' $download 'O=Pyrsys B.V.' '8A5A6711C3766A6E5FB74F280BA0E3551E719CE94538D901FDBD1F8F9920B3F0'
  $process = Start-Process -FilePath $download -ArgumentList @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS','/TASKS=""',('/DIR="' + $compilerFolder + '"')) -WindowStyle Hidden -PassThru -Wait
  if ($process.ExitCode -ne 0) { throw "Inno Setup installation failed: $($process.ExitCode)" }
}
$bootstrapper = Join-Path $downloadFolder 'MicrosoftEdgeWebview2Setup.exe'
Get-VerifiedDownload 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' $bootstrapper 'O=Microsoft Corporation' ''
# Use the exact approved vector-derived icons, with no stock wizard illustration.
Copy-Item -LiteralPath (Join-Path $project 'ui\icon.png') -Destination (Join-Path $PSScriptRoot 'setup-icon.png')
Copy-Item -LiteralPath (Join-Path $project 'ui\icon-dark.png') -Destination (Join-Path $PSScriptRoot 'setup-icon-dark.png')
& (Join-Path $PSScriptRoot 'artwork.ps1')
$arguments = @('/Qp', "/DAppVersion=$Version", "/DAppBinary=$Binary")
if ($Testing) { $arguments += '/DInstallTest' }
if ($SignCommand) { $arguments += '/DSigning'; $arguments += "/Sdokvi=$SignCommand" }
$arguments += (Join-Path $PSScriptRoot 'Dokvi.iss')
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
$name = if ($Testing) { "Dokvi-Test-Setup-$Version.exe" } else { "Dokvi-Setup-$Version.exe" }
Get-Item -LiteralPath (Join-Path $project "dist\$name") | Select-Object FullName,Length
