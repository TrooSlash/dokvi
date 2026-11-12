param([switch]$IncludeInstaller, [switch]$PublicSource, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$workspace = [IO.Path]::GetFullPath((Join-Path $project '..\..'))
$outputs = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $workspace 'outputs' }
New-Item -ItemType Directory -Path $outputs -Force | Out-Null
$version = (Get-Content -LiteralPath (Join-Path $project 'package.json') -Raw | ConvertFrom-Json).version
$stage = Join-Path $project 'build-source'
if (Test-Path -LiteralPath $stage) {
  if ([IO.Path]::GetFullPath($stage) -ne (Join-Path $project 'build-source')) { throw 'Invalid staging path' }
  Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($item in @('host','src','test','scripts','installer','ui','docs','package.json','package-lock.json','build.mjs','README.md','README.txt','CHANGELOG.md','LICENSE','DESIGN.md','.gitignore')) {
  Copy-Item -LiteralPath (Join-Path $project $item) -Destination $stage -Recurse
}
if (-not $PublicSource) {
Copy-Item -LiteralPath (Join-Path $project 'BACKLOG.md') -Destination $stage
$quality = New-Item -ItemType Directory -Path (Join-Path $stage 'quality') -Force
foreach ($item in @('report.md','windows-qa-followup.md','resize-checks.json','input-focus-report.md','input-focus-checks.json','taskbar-focus-report.md','taskbar-focus-checks.json','release-report.md','release-checks.json','followup-report.md','followup-checks.json','remaining-report.md','remaining-checks.json','cpu-investigation.md','comment-review.json','checks.json','benchmarks')) {
  Copy-Item -LiteralPath (Join-Path $project "quality\$item") -Destination $quality.FullName -Recurse
}
Copy-Item -LiteralPath (Join-Path $project 'quality\table-layout-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\table-alignment-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\pdf-tables-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\pdf-content-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\control-2.7.8-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\ui-theme-2.7.9-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\empty-menu-2.7.10-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\refactor-2.7.11-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\reuse-2.7.12-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\native-menu-2.7.13-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\menu-consistency-2.7.14-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\shared-icons-2.7.15-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\chrome-controls-2.7.16-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\button-states-2.7.17-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\keyboard-navigation-2.7.18-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\dialog-keyboard-report.md') -Destination $quality.FullName
Copy-Item -LiteralPath (Join-Path $project 'quality\surface-review-2.7.12.md') -Destination $quality.FullName
$qualityLogs = New-Item -ItemType Directory -Path (Join-Path $quality.FullName 'logs') -Force
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\table-layout-2.7.5') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\table-alignment-2.7.6') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\pdf-tables-2.7.7') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\pdf-content-2.7.8') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\release-2.7.8-20261111-111424') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\ui-theme-2.7.9') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\empty-menu-2.7.10') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\refactor-2.7.11') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\reuse-2.7.12') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\native-menu-2.7.13') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\menu-consistency-2.7.14') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\shared-icons-2.7.15') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\chrome-controls-2.7.16') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\button-states-2.7.17') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\keyboard-navigation-2.7.18') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\dialog-keyboard') -Destination $qualityLogs.FullName -Recurse
Copy-Item -LiteralPath (Join-Path $project 'quality\logs\surface-review-2.7.12') -Destination $qualityLogs.FullName -Recurse
}
Add-Type -AssemblyName System.IO.Compression.FileSystem, System.IO.Compression
$archive = Join-Path $outputs "Dokvi-$version-source.zip"
if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
$zip = [IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
try {
  foreach ($file in Get-ChildItem -LiteralPath $stage -File -Recurse) {
    $entry = $file.FullName.Substring($stage.Length + 1).Replace('\','/')
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$entry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
  }
} finally { $zip.Dispose() }
Copy-Item -LiteralPath (Join-Path $project "dist\Dokvi-$version.exe") -Destination $outputs
if ($IncludeInstaller) { Copy-Item -LiteralPath (Join-Path $project "dist\Dokvi-Setup-$version.exe") -Destination $outputs }
Copy-Item -LiteralPath (Join-Path $project 'ui\document.ico') -Destination (Join-Path $outputs 'Dokvi-Markdown.ico')
Copy-Item -LiteralPath (Join-Path $project 'README.txt') -Destination (Join-Path $outputs "Dokvi-$version-readme.txt")
Copy-Item -LiteralPath (Join-Path $project 'LICENSE') -Destination (Join-Path $outputs "Dokvi-$version-LICENSE.txt")
$release = Join-Path $outputs "Dokvi-$version.exe"
"$((Get-FileHash -LiteralPath $release -Algorithm SHA256).Hash)  Dokvi-$version.exe" | Set-Content -LiteralPath (Join-Path $outputs "Dokvi-$version-SHA256.txt") -Encoding ASCII
Get-Item -LiteralPath $archive,(Join-Path $outputs "Dokvi-$version.exe") | Select-Object Name,Length
