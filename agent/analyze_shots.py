"""Analyze all captured screenshots."""
from PIL import Image
import collections, os

scrap = 'C:/Users/Shadow/Documents/lute/scrap'
targets = ['south_gate', 'plaza_center', 'south_moat', 'south_gate_close',
           'well_close', 'tower_close',
           'market_orbit_000', 'market_orbit_090', 'market_orbit_180', 'market_orbit_270']

for name in targets:
    path = os.path.join(scrap, name + '.png')
    if not os.path.exists(path):
        print(f'{name:25s}: NOT FOUND')
        continue
    img = Image.open(path)
    pixels = list(img.getdata())
    counts = collections.Counter(pixels)
    unique = len(counts)
    total = len(pixels)
    bright = sum(1 for p in pixels if sum(p[:3])/3 > 200)
    mid = sum(1 for p in pixels if 100 < sum(p[:3])/3 <= 200)
    dark = sum(1 for p in pixels if sum(p[:3])/3 <= 100)
    top3 = counts.most_common(3)
    print(f'{name:25s}: unique={unique:5d} bright={bright/total:.0%} mid={mid/total:.0%} dark={dark/total:.0%}')
    for color, count in top3:
        pct = count / total
        r, g, b = color[:3]
        label = ''
        if r > 240 and g > 240 and b > 240: label = 'white/clouds'
        elif abs(r-85) < 5 and abs(g-118) < 5 and abs(b-133) < 5: label = 'sky'
        elif r > 180 and g > 180 and b > 180: label = 'light grey (stone?)'
        elif r > 130 and g < 140 and b < 120: label = 'brown (wood?)'
        elif r < 100 and g < 100 and b < 100: label = 'dark'
        print(f'  RGB({r:3d},{g:3d},{b:3d}) {pct:3.0%}  {label}')
