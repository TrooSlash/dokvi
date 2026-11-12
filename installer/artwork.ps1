$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
& (Join-Path $PSScriptRoot '..\scripts\icons.ps1')
foreach ($dark in @($false, $true)) {
  $suffix = if ($dark) { '-dark' } else { '' }
  $canvas = [Drawing.Bitmap]::new(328,628)
  $graphics = [Drawing.Graphics]::FromImage($canvas)
  $background = if ($dark) { [Drawing.Color]::FromArgb(41,43,46) } else { [Drawing.Color]::FromArgb(240,241,243) }
  $foreground = if ($dark) { [Drawing.Color]::FromArgb(242,239,233) } else { [Drawing.Color]::FromArgb(36,35,34) }
  $graphics.Clear($background)
  $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $graphics.TextRenderingHint = [Drawing.Text.TextRenderingHint]::AntiAliasGridFit
  $icon = [DokviIconBuilder]::Render(128, $dark, $false)
  $graphics.DrawImage($icon,100,188,128,128)
  $font = [Drawing.Font]::new('Segoe UI',32,[Drawing.FontStyle]::Regular,[Drawing.GraphicsUnit]::Pixel)
  $brush = [Drawing.SolidBrush]::new($foreground)
  $format = [Drawing.StringFormat]::new()
  $format.Alignment = [Drawing.StringAlignment]::Center
  $graphics.DrawString('Dokvi',$font,$brush,[Drawing.RectangleF]::new(0,340,328,60),$format)
  $canvas.Save((Join-Path $PSScriptRoot "setup-panel$suffix.png"),[Drawing.Imaging.ImageFormat]::Png)
  $format.Dispose(); $brush.Dispose(); $font.Dispose(); $icon.Dispose(); $graphics.Dispose(); $canvas.Dispose()
}
