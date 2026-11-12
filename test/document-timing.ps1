param([string]$OutputDirectory = '')
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testExe = Join-Path $project 'dist\MD-Reader-Test.exe'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project ('quality\logs\document-timing-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($scenario in @('plain', 'large', 'sample')) {
  $profile = Join-Path $project ('dist\test-data\document-timing-' + $scenario + '-' + [DateTime]::UtcNow.Ticks)
  New-Item -ItemType Directory -Path $profile | Out-Null
  $path = Join-Path $profile ($scenario + '.md')
  if ($scenario -eq 'plain') { [IO.File]::WriteAllText($path, "# Проверка открытия`n`nОбычный документ: русский текст и English.`n`n- Один`n- Два`n", $utf8) }
  if ($scenario -eq 'large') {
    $content = [Text.StringBuilder]::new()
    foreach ($i in 0..1199) {
      [void]$content.Append("## Section $i`n`n")
      [void]$content.Append(('Document text. **Important point** and [link](#section-0). ' * 6))
      [void]$content.Append("`n`n")
    }
    [IO.File]::WriteAllText($path, $content.ToString(), $utf8)
  }
  $arguments = @(('--session-root="' + $profile + '"'), ('--document-timing=' + $scenario))
  if ($scenario -ne 'sample') { $arguments += '--timing-file="' + $path + '"' }
  $p = Start-Process -FilePath $testExe -ArgumentList $arguments -PassThru
  try {
    $limit = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path -LiteralPath (Join-Path $profile 'empty-ready.json'))) {
      if ($p.HasExited -or [DateTime]::UtcNow -gt $limit) { throw "Empty window did not initialize: $scenario" }
      Start-Sleep -Milliseconds 50
    }
    [IO.File]::WriteAllText((Join-Path $profile 'open.signal'), '')
    $limit = [DateTime]::UtcNow.AddSeconds(125)
    $resultPath = Join-Path $profile 'document-timing.json'
    while (-not (Test-Path -LiteralPath $resultPath)) {
      if ($p.HasExited -or [DateTime]::UtcNow -gt $limit) { throw "No timing result: $scenario" }
      Start-Sleep -Milliseconds 100
    }
    $result = Get-Content -LiteralPath $resultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $result | Add-Member -NotePropertyName profile -NotePropertyValue $profile
    $result | Add-Member -NotePropertyName testExeSha256 -NotePropertyValue (Get-FileHash -LiteralPath $testExe -Algorithm SHA256).Hash
    if ($scenario -ne 'sample') {
      $result | Add-Member -NotePropertyName fileBytes -NotePropertyValue (Get-Item -LiteralPath $path).Length
      $result | Add-Member -NotePropertyName fileSha256 -NotePropertyValue (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    } else {
      $samplePath = Get-ChildItem -LiteralPath (Join-Path $profile 'assets') -Filter sample.md -Recurse | Select-Object -First 1 -ExpandProperty FullName
      $result | Add-Member -NotePropertyName fileBytes -NotePropertyValue (Get-Item -LiteralPath $samplePath).Length
      $result | Add-Member -NotePropertyName fileSha256 -NotePropertyValue (Get-FileHash -LiteralPath $samplePath -Algorithm SHA256).Hash
    }
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $OutputDirectory ($scenario+'.json')) -Encoding UTF8
    if (-not $result.ok) { throw "Document assertion failed: $scenario $($result.error)" }
    Copy-Item -LiteralPath (Join-Path $profile 'document-ready.png') -Destination (Join-Path $OutputDirectory ($scenario+'.png'))
    if ($scenario -eq 'sample') { Copy-Item -LiteralPath (Join-Path $profile 'formulas-ready.png') -Destination (Join-Path $OutputDirectory 'sample-formulas.png') }
    $result | Select-Object scenario, revealLowerMs, revealUpperMs, verifiedUpperMs, fileBytes | ConvertTo-Json
  } finally {
    [IO.File]::WriteAllText((Join-Path $profile 'close.signal'), '')
    if (-not $p.WaitForExit(10000)) { $p.Kill(); throw 'Timing process did not close' }
  }
}
