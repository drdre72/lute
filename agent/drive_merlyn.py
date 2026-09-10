"""
drive_merlyn.py — Poll Merlyn's position as he walks toward a target.

Looks up Merlyn by name (not hardcoded GUID) via vision_lib, then polls
every 10s until he arrives within 500 units of the target.

Usage:
  python drive_merlyn.py                          # default target: market center (15000, 15000)
  python drive_merlyn.py --target 15748,10236     # custom target
  python drive_merlyn.py --interval 5 --max 20    # poll every 5s, max 20 iterations
"""
import argparse, time, sys

from vision_lib import VisionLib, call

def main():
    parser = argparse.ArgumentParser(description='Poll Merlyn position while walking')
    parser.add_argument('--target', default='15000,15000', help='Target "x,y" to check distance against')
    parser.add_argument('--interval', type=float, default=10, help='Poll interval in seconds')
    parser.add_argument('--max', type=int, default=12, help='Max poll iterations')
    parser.add_argument('--arrive-dist', type=float, default=500, help='Arrival distance in world units')
    args = parser.parse_args()

    tx, ty = [float(x) for x in args.target.split(',')]
    vl = VisionLib()

    for i in range(args.max):
        time.sleep(args.interval)
        pos = vl.get_merlyn_pos()
        if not pos:
            print(f't={i+1}: Merlyn not found')
            continue
        px, py, pz = pos
        dist = ((px - tx) ** 2 + (py - ty) ** 2) ** 0.5
        print(f't={i+1} pos=({px:.0f},{py:.0f},{pz:.0f}) dist={dist:.0f} ({dist/39.37:.0f}m)')
        if dist < args.arrive_dist:
            print(f'Arrived at target ({tx:.0f},{ty:.0f})!')
            break

    # Read recent AgentDrive logs
    console = call('read_console', limit=10)
    text = console['result']['content'][0]['text']
    for line in text.split('\n'):
        if 'AgentDrive' in line or 'AgentControlled' in line:
            print(line)

if __name__ == '__main__':
    main()
