import argparse
import json
import re
import shutil
import subprocess
from pathlib import Path

import pdfplumber
from PIL import Image, ImageDraw


def check(folder, case):
    errors = []
    with pdfplumber.open(folder / case['pdf']) as pdf:
        chars = [char for page in pdf.pages for char in page.chars]
        text = re.sub(r'\s+', '', ''.join(c['text'] for c in chars))
        missing = [value for value in case['expected'] if re.sub(r'\s+', '', value) not in text]
        if missing:
            errors.append(f'{len(missing)} missing values')
        outside = []
        for number, page in enumerate(pdf.pages, 1):
            for obj in page.chars + page.images:
                if obj.get('text', 'image').strip() and (obj['x0'] < -0.5 or obj['x1'] > page.width + .5 or obj['top'] < -.5 or obj['bottom'] > page.height + .5):
                    outside.append({'page': number, 'text': obj.get('text', 'image'), 'bounds': [obj[key] for key in ('x0', 'top', 'x1', 'bottom')]})
        if outside:
            errors.append(f'{len(outside)} text/image bounds outside page')
        if not case['restored']:
            errors.append('Editor state not restored')
        diagnostics = json.loads(case['diagnostics'])
        kind = case['scenario'].rsplit('-', 1)[0]
        if kind == 'images-landscape':
            kind = 'images'
        if kind in ('math', 'mixed') and diagnostics['math'] < 4:
            errors.append('Math not rendered')
        if kind in ('diagrams', 'mixed') and diagnostics['svg'] != (4 if kind == 'diagrams' else 1):
            errors.append('Diagram SVG missing')
        if kind in ('images', 'mixed', 'timeout') and not all(i['loaded'] for i in diagnostics['images']):
            errors.append('Image not loaded')
        if kind == 'errors' and diagnostics['diagramErrors'] != 1:
            errors.append('Diagram error notice missing or duplicated')
        images = [{'page': n, 'width': i['width'], 'height': i['height']} for n, p in enumerate(pdf.pages, 1) for i in p.images]
        if kind in ('images', 'mixed', 'timeout') and len(images) < (3 if kind == 'images' else 1):
            errors.append('Raster image missing from PDF')
        return {'scenario': case['scenario'], 'ok': not errors, 'pages': len(pdf.pages), 'missing': missing, 'outside': outside[:15], 'images': images, 'errors': errors}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('folder', type=Path)
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args()
    manifest = json.loads((args.folder / 'manifest.json').read_text(encoding='utf-8-sig'))
    results = [check(args.folder, case) for case in manifest['cases']]
    if args.render:
        images = args.folder / 'pages'
        images.mkdir(exist_ok=True)
        for case in manifest['cases']:
            subprocess.run([shutil.which('pdftoppm'), '-r', '96', '-png', str(args.folder / case['pdf']), str(images / case['scenario'])], check=True, capture_output=True)
        files = sorted(images.glob('*.png'))
        sheet = Image.new('RGB', (1440, ((len(files) + 3) // 4) * 545), '#dddddd')
        draw = ImageDraw.Draw(sheet)
        for index, file in enumerate(files):
            x, y = index % 4 * 360 + 8, index // 4 * 545 + 8
            draw.text((x, y), file.stem, fill='black')
            with Image.open(file) as picture:
                picture.thumbnail((344, 510))
                sheet.paste(picture, (x, y + 20))
        sheet.save(args.folder / 'contact-sheet.png')
    report = {'ok': manifest['ok'] and len(results) == 10 and manifest.get('timeoutRecovered') and all(r['ok'] for r in results), 'cases': results}
    (args.folder / 'pdf-checks.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'ok': report['ok'], 'cases': [{k: r[k] for k in ('scenario', 'pages', 'errors')} for r in results]}, indent=2))
    raise SystemExit(0 if report['ok'] else 1)


if __name__ == '__main__':
    main()
