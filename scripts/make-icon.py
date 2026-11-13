# Generates the product icon set from one geometric definition (docs/spec/ui/brand/README.md, UI-TOK-043).
# Usage: python scripts/make-icon.py      Requires: Pillow (build-time only, not shipped).
# Output: docs/spec/ui/brand/  icon.svg, icon-dark.svg, icon-<size>.png, app.ico, _preview.png
import os
from PIL import Image, ImageDraw

OUT = os.path.join(os.path.dirname(__file__), '..', 'docs', 'spec', 'ui', 'brand')
LIGHT = dict(bg='#0F7B6C', fg='#FFFFFF', hi='#8FE3D6')     # for light surfaces / Windows shell
DARK  = dict(bg='#3FC1AE', fg='#06201C', hi='#0B4A41')     # for dark surfaces inside the IDE
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512, 1024]

def geometry(small):
    """1024 grid. Mark: a chevron (stem) + a long bar + a short bar = a prompt that reads as the letter F."""
    if small:   # 16-24 px: heavier strokes, slightly larger glyph
        return 136, [(236, 286), (446, 512), (236, 738)], [(540, 286), (800, 286)], [(580, 512), (750, 512)]
    return 104, [(250, 300), (450, 512), (250, 724)], [(520, 300), (790, 300)], [(560, 512), (730, 512)]

def svg(c):
    w, chev, top, mid = geometry(False)
    pts = ' '.join(f'{x},{y}' for x, y in chev)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" role="img" aria-label="Application icon">
  <rect width="1024" height="1024" rx="205" fill="{c['bg']}"/>
  <g fill="none" stroke-width="{w}" stroke-linecap="round" stroke-linejoin="round">
    <polyline points="{pts}" stroke="{c['fg']}"/>
    <line x1="{top[0][0]}" y1="{top[0][1]}" x2="{top[1][0]}" y2="{top[1][1]}" stroke="{c['fg']}"/>
    <line x1="{mid[0][0]}" y1="{mid[0][1]}" x2="{mid[1][0]}" y2="{mid[1][1]}" stroke="{c['hi']}"/>
  </g>
</svg>
'''

def render(size, c=LIGHT):
    ss = 8 if size <= 128 else 2
    n = size * ss; k = n / 1024
    img = Image.new('RGBA', (n, n), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, n - 1, n - 1], radius=round(205 * k), fill=c['bg'])
    w, chev, top, mid = geometry(size <= 24)
    def stroke(points, col):
        p = [(x * k, y * k) for x, y in points]
        d.line(p, fill=col, width=round(w * k), joint='curve')
        r = w * k / 2
        for x, y in p: d.ellipse([x - r, y - r, x + r, y + r], fill=col)
    stroke(chev, c['fg']); stroke(top, c['fg']); stroke(mid, c['hi'])
    return img.resize((size, size), Image.LANCZOS)

os.makedirs(OUT, exist_ok=True)
open(os.path.join(OUT, 'icon.svg'), 'w', encoding='utf-8', newline='\n').write(svg(LIGHT))
open(os.path.join(OUT, 'icon-dark.svg'), 'w', encoding='utf-8', newline='\n').write(svg(DARK))
imgs = {s: render(s) for s in SIZES}
for s, im in imgs.items(): im.save(os.path.join(OUT, f'icon-{s}.png'))
ico = [16, 20, 24, 32, 40, 48, 64, 128, 256]
imgs[256].save(os.path.join(OUT, 'app.ico'), format='ICO', sizes=[(s, s) for s in ico],
               append_images=[imgs[s] for s in ico if s != 256])
sheet = Image.new('RGBA', (900, 430), '#F4F6F5'); x = 20          # review sheet, not shipped
for s in [256, 128, 64, 48, 32, 24, 16]:
    sheet.paste(imgs[s], (x, 20), imgs[s]); x += s + 20
sheet.paste(Image.new('RGBA', (900, 130), '#1B1F1E'), (0, 300)); x = 20
for s in [96, 64, 48, 32, 24, 16]:
    im = render(s, DARK); sheet.paste(im, (x, 318), im); x += s + 20
sheet.save(os.path.join(OUT, '_preview.png'))
print('written', len(os.listdir(OUT)), 'files')
