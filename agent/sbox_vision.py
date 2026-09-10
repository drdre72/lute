"""
sbox_vision.py — Vision sub-agent using Merlyn's Eyes camera.

Teleports Merlyn to a vantage, captures a screenshot, sends it to the
local Moondream2 vision model, and returns a text description.

Usage:
  python sbox_vision.py --look 15748,10236,0 --out south_gate --ask "Describe the gate"
  python sbox_vision.py --orbit 15748,15748,0 --out market --ask "Describe the market"
  python sbox_vision.py --file scrap/eyes_south_gate.png --ask "What do you see?"
  python sbox_vision.py --look 15748,10236,0 --via player --ask "Describe the gate"
"""
import argparse, os, sys, time

from vision_lib import VisionLib, vantage, save_img, ask_vision, pixel_check, SCRAP

def main():
    parser = argparse.ArgumentParser(description='S&Box vision via Merlyn Eyes or Player camera')
    parser.add_argument('--look', help='Target "x,y,z" to look at')
    parser.add_argument('--orbit', help='Orbit point "x,y,z" (8 angles)')
    parser.add_argument('--file', help='Use existing screenshot file instead of capturing')
    parser.add_argument('--out', default='vision', help='Output name prefix')
    parser.add_argument('--ask', default='Describe what you see in this image.', help='Question')
    parser.add_argument('--via', default='merlyn', choices=['merlyn', 'player'],
                        help='Camera source: merlyn (Eyes) or player (main camera)')
    parser.add_argument('--distance', type=float, default=1000)
    parser.add_argument('--height-offset', type=float, default=200)
    parser.add_argument('--check', action='store_true',
                        help='Run pixel sanity check before asking vision (skip bad frames)')
    parser.add_argument('--flash', action='store_true',
                        help='Auto-add a temporary light if scene is too dark (>40%% dark pixels)')
    parser.add_argument('--flash-threshold', type=float, default=0.40,
                        help='Dark pixel ratio that triggers flash (default 0.40)')
    parser.add_argument('--flash-radius', type=float, default=2000,
                        help='Flash light radius in world units (default 2000 = ~50m)')
    args = parser.parse_args()

    vl = VisionLib()

    if args.file:
        img_path = args.file if os.path.isabs(args.file) else os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', args.file)
        if not os.path.exists(img_path):
            print(f'File not found: {img_path}')
            sys.exit(1)
        with open(img_path, 'rb') as f:
            img_bytes = f.read()
        print(f'Asking vision about {img_path}...')
        answer = ask_vision(img_bytes, args.ask)
        print(f'\n--- VISION ---\n{answer}\n')
        return

    if args.orbit:
        tx, ty, tz = [float(x) for x in args.orbit.split(',')]
        print(f'Orbiting ({tx},{ty},{tz}) via {args.via} — 8 angles')
        for i in range(8):
            yaw = i * 45
            pos, angles = vantage(tx, ty, tz, args.distance, args.height_offset, yaw)
            vl.teleport(pos, angles=angles, via=args.via)
            img = vl.capture(via=args.via, flash=args.flash,
                             flash_threshold=args.flash_threshold,
                             flash_radius=args.flash_radius)
            if not img:
                print(f'\n=== Angle {yaw} === CAPTURE FAILED')
                continue
            path = save_img(img, f'{args.out}_orbit_{yaw:03d}')
            if args.check:
                ok, stats = pixel_check(img)
                if not ok:
                    print(f'\n=== Angle {yaw} === SKIPPED (bad frame: {stats["dominant_pct"]:.0%} one color)')
                    continue
                if args.flash and stats['dark_pct'] > args.flash_threshold:
                    print(f'  [flash triggered: was {stats["dark_pct"]:.0%} dark]')
            print(f'\n=== Angle {yaw} === ({len(img)} bytes -> {path})')
            answer = ask_vision(img, args.ask)
            print(answer)
        return

    if args.look:
        tx, ty, tz = [float(x) for x in args.look.split(',')]
        pos, angles = vantage(tx, ty, tz, args.distance, args.height_offset, 180)
        vl.teleport(pos, angles=angles, via=args.via)
        img = vl.capture(via=args.via, flash=args.flash,
                         flash_threshold=args.flash_threshold,
                         flash_radius=args.flash_radius,
                         flash_pos=(tx, ty, tz))
        if not img:
            print('CAPTURE FAILED')
            sys.exit(1)
        path = save_img(img, args.out)
        print(f'Captured {len(img)} bytes -> {path}')
        if args.check:
            ok, stats = pixel_check(img)
            if not ok:
                print(f'Frame rejected: {stats["dominant_pct"]:.0%} single color — likely sky or empty')
                sys.exit(1)
            print(f'Pixel check OK: bright={stats["bright_pct"]:.0%} mid={stats["mid_pct"]:.0%} dark={stats["dark_pct"]:.0%}')
            if args.flash and stats['dark_pct'] > args.flash_threshold:
                print(f'  [flash was triggered for this dark scene]')
        answer = ask_vision(img, args.ask)
        print(f'\n--- VISION ---\n{answer}\n')
        return

    print('Use --look, --orbit, or --file')
    sys.exit(1)

if __name__ == '__main__':
    main()
