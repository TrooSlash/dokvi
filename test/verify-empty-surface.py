"""Compare native and WebView welcome text in quality-checks captures (96 DPI)."""
import json
import sys
from pathlib import Path

from PIL import Image, ImageChops


folder = Path(sys.argv[1])
results = []
for width, height in ((1120, 820), (620, 480)):
    for language in ("ru", "en"):
        for theme in ("light", "dark"):
            name = f"{language}-{theme}-{width}"
            native = Image.open(folder / f"empty-{name}.png").convert("RGB")
            web = Image.open(folder / f"web-empty-{name}.png").convert("RGB")
            assert native.size == web.size == (width, height), name
            for label, y, h in (("title", 0, 32), ("hint", 39, 26)):
                top = max(105, height // 2 - 80) + y
                crop = (width // 2 - 285, top, width // 2 + 285, top + h)
                boxes = []
                densities = []
                for source in (native, web):
                    text = source.crop(crop)
                    background = Image.new("RGB", text.size, text.getpixel((0, 0)))
                    diff = ImageChops.difference(text, background).convert("L")
                    boxes.append(diff.point(lambda p: 255 if p > 50 else 0).getbbox())
                    densities.append(sum(diff.get_flattened_data()))
                assert all(boxes), (name, label, "missing text")
                displacement = max(abs(a - b) for a, b in zip(*boxes))
                assert displacement <= 1, (name, label, boxes)
                results.append({"case": name, "text": label, "bounds": boxes,
                                "maxPixelDisplacement": displacement,
                                "inkRatio": round(densities[0] / densities[1], 4)})
report = {"ok": True, "checks": results,
          "note": "Glyph bounds are compared within one pixel; rasterizers may differ at antialiased edges."}
(folder / "text-comparison.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(json.dumps(report, indent=2))
