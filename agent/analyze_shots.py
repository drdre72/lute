"""Analyze captured screenshots with pixel statistics."""
from PIL import Image
import collections, os

scrap = 'C:/Users/Shadow/Documents/lute/scrap'
names = ['south_gate','plaza_center',
         'market_orbit_000','market_orbit_045','market_orbit_090','market_orbit_135',
         'market_orbit_180','market_orbit_225','market_orbit_270','market_orbit_315']

for name in names:
    path = os.path.join(scrap, name + '.png')
    if not os.path.exists(path):
        continue
    img = Image.open(path)
    pixels = list(img.getdata())
    counts = collections.Counter(pixels)
    unique = len(counts)
    total = len(pixels)
    # Brightness buckets
    bright = sum(1 for p in pixels if sum(p[:3])/3 > 200)
    mid = sum(1 for p in pixels if 100 < sum(p[:3])/3 <= 200)
    dark = sum(1 for p in pixels if sum(p[:3])/3 <= 100)
    top3 = counts.most_common(3)
    print(f'{name:25s}: unique={unique:5d} bright={bright/total:.0%} mid={mid/total:.0%} dark={dark/total:.0%}')
    for color, count in top3:
        pct = count / total
        print(f'  RGB{color[:3]} : {pct:.0%}')
