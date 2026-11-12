import argparse
import json
import re
import shutil
import subprocess
from pathlib import Path

import pdfplumber


def compact(text):
    return re.sub(r"\s+", "", text)


def check_pdf(folder, case):
    errors = []
    with pdfplumber.open(folder / case["pdf"]) as pdf:
        chars = [char for page in pdf.pages for char in page.chars]
        text = compact("".join(char["text"] for char in chars))
        missing = [value for value in case["expected"] if compact(value) not in text]
        if missing:
            errors.append(f"Missing {len(missing)} expected values")
        outside = []
        for index, page in enumerate(pdf.pages, 1):
            for char in page.chars:
                if char["text"].strip() and (char["x0"] < -0.5 or char["x1"] > page.width + 0.5 or char["top"] < -0.5 or char["bottom"] > page.height + 0.5):
                    outside.append({"page": index, "text": char["text"], "x0": char["x0"], "x1": char["x1"], "top": char["top"], "bottom": char["bottom"]})
        if outside:
            errors.append(f"{len(outside)} characters outside page bounds")
        alignment = {}
        orientation = "landscape" if pdf.pages[0].width > pdf.pages[0].height else "portrait"
        expected_orientation = "portrait" if case["scenario"] in ("compact-read", "tall-row-read") else "landscape"
        if orientation != expected_orientation:
            errors.append(f"Expected {expected_orientation}, got {orientation}")
        if case["scenario"] == "compact-read":
            words = {word["text"]: word for page in pdf.pages for word in page.extract_words()}
            for names, direction in [(('Left', 'L1', 'L10'), 'left'), (('Center', 'C1', 'C10'), 'center'), (('Right', 'R1', 'R10'), 'right'), (('Default', 'D1', 'D10'), 'left')]:
                positions = []
                for name in names:
                    if name not in words:
                        errors.append(f"Alignment label missing: {name}")
                        continue
                    word = words[name]
                    positions.append(word['x0'] if direction == 'left' else word['x1'] if direction == 'right' else (word['x0'] + word['x1']) / 2)
                spread = max(positions) - min(positions) if positions else None
                alignment[names[0]] = {"direction": direction, "spreadPoints": spread}
                if spread is None or spread > 1:
                    errors.append(f"Incorrect {direction} alignment: {names[0]}")
        else:
            flat = [(letter, char) for char in chars for letter in char["text"] if not letter.isspace()]
            sequence = "".join(letter for letter, _ in flat)
            def bounds(value):
                start = sequence.find(value)
                if start < 0:
                    return []
                lines = {}
                for _, char in flat[start:start + len(value)]:
                    key = round(char["top"], 1)
                    lines.setdefault(key, []).append(char)
                return [(min(c["x0"] for c in line), max(c["x1"] for c in line)) for line in lines.values()]
            for value in case["expected"]:
                match = re.fullmatch(r"R\d{3}C(\d{2}).*", value)
                if not match:
                    continue
                col = int(match[1])
                header, lines = bounds(f"H{col:02d}"), bounds(value)
                if not header or not lines:
                    continue
                direction = (col - 1) % 3
                project = lambda box: box[0] if direction == 0 else (box[0] + box[1]) / 2 if direction == 1 else box[1]
                reference = project((min(box[0] for box in header), max(box[1] for box in header)))
                spread = max(abs(project(box) - reference) for box in lines)
                if spread > 1:
                    errors.append(f"Incorrect column alignment for {value}: {spread:.2f} pt")
            if case["scenario"] == "multipage-read":
                for page_number, page in enumerate(pdf.pages, 1):
                    page_text = compact("".join(char["text"] for char in page.chars))
                    if re.search(r"R\d{3}C\d{2}", page_text) and not all(f"H{col:02d}" in page_text for col in range(1, 13)):
                        errors.append(f"Table header missing on page {page_number}")
        return {"scenario": case["scenario"], "ok": not errors, "pages": len(pdf.pages), "orientation": orientation, "characters": len(chars), "missing": missing, "outside": outside[:12], "alignment": alignment, "errors": errors}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("folder", type=Path)
    parser.add_argument("--render", action="store_true")
    args = parser.parse_args()
    manifest = json.loads((args.folder / "manifest.json").read_text(encoding="utf-8-sig"))
    results = [check_pdf(args.folder, case) for case in manifest["cases"]]
    if args.render:
        executable = shutil.which("pdftoppm")
        if not executable:
            raise RuntimeError("Poppler pdftoppm is required for visual inspection")
        images = args.folder / "pages"
        images.mkdir(exist_ok=True)
        for case in manifest["cases"]:
            subprocess.run([executable, "-r", "96", "-png", str(args.folder / case["pdf"]), str(images / case["scenario"])], check=True, capture_output=True)
    report = {"ok": manifest["ok"] and all(result["ok"] for result in results), "cases": results}
    (args.folder / "pdf-checks.json").write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"ok": report["ok"], "cases": [{key: result[key] for key in ("scenario", "ok", "pages", "errors")} for result in results]}, indent=2))
    raise SystemExit(0 if report["ok"] else 1)


if __name__ == "__main__":
    main()
