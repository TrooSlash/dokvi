param([switch]$PublicSource, [string]$OutputDirectory)
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
foreach ($item in @('host','src','test','scripts','ui','docs','site','.github','package.json','package-lock.json','build.mjs','README.md','README.txt','CHANGELOG.md','LICENSE','DESIGN.md','.gitignore')) {
  Copy-Item -LiteralPath (Join-Path $project $item) -Destination $stage -Recurse
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
Copy-Item -LiteralPath (Join-Path $project 'ui\document.ico') -Destination (Join-Path $outputs 'Dokvi-Markdown.ico')
Copy-Item -LiteralPath (Join-Path $project 'README.txt') -Destination (Join-Path $outputs "Dokvi-$version-readme.txt")
Copy-Item -LiteralPath (Join-Path $project 'LICENSE') -Destination (Join-Path $outputs "Dokvi-$version-LICENSE.txt")
$release = Join-Path $outputs "Dokvi-$version.exe"
"$((Get-FileHash -LiteralPath $release -Algorithm SHA256).Hash)  Dokvi-$version.exe" | Set-Content -LiteralPath (Join-Path $outputs "Dokvi-$version-SHA256.txt") -Encoding ASCII
Get-Item -LiteralPath $archive,(Join-Path $outputs "Dokvi-$version.exe") | Select-Object Name,Length
