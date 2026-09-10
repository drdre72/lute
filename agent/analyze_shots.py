"""
analyze_shots.py — Pixel-histogram sanity check for captured screenshots.

Scans the scrap/ directory for all .png files and reports brightness
ratios, dominant colors, and whether each frame is likely usable
(not just sky, not a failed capture).

Usage:
  python analyze_shots.py                    # analyze all PNGs in scrap/
  python analyze_shots.py market_visual      # analyze a specific file (without .png)
  python analyze_shots.py --min-size 10000   # skip files smaller than 10KB
"""
import argparse, os, collections, glob

from vision_lib import SCRAP

def analyze(path):
    """Analyze a single screenshot. Returns (ok, stats_dict)."""
    from PIL import Image
    img = Image.open(path).convert('RGB')
    # Resize to small thumbnail for fast pixel statistics
    img = img.resize((100, 100))
    pixels = list(img.getdata())
    total = len(pixels)
    bright = sum(1 for p in pixels if sum(p[:3]) / 3 > 200)
    mid = sum(1 for p in pixels if 100 < sum(p[:3]) / 3 <= 200)
    dark = sum(1 for p in pixels if sum(p[:3]) / 3 <= 100)
    counts = collections.Counter(pixels)
    top3 = counts.most_common(3)
    dominant_pct = top3[0][1] / total if top3 else 1.0
    ok = dominant_pct < 0.80
    return ok, {
        'total': total,
        'bright_pct': bright / total,
        'mid_pct': mid / total,
        'dark_pct': dark / total,
        'dominant_pct': dominant_pct,
        'top3': top3,
    }

def label_color(r, g, b):
    """Human-readable label for a dominant color."""
    if r > 240 and g > 240 and b > 240:
        return 'white/clouds'
    elif abs(r - 85) < 10 and abs(g - 118) < 10 and abs(b - 133) < 10:
        return 'sky blue'
    elif r > 180 and g > 180 and b > 180:
        return 'light grey (stone/plaza?)'
    elif r > 130 and g < 140 and b < 120:
        return 'brown (wood?)'
    elif r < 100 and g < 100 and b < 100:
        return 'dark (shadow/door?)'
    elif r > 150 and g > 100 and b < 80:
        return 'warm/torch light'
    return f'RGB({r},{g},{b})'

def main():
    parser = argparse.ArgumentParser(description='Analyze screenshots in scrap/')
    parser.add_argument('name', nargs='?', default=None,
                        help='Specific file to analyze (without .png extension)')
    parser.add_argument('--min-size', type=int, default=1000,
                        help='Skip files smaller than this many bytes')
    args = parser.parse_args()

    if args.name:
        path = os.path.join(SCRAP, f'{args.name}.png')
        if not os.path.exists(path):
            print(f'File not found: {path}')
            return
        files = [path]
    else:
        files = sorted(glob.glob(os.path.join(SCRAP, '*.png')))
        files = [f for f in files if os.path.getsize(f) >= args.min_size]

    if not files:
        print('No screenshots found in scrap/')
        return

    print(f'Analyzing {len(files)} screenshot(s) in {SCRAP}\n')
    for path in files:
        name = os.path.basename(path)
        ok, stats = analyze(path)
        status = 'OK' if ok else 'BAD (likely sky/empty)'
        print(f'{name:35s}: [{status}] bright={stats["bright_pct"]:.0%} '
              f'mid={stats["mid_pct"]:.0%} dark={stats["dark_pct"]:.0%} '
              f'dominant={stats["dominant_pct"]:.0%}')
        for color, count in stats['top3']:
            r, g, b = color[:3]
            pct = count / stats['total']
            print(f'  RGB({r:3d},{g:3d},{b:3d}) {pct:3.0%}  {label_color(r, g, b)}')
        print()

if __name__ == '__main__':
    main()
