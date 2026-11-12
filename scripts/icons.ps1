# Rasterize the two circular caps in src/icon.svg without an external image library.
# Only the Windows shell icon gets a fine light rim and an opaque reading line,
# keeping the black symbol visible on both light and dark desktop backgrounds.
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Add-Type -AssemblyName System.Drawing
if (-not ('DokviIconBuilder' -as [type])) {
  Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class DokviIconBuilder {
  public static Bitmap Render(int size, bool dark, bool shell) {
    const int oversample = 4;
    var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var large = new Bitmap(size * oversample, size * oversample, PixelFormat.Format32bppArgb)) {
      using (var g = Graphics.FromImage(large)) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size * oversample / 48f, size * oversample / 48f);
        if (shell) using (var backing = new SolidBrush(Color.FromArgb(242, 239, 233))) g.FillEllipse(backing, 2.5f, 2.5f, 43, 43);
        // SVG endpoint arcs: radius 21, chord endpoints x=3.22 / 44.78.
        float rise = (float)Math.Sqrt(21 * 21 - 20.78 * 20.78);
        float angle = (float)(Math.Asin(rise / 21) * 180 / Math.PI);
        using (var caps = new GraphicsPath())
        using (var body = new SolidBrush(dark ? Color.FromArgb(242, 239, 233) : Color.FromArgb(36, 35, 34)))
        using (var accent = new SolidBrush(Color.FromArgb(215, 65, 43))) {
          caps.AddArc(3, rise, 42, 42, 180 + angle, 180 - 2 * angle);
          caps.CloseFigure();
          caps.StartFigure();
          caps.AddArc(3, 6 - rise, 42, 42, angle, 180 - 2 * angle);
          caps.CloseFigure();
          g.FillPath(body, caps);
          g.FillRectangle(accent, 21, 21, 18, 6);
        }
      }
      using (var g = Graphics.FromImage(result)) {
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(large, new Rectangle(0, 0, size, size), 0, 0, large.Width, large.Height, GraphicsUnit.Pixel);
      }
    }
    return result;
  }
  // Exact vector geometry from src/document.svg, including outlined MD glyphs.
  public static Bitmap RenderDocument(int size) {
    var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var large = new Bitmap(size * 4, size * 4, PixelFormat.Format32bppArgb)) {
      using (var g = Graphics.FromImage(large))
      using (var paper = new SolidBrush(Color.FromArgb(252, 252, 250)))
      using (var ink = new SolidBrush(Color.FromArgb(36, 35, 34)))
      using (var accent = new SolidBrush(Color.FromArgb(215, 65, 43)))
      using (var outline = new Pen(Color.FromArgb(174, 177, 172), 1.5f))
      using (var line = new Pen(Color.FromArgb(146, 150, 143), 2)) {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size * 4 / 48f, size * 4 / 48f);
        outline.LineJoin = LineJoin.Round;
        PointF[] page = { new PointF(14,3), new PointF(32,3), new PointF(43,14), new PointF(43,45), new PointF(14,45) };
        g.FillPolygon(paper, page); g.DrawPolygon(outline, page);
        g.DrawLines(outline, new[] { new PointF(32,3), new PointF(32,14), new PointF(43,14) });
        g.DrawLine(line, 20,19,36,19);
        g.FillRectangle(ink, 3,24,34,19);
        g.FillRectangle(accent, 3,24,3,19);
        g.TranslateTransform(8, 28); g.ScaleTransform(.96f, .96f);
        using (var letters = new GraphicsPath(FillMode.Alternate)) {
          letters.AddPolygon(new[] { new PointF(0,12), new PointF(0,0), new PointF(3,0), new PointF(6,5), new PointF(9,0), new PointF(12,0), new PointF(12,12), new PointF(9,12), new PointF(9,5), new PointF(6,10), new PointF(3,5), new PointF(3,12) });
          letters.StartFigure(); letters.AddLine(15,0,20,0);
          letters.AddBezier(20,0,24,0,26,2,26,6); letters.AddBezier(26,6,26,10,24,12,20,12);
          letters.AddLine(20,12,15,12); letters.CloseFigure();
          letters.StartFigure(); letters.AddLine(18,3,18,9); letters.AddLine(18,9,20,9);
          letters.AddBezier(20,9,22,9,23,8,23,6); letters.AddBezier(23,6,23,4,22,3,20,3);
          letters.CloseFigure(); g.FillPath(paper, letters);
        }
      }
      using (var g = Graphics.FromImage(result)) {
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(large, new Rectangle(0,0,size,size), 0,0,large.Width,large.Height, GraphicsUnit.Pixel);
      }
    }
    return result;
  }
  static void WriteIcon(string path, bool document) {
    int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
    var frames = new byte[sizes.Length][];
    for (int i = 0; i < sizes.Length; i++) {
      using (var bitmap = document ? RenderDocument(sizes[i]) : Render(sizes[i], false, true))
      using (var stream = new MemoryStream()) {
        bitmap.Save(stream, ImageFormat.Png); frames[i] = stream.ToArray();
      }
    }
    using (var file = File.Create(path))
    using (var writer = new BinaryWriter(file)) {
      writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
      uint offset = (uint)(6 + sizes.Length * 16);
      for (int i = 0; i < sizes.Length; i++) {
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
        writer.Write((byte)0); writer.Write((byte)0);
        writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write((uint)frames[i].Length); writer.Write(offset);
        offset += (uint)frames[i].Length;
      }
      foreach (var frame in frames) writer.Write(frame);
    }
  }
  public static void Build(string folder) {
    using (var light = Render(64, false, false)) light.Save(Path.Combine(folder, "icon.png"), ImageFormat.Png);
    using (var dark = Render(64, true, false)) dark.Save(Path.Combine(folder, "icon-dark.png"), ImageFormat.Png);
    using (var document = RenderDocument(256)) document.Save(Path.Combine(folder, "document.png"), ImageFormat.Png);
    WriteIcon(Path.Combine(folder, "icon.ico"), false);
    WriteIcon(Path.Combine(folder, "document.ico"), true);
  }
}
'@
}
$ui = Join-Path $project 'ui'
New-Item -ItemType Directory -Force -Path $ui | Out-Null
foreach ($name in @('icon.svg', 'icon-dark.svg', 'document.svg')) {
  Copy-Item -LiteralPath (Join-Path $project "src\$name") -Destination (Join-Path $ui $name)
}
[DokviIconBuilder]::Build($ui)
