"""
sbox_camera.py — Camera observation tool for the Lute project.

Teleports the player or Merlyn to a vantage point and captures a
screenshot. No vision model — just capture and save.

Use --via player to move the Player Controller (camera follows).
Use --via merlyn to move Merlyn and capture from his Eyes camera.

Usage:
  python sbox_camera.py --look 15748,10236,0 --out south_gate
  python sbox_camera.py --orbit 15748,15748,0 --out market --via player
  python sbox_camera.py --pos "15748,9500,100" --angles "-10,0,0" --out custom
"""
import argparse, os, sys, time

from vision_lib import VisionLib, vantage, save_img, pixel_check

def main():
    parser = argparse.ArgumentParser(description='S&Box camera observation via teleport')
    parser.add_argument('--look', help='Target to look at "x,y,z"')
    parser.add_argument('--orbit', help='Orbit around point "x,y,z" (8 angles)')
    parser.add_argument('--pos', help='Direct position "x,y,z"')
    parser.add_argument('--angles', help='Look angles "pitch,yaw,roll" (player mode only)')
    parser.add_argument('--out', default='capture', help='Output filename prefix')
    parser.add_argument('--via', default='player', choices=['merlyn', 'player'],
                        help='Camera source: player (main camera) or merlyn (Eyes)')
    parser.add_argument('--width', type=int, default=1280)
    parser.add_argument('--height', type=int, default=720)
    parser.add_argument('--distance', type=float, default=1500)
    parser.add_argument('--height-offset', type=float, default=400)
    parser.add_argument('--check', action='store_true',
                        help='Run pixel sanity check on each capture')
    parser.add_argument('--flash', action='store_true',
                        help='Auto-add a temporary light if scene is too dark (>40%% dark pixels)')
    parser.add_argument('--flash-threshold', type=float, default=0.40,
                        help='Dark pixel ratio that triggers flash (default 0.40)')
    parser.add_argument('--flash-radius', type=float, default=2000,
                        help='Flash light radius in world units (default 2000 = ~50m)')
    args = parser.parse_args()

    vl = VisionLib()

    if args.orbit:
        tx, ty, tz = [float(x) for x in args.orbit.split(',')]
        print(f'Orbiting ({tx},{ty},{tz}) via {args.via} — 8 angles')
        for i in range(8):
            yaw = i * 45
            pos, angles = vantage(tx, ty, tz, args.distance, args.height_offset, yaw)
            vl.teleport(pos, angles=angles, via=args.via)
            time.sleep(0.5)
            img = vl.capture(via=args.via, width=args.width, height=args.height,
                             flash=args.flash, flash_threshold=args.flash_threshold,
                             flash_radius=args.flash_radius, flash_pos=(tx, ty, tz))
            if img:
                path = save_img(img, f'{args.out}_orbit_{yaw:03d}')
                extra = ''
                if args.check:
                    ok, stats = pixel_check(img)
                    extra = f' [{"OK" if ok else "BAD"}]' if not ok else ''
                print(f'  {yaw:3d}d: {len(img)} bytes -> {path}{extra}')
            else:
                print(f'  {yaw:3d}d: CAPTURE FAILED')
    elif args.look:
        tx, ty, tz = [float(x) for x in args.look.split(',')]
        pos, angles = vantage(tx, ty, tz, args.distance, args.height_offset)
        vl.teleport(pos, angles=angles, via=args.via)
        time.sleep(0.5)
        img = vl.capture(via=args.via, width=args.width, height=args.height,
                         flash=args.flash, flash_threshold=args.flash_threshold,
                         flash_radius=args.flash_radius, flash_pos=(tx, ty, tz))
        if img:
            path = save_img(img, args.out)
            print(f'Captured {args.out} ({len(img)} bytes) looking at ({tx},{ty},{tz}) via {args.via}')
    elif args.pos:
        vl.teleport(args.pos, angles=args.angles, via=args.via)
        time.sleep(0.5)
        img = vl.capture(via=args.via, width=args.width, height=args.height)
        if img:
            path = save_img(img, args.out)
            print(f'Captured {args.out} ({len(img)} bytes) at {args.pos} via {args.via}')
    else:
        img = vl.capture(via=args.via, width=args.width, height=args.height)
        if img:
            path = save_img(img, args.out)
            print(f'Captured {args.out} ({len(img)} bytes) via {args.via}')

if __name__ == '__main__':
    main()
