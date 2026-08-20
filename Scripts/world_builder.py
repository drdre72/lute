#!/usr/bin/env python3
"""
Coherent Procedural World Builder for Lute Unreal
Rust-style map generation: terrain sculpting, ring road connecting monuments,
natural zones, roadside details. All props attach to a single
WorldBuilder actor to prevent editor crashes.
"""

import requests
import json
import random
import math
import time
import sys

SERVER = "http://localhost:6410"
MAP_RADIUS = 1400

def wait_for_server(timeout=120):
    start = time.time()
    while time.time() - start < timeout:
        try:
            r = requests.get(f"{SERVER}/state", timeout=5)
            if r.status_code == 200:
                data = r.json()
                print(f"Server up! Map: {data.get('map_name', '?')} Actors: {data.get('actor_count', 0)}")
                return True
        except:
            pass
        time.sleep(2)
    return False

def clear_world():
    """Clear all WorldBuilder props from the level."""
    try:
        r = requests.post(f"{SERVER}/clear_world", json={}, timeout=30)
        if r.status_code == 200:
            data = r.json()
            print(f"  Cleared {data.get('removed_count', 0)} actors")
            return True
    except Exception as e:
        print(f"  ! clear_world failed: {e}")
    return False

def terrain_sculpt(x, y, radius=500, strength=100, mode="raise"):
    """Sculpt terrain at a position."""
    try:
        r = requests.post(f"{SERVER}/terrain_sculpt", json={
            "x": x, "y": y, "radius": radius, "strength": strength, "mode": mode
        }, timeout=60)
        if r.status_code == 200:
            data = r.json()
            if data.get("success"):
                print(f"  ~ terrain {mode} at ({x:.0f},{y:.0f}) r={radius} -> {data.get('affected_verts',0)} verts")
                return True
            else:
                print(f"  ! terrain error: {data.get('error','?')}")
    except Exception as e:
        print(f"  ! terrain_sculpt failed: {e}")
    return False

def terrain_paint(x, y, radius=500, strength=0.5, layer="Grass"):
    """Paint terrain layer at a position."""
    try:
        r = requests.post(f"{SERVER}/terrain_paint", json={
            "x": x, "y": y, "radius": radius, "strength": strength, "layer": layer
        }, timeout=60)
        if r.status_code == 200:
            data = r.json()
            if data.get("success"):
                print(f"  ~ paint {layer} at ({x:.0f},{y:.0f}) r={radius} -> {data.get('affected_verts',0)} verts")
                return True
            else:
                print(f"  ! paint error: {data.get('error','?')}")
    except Exception as e:
        print(f"  ! terrain_paint failed: {e}")
    return False

def get_terrain_height(x, y):
    """Query terrain height at a position. Returns Z or 0."""
    try:
        r = requests.get(f"{SERVER}/get_terrain_height", params={"x": x, "y": y}, timeout=10)
        if r.status_code == 200:
            data = r.json()
            if data.get("hit"):
                return data.get("height", 0.0)
    except:
        pass
    return 0.0

def list_props(filter_str="", max_count=2000):
    r = requests.post(f"{SERVER}/list_props", json={"filter": filter_str, "max": max_count}, timeout=30)
    if r.status_code == 200:
        return r.json().get("props", [])
    return []

def place(mesh_path, x, y, z=0, yaw=0, scale=1.0, label="", retries=3):
    for attempt in range(retries):
        try:
            r = requests.post(f"{SERVER}/place_prop", json={
                "mesh_path": mesh_path,
                "x": x, "y": y, "z": z,
                "yaw": yaw, "scale": scale
            }, timeout=60)
            if r.status_code == 200:
                data = r.json()
                if data.get("success"):
                    tag = f" [{label}]" if label else ""
                    print(f"  + {data.get('mesh','?')}{tag} at ({x:.0f},{y:.0f},{z:.0f})")
                    return True
            print(f"  ! FAILED (attempt {attempt+1}): {mesh_path}")
        except Exception as e:
            print(f"  ! TIMEOUT (attempt {attempt+1})")
            time.sleep(3)
    return False

def batch_place(mesh_path, placements, label="", retries=3):
    """Batch place instances via HISM (single draw call for thousands of props).
    placements: list of {x, y, z, yaw, scale} dicts."""
    for attempt in range(retries):
        try:
            r = requests.post(f"{SERVER}/batch_place", json={
                "mesh_path": mesh_path,
                "placements": placements
            }, timeout=120)
            if r.status_code == 200:
                data = r.json()
                if data.get("success"):
                    tag = f" [{label}]" if label else ""
                    count = data.get("instance_count", 0)
                    print(f"  + HISM {data.get('mesh','?')}{tag}: {count} instances")
                    return count
            print(f"  ! BATCH FAILED (attempt {attempt+1}): {mesh_path}")
        except Exception as e:
            print(f"  ! BATCH TIMEOUT (attempt {attempt+1})")
            time.sleep(3)
    return 0

class PropLibrary:
    def __init__(self, all_props):
        self.props = all_props
    
    def find(self, *kws):
        kw = [k.lower() for k in kws]
        for p in self.props:
            name = p.get("name", "").lower()
            if all(k in name for k in kw):
                return p
        return None
    
    def find_all(self, *kws):
        kw = [k.lower() for k in kws]
        return [p for p in self.props if all(k in p.get("name","").lower() for k in kw)]
    
    def in_path(self, path_kw, *name_kws):
        pk = path_kw.lower()
        nk = [k.lower() for k in name_kws]
        return [p for p in self.props 
                if pk in p.get("path","").lower() 
                and (all(k in p.get("name","").lower() for k in nk) if nk else True)]

class MapGen:
    """Coherent Rust-style map generator."""
    
    def __init__(self, lib):
        self.lib = lib
        self.placed = 0
        self.road_points = []
        self.monuments = []
        # Dynamic multipliers for vision-driven iteration
        self.forest_multiplier = 1
        self.terrain_strength_mult = 1.0
        self.spread_mult = 1.0
        self.variety_mult = 1
    
    def _p(self, mesh_path, x, y, z=None, yaw=0, scale=1.0, label=""):
        if z is None:
            z = get_terrain_height(x, y)
        if place(mesh_path, x, y, z, yaw, scale, label):
            self.placed += 1
        time.sleep(0.15)
    
    def _pick(self, prop_list, avoid=None):
        """Pick a random prop, optionally avoiding certain name patterns."""
        if not prop_list:
            return None
        candidates = prop_list
        if avoid:
            candidates = [p for p in prop_list if not any(a in p.get("name","").lower() for a in avoid)]
            if not candidates:
                candidates = prop_list
        return random.choice(candidates)
    
    def generate(self):
        # Sculpt terrain before placing anything
        self.sculpt_terrain()
        time.sleep(1)
        
        # Discover key assets
        self.discover_assets()
        
        # Plan layout
        self.plan_layout()
        
        # Build in coherent order
        self.build_road_ring()
        self.build_monuments()
        self.build_forest_zones()
        self.build_rock_zones()
        self.build_roadside_details()
        self.build_water_features()
        
        # Paint terrain layers after props (so we can see what's where)
        self.paint_terrain()
        
        print(f"\n=== Complete! {self.placed} props placed ===")
        self.print_summary()
    
    def discover_assets(self):
        lib = self.lib
        print("\n--- Asset Discovery ---")
        
        # Trees by type
        self.beech = lib.find_all("europeanbeech", "forest")
        self.hornbeam = lib.find_all("europeanhornbeam", "forest")
        self.stylized_trees = [p for p in lib.find_all("stylized", "tree") if "willow" not in p.get("name","").lower() or random.random() > 0.5]
        self.pines = lib.find_all("pine", "tree")
        self.all_trees = self.beech + self.hornbeam + self.stylized_trees + self.pines
        # Filter out imposters/card versions for close-up
        self.all_trees = [t for t in self.all_trees if "imposter" not in t.get("name","").lower() and "card" not in t.get("name","").lower()]
        print(f"  Trees: {len(self.all_trees)}")
        
        # Rocks
        self.rocks = [p for p in lib.find_all("rock") if "wall" not in p.get("name","").lower() and "sm_" in p.get("name","").lower()]
        self.stones = [p for p in lib.find_all("stone") if "wall" not in p.get("name","").lower() and "sm_" in p.get("name","").lower()]
        self.all_rocks = self.rocks + self.stones
        print(f"  Rocks: {len(self.all_rocks)}")
        
        # Walls — look for actual wall segments, not doormats
        self.stone_walls = lib.find_all("stonewall")
        self.fc_walls = [p for p in lib.in_path("Fishermans_Cabin", "wall") 
                         if "doormat" not in p.get("name","").lower()]
        self.fc_floors = lib.in_path("Fishermans_Cabin", "floor")
        self.fc_roofs = lib.in_path("Fishermans_Cabin", "roof")
        self.fc_doors = lib.in_path("Fishermans_Cabin", "door")
        self.fc_windows = lib.in_path("Fishermans_Cabin", "window")
        self.fc_stairs = lib.in_path("Fishermans_Cabin", "stair")
        print(f"  Walls: stone={len(self.stone_walls)}, fc={len(self.fc_walls)}, floors={len(self.fc_floors)}, roofs={len(self.fc_roofs)}")
        
        # Furniture
        self.chairs = [p for p in lib.find_all("chair") if "sm_" in p.get("name","").lower() and "gaming" not in p.get("name","").lower()]
        self.tables = [p for p in lib.find_all("table") if "sm_" in p.get("name","").lower() and "tableware" not in p.get("name","").lower()]
        self.beds = lib.find_all("bed")
        self.chests = lib.find_all("chest")
        self.barrels = lib.find_all("barrel")
        self.lamps = lib.find_all("lamp")
        self.bookcases = lib.find_all("bookcase") + lib.find_all("bookshelf")
        self.cabinets = lib.find_all("cabinet")
        print(f"  Furniture: chairs={len(self.chairs)}, tables={len(self.tables)}, beds={len(self.beds)}, chests={len(self.chests)}, barrels={len(self.barrels)}, lamps={len(self.lamps)}")
        
        # Decor
        self.vases = lib.find_all("vase")
        self.pillars = [p for p in lib.find_all("pillar") if "sm_" in p.get("name","").lower()]
        self.bushes = lib.find_all("bush")
        self.plants = [p for p in lib.find_all("plant") if "sm_" in p.get("name","").lower()]
        print(f"  Decor: vases={len(self.vases)}, pillars={len(self.pillars)}, bushes={len(self.bushes)}")
        
        # Weapons & tools
        self.weapons = [p for p in lib.props if any(k in p.get("name","").lower() for k in ["sword","axe","shield","helmet","blade"]) and "sm_" in p.get("name","").lower()]
        self.anvil = lib.find("anvil")
        print(f"  Weapons: {len(self.weapons)}, Anvil: {self.anvil is not None}")
        
        # Boats
        self.boats = lib.find_all("boat")
        print(f"  Boats: {len(self.boats)}")
        
        # Fences
        self.fences = lib.find_all("fence")
        print(f"  Fences: {len(self.fences)}")
        
        # Crates
        self.crates = lib.find_all("crate")
        print(f"  Crates: {len(self.crates)}")
    
    def sculpt_terrain(self):
        """Sculpt terrain: hills at perimeter, flatten center, water basin at harbor."""
        print("\n=== Sculpting Terrain ===")
        tsm = self.terrain_strength_mult
        
        # Flatten the center area for the ring road and monuments
        terrain_sculpt(0, 0, radius=800, strength=int(50 * tsm), mode="flatten")
        time.sleep(0.3)
        
        # Raise hills at perimeter (between monuments) for natural barriers
        for i in range(6):
            angle = (i / 6) * 2 * math.pi + math.pi / 6  # Offset from monument positions
            hx = math.cos(angle) * 1100
            hy = math.sin(angle) * 1100
            terrain_sculpt(hx, hy, radius=600, strength=int(200 * tsm), mode="raise")
            time.sleep(0.3)
        
        # Lower a water basin at harbor position (bottom of map)
        harbor_angle = (4 / 6) * 2 * math.pi - math.pi / 2
        wx = math.cos(harbor_angle) * 600
        wy = math.sin(harbor_angle) * 600
        terrain_sculpt(wx, wy, radius=500, strength=300, mode="lower")
        time.sleep(0.3)
        # Extend water outward
        terrain_sculpt(wx + math.cos(harbor_angle) * 300, wy + math.sin(harbor_angle) * 300, radius=400, strength=200, mode="lower")
        time.sleep(0.3)
        
        # Smooth the transitions
        for i in range(6):
            angle = (i / 6) * 2 * math.pi + math.pi / 6
            hx = math.cos(angle) * 1100
            hy = math.sin(angle) * 1100
            terrain_sculpt(hx, hy, radius=800, strength=30, mode="smooth")
            time.sleep(0.3)
        
        # Smooth around the water basin
        terrain_sculpt(wx, wy, radius=700, strength=20, mode="smooth")
        time.sleep(0.3)
        
        print("  Terrain sculpting complete")
    
    def paint_terrain(self):
        """Paint terrain layers: dirt roads, grass around monuments, sand near water."""
        print("\n=== Painting Terrain ===")
        
        # Dirt path along the ring road
        for x, y, angle in self.road_points:
            terrain_paint(x, y, radius=120, strength=0.6, layer="Dirt")
            time.sleep(0.05)
        
        # Grass around monument areas
        for m in self.monuments:
            terrain_paint(m["x"], m["y"], radius=300, strength=0.5, layer="Grass")
            time.sleep(0.1)
        
        # Sand near harbor/water
        harbor = next((m for m in self.monuments if m["type"] == "harbor"), None)
        if harbor:
            terrain_paint(harbor["x"], harbor["y"], radius=400, strength=0.7, layer="Sand")
            time.sleep(0.1)
            # Extend sand toward water
            for i in range(3):
                x = harbor["x"] + math.cos(harbor["angle"]) * (100 + i * 100)
                y = harbor["y"] + math.sin(harbor["angle"]) * (100 + i * 100)
                terrain_paint(x, y, radius=200, strength=0.6, layer="Sand")
                time.sleep(0.1)
        
        # Rock layer on perimeter hills
        for i in range(6):
            angle = (i / 6) * 2 * math.pi + math.pi / 6
            hx = math.cos(angle) * 1100
            hy = math.sin(angle) * 1100
            terrain_paint(hx, hy, radius=400, strength=0.4, layer="Rock")
            time.sleep(0.1)
        
        print("  Terrain painting complete")
    
    def plan_layout(self):
        """Plan monument positions around a ring road."""
        print("\n--- Layout Planning ---")
        ring_radius = 600
        monument_defs = [
            ("Town Market", "market"),
            ("Tavern & Inn", "tavern"),
            ("Blacksmith Forge", "forge"),
            ("Ancient Ruins", "ruins"),
            ("Harbor Dock", "harbor"),
            ("Watchtower Camp", "camp"),
        ]
        
        for i, (name, mtype) in enumerate(monument_defs):
            angle = (i / len(monument_defs)) * 2 * math.pi - math.pi / 2
            x = math.cos(angle) * ring_radius
            y = math.sin(angle) * ring_radius
            self.monuments.append({
                "name": name, "type": mtype,
                "x": x, "y": y, "angle": angle,
                "facing": math.degrees(angle) + 180  # Face toward center
            })
            print(f"  {name} at ({x:.0f},{y:.0f}) facing {math.degrees(angle)+180:.0f}")
        
        # Generate road points along ring
        num_segments = 60
        for i in range(num_segments):
            t = i / num_segments
            angle = t * 2 * math.pi - math.pi / 2
            r = ring_radius + random.uniform(-15, 15)
            x = math.cos(angle) * r
            y = math.sin(angle) * r
            self.road_points.append((x, y, angle + math.pi/2))
        
        print(f"  Road points: {len(self.road_points)}")
    
    def build_road_ring(self):
        """Place stones/walls along the ring road as path surface."""
        print("\n=== Building Road Ring ===")
        if not self.stone_walls:
            print("  No stone walls available, skipping")
            return
        
        wall = self.stone_walls[0]
        for x, y, angle in self.road_points:
            self._p(wall["path"], x, y, 0, math.degrees(angle), 1.0, "road")
    
    def build_monuments(self):
        """Build each monument at its planned position."""
        for m in self.monuments:
            print(f"\n=== Building: {m['name']} ===")
            builder = getattr(self, f"build_{m['type']}", None)
            if builder:
                builder(m)
            else:
                print(f"  No builder for {m['type']}")
    
    def build_market(self, m):
        """Town market: open square with stalls, tables, barrels."""
        mx, my = m["x"], m["y"]
        facing = m["facing"]
        
        # Stone wall border — 4 sides, gap on road-facing side
        if self.stone_walls:
            wall = self.stone_walls[0]
            size = 200
            # Back wall (away from center)
            for step in range(5):
                x = mx - size + step * 100
                y = my - size
                self._p(wall["path"], x, y, 0, 0, 1.0, "market wall")
            # Side walls
            for step in range(4):
                y = my - size + step * 100
                self._p(wall["path"], mx - size, y, 0, 90, 1.0, "market wall")
                self._p(wall["path"], mx + size, y, 0, 90, 1.0, "market wall")
        
        # Market stalls — tables in a row
        if self.tables:
            for i in range(4):
                x = mx - 150 + i * 100
                y = my
                table = self._pick(self.tables)
                self._p(table["path"], x, y, 0, 0, 1.0, "market stall")
                # Barrel beside each stall
                if self.barrels:
                    barrel = self._pick(self.barrels)
                    self._p(barrel["path"], x + 40, y - 50, 0, 0, 1.0, "market barrel")
                # Crate
                if self.crates:
                    crate = self._pick(self.crates)
                    self._p(crate["path"], x - 40, y - 50, 0, 0, 1.0, "market crate")
        
        # Vases at corners
        if self.vases:
            for dx, dy in [(-180,-180),(180,-180),(-180,180),(180,180)]:
                vase = self._pick(self.vases)
                self._p(vase["path"], mx+dx, my+dy, 0, 0, 1.0, "market vase")
    
    def build_tavern(self, m):
        """Tavern: walled building with tables, chairs, bar, lamps, chests."""
        tx, ty = m["x"], m["y"]
        
        # Building walls
        if self.fc_walls:
            wall = self.fc_walls[0]
            size = 250
            # Back wall
            for step in range(6):
                self._p(wall["path"], tx - size + step * 100, ty - size, 0, 0, 1.0, "tavern wall")
            # Front wall with door gap
            for step in range(6):
                if step in [2, 3]:
                    continue
                self._p(wall["path"], tx - size + step * 100, ty + size, 0, 0, 1.0, "tavern wall")
            # Side walls
            for step in range(5):
                self._p(wall["path"], tx - size, ty - 200 + step * 100, 0, 90, 1.0, "tavern wall")
                self._p(wall["path"], tx + size, ty - 200 + step * 100, 0, 90, 1.0, "tavern wall")
        
        # Door at gap
        if self.fc_doors:
            door = self.fc_doors[0]
            self._p(door["path"], tx, ty + size, 0, 0, 1.0, "tavern door")
        
        # Interior: tables with chairs
        if self.tables:
            for i in range(3):
                x = tx - 150 + i * 150
                y = ty - 50
                table = self._pick(self.tables, avoid=["tableware"])
                self._p(table["path"], x, y, 0, 0, 1.0, "tavern table")
                if self.chairs:
                    chair = self._pick(self.chairs)
                    self._p(chair["path"], x, y + 60, 0, 180, 1.0, "tavern chair")
                    self._p(chair["path"], x, y - 60, 0, 0, 1.0, "tavern chair")
        
        # Bar counter (tables lined up along back)
        if self.tables:
            bar = self._pick(self.tables, avoid=["tableware"])
            for step in range(4):
                self._p(bar["path"], tx - 200 + step * 120, ty - 180, 0, 90, 1.0, "bar counter")
        
        # Lamps on walls
        if self.lamps:
            for i in range(2):
                lamp = self._pick(self.lamps)
                self._p(lamp["path"], tx - 180 + i * 360, ty - 220, 80, 0, 1.0, "tavern lamp")
        
        # Chests in corners
        if self.chests:
            chest = self._pick(self.chests)
            self._p(chest["path"], tx - 220, ty - 220, 0, 0, 1.0, "tavern chest")
            self._p(chest["path"], tx + 220, ty - 220, 0, 0, 1.0, "tavern chest")
        
        # Bookcase
        if self.bookcases:
            bc = self.bookcases[0]
            self._p(bc["path"], tx + 220, ty + 100, 0, 270, 1.0, "tavern bookcase")
    
    def build_forge(self, m):
        """Blacksmith forge: walled area with anvil, weapons, barrels."""
        fx, fy = m["x"], m["y"]
        
        if self.fc_walls:
            wall = self.fc_walls[0]
            size = 200
            # Back and side walls
            for step in range(5):
                self._p(wall["path"], fx - size + step * 100, fy + size, 0, 0, 1.0, "forge wall")
            for step in range(4):
                self._p(wall["path"], fx - size, fy - 150 + step * 100, 0, 90, 1.0, "forge wall")
                self._p(wall["path"], fx + size, fy - 150 + step * 100, 0, 90, 1.0, "forge wall")
        
        # Anvil at center
        if self.anvil:
            self._p(self.anvil["path"], fx, fy, 0, 0, 1.0, "anvil")
        
        # Weapon rack
        if self.weapons:
            for i in range(4):
                w = self._pick(self.weapons)
                self._p(w["path"], fx - 120 + i * 80, fy + 80, 0, 0, 1.0, "forge weapon")
        
        # Supply barrels
        if self.barrels:
            for i in range(3):
                barrel = self._pick(self.barrels)
                self._p(barrel["path"], fx + 160, fy - 100 + i * 60, 0, 0, 1.0, "forge barrel")
        
        # Crates of supplies
        if self.crates:
            for i in range(2):
                crate = self._pick(self.crates)
                self._p(crate["path"], fx - 160, fy - 100 + i * 60, 0, 0, 1.0, "forge crate")
    
    def build_ruins(self, m):
        """Ancient ruins: pillars in circle, fallen pillars, rocks."""
        rx, ry = m["x"], m["y"]
        
        if self.pillars:
            # Standing pillars in a ring
            for i in range(8):
                angle = (i / 8) * 2 * math.pi
                px = rx + math.cos(angle) * 100
                py = ry + math.sin(angle) * 100
                pillar = self._pick(self.pillars)
                self._p(pillar["path"], px, py, 0, math.degrees(angle), 1.0, "ruin pillar")
            
            # Fallen pillars lying flat
            for i in range(4):
                px = rx + random.uniform(-130, 130)
                py = ry + random.uniform(-130, 130)
                pillar = self._pick(self.pillars)
                self._p(pillar["path"], px, py, 0, random.uniform(0, 360), random.uniform(0.8, 1.2), "fallen pillar")
        
        # Rocks scattered around
        if self.all_rocks:
            for i in range(12):
                angle = random.uniform(0, 2 * math.pi)
                radius = random.uniform(130, 220)
                px = rx + math.cos(angle) * radius
                py = ry + math.sin(angle) * radius
                rock = self._pick(self.all_rocks)
                self._p(rock["path"], px, py, 0, random.uniform(0, 360), random.uniform(0.5, 1.5), "ruin rock")
    
    def build_harbor(self, m):
        """Harbor dock: boats, barrels, crates by the water."""
        hx, hy = m["x"], m["y"]
        
        # Boats lined up
        if self.boats:
            for i in range(4):
                x = hx - 200 + i * 120
                y = hy + random.uniform(-30, 30)
                boat = self._pick(self.boats)
                self._p(boat["path"], x, y, 0, random.uniform(-10, 10), 1.0, "harbor boat")
        
        # Crates and barrels on dock
        if self.crates:
            for i in range(4):
                crate = self._pick(self.crates)
                self._p(crate["path"], hx + random.uniform(-100,100), hy - 150 + random.uniform(-30,30), 0, random.uniform(0,360), 1.0, "dock crate")
        
        if self.barrels:
            for i in range(4):
                barrel = self._pick(self.barrels)
                self._p(barrel["path"], hx + random.uniform(-100,100), hy - 200 + random.uniform(-30,30), 0, random.uniform(0,360), 1.0, "dock barrel")
        
        # Rocks at water's edge
        if self.all_rocks:
            for i in range(6):
                rock = self._pick(self.all_rocks)
                self._p(rock["path"], hx + random.uniform(-200,200), hy + 100 + random.uniform(-50,50), 0, random.uniform(0,360), random.uniform(0.5,1.5), "shore rock")
    
    def build_camp(self, m):
        """Watchtower camp: tents (crates as makeshift), campfire, supplies."""
        cx, cy = m["x"], m["y"]
        
        # Campfire — rocks in a circle
        if self.all_rocks:
            for i in range(6):
                angle = (i / 6) * 2 * math.pi
                px = cx + math.cos(angle) * 40
                py = cy + math.sin(angle) * 40
                rock = self._pick(self.all_rocks, avoid=["big","large"])
                self._p(rock["path"], px, py, 0, random.uniform(0,360), 0.5, "campfire rock")
        
        # Supply crates around camp
        if self.crates:
            for i in range(6):
                angle = (i / 6) * 2 * math.pi
                px = cx + math.cos(angle) * 120
                py = cy + math.sin(angle) * 120
                crate = self._pick(self.crates)
                self._p(crate["path"], px, py, 0, math.degrees(angle), 1.0, "camp crate")
        
        # Barrels
        if self.barrels:
            for i in range(3):
                barrel = self._pick(self.barrels)
                self._p(barrel["path"], cx + random.uniform(-100,100), cy + random.uniform(-100,100), 0, random.uniform(0,360), 1.0, "camp barrel")
        
        # Weapons rack
        if self.weapons:
            for i in range(3):
                w = self._pick(self.weapons)
                self._p(w["path"], cx - 100 + i * 60, cy - 130, 0, 0, 1.0, "camp weapon")
        
        # Bushes around camp perimeter
        if self.bushes:
            for i in range(8):
                angle = (i / 8) * 2 * math.pi
                px = cx + math.cos(angle) * 180
                py = cy + math.sin(angle) * 180
                bush = self._pick(self.bushes)
                self._p(bush["path"], px, py, 0, random.uniform(0,360), 1.0, "camp bush")
    
    def build_forest_zones(self):
        """Clustered forests in zones between monuments, denser at perimeter.
        Uses HISM batch_place for performance (thousands of trees in one call)."""
        print("\n=== Building Forest Zones (HISM Batch) ===")
        if not self.all_trees:
            print("  No trees available")
            return
        
        # Group placements by mesh path for HISM batching
        tree_placements = {}  # mesh_path -> list of placement dicts
        
        # Perimeter forest — dense ring (scaled by forest_multiplier)
        fm = self.forest_multiplier
        for ring in range(4 * fm):
            r_min = 900 + ring * 120
            r_max = r_min + 100
            density = (25 - min(ring, 4) * 4) * fm
            for i in range(density):
                angle = random.uniform(0, 2 * math.pi)
                radius = random.uniform(r_min, r_max)
                x = math.cos(angle) * radius
                y = math.sin(angle) * radius
                tree = self._pick(self.all_trees)
                path = tree["path"]
                if path not in tree_placements:
                    tree_placements[path] = []
                tree_placements[path].append({
                    "x": x, "y": y, "z": 0,
                    "yaw": random.uniform(0, 360),
                    "scale": random.uniform(0.8, 1.5)
                })
        
        # Clustered groves between monuments
        for i in range(len(self.monuments)):
            m1 = self.monuments[i]
            m2 = self.monuments[(i+1) % len(self.monuments)]
            mid_x = (m1["x"] + m2["x"]) / 2
            mid_y = (m1["y"] + m2["y"]) / 2
            dist = math.sqrt(mid_x**2 + mid_y**2)
            if dist > 0:
                push = 1.3
                mid_x = mid_x * push
                mid_y = mid_y * push
            
            for j in range(12 * fm):
                x = mid_x + random.uniform(-80, 80)
                y = mid_y + random.uniform(-80, 80)
                tree = self._pick(self.all_trees)
                path = tree["path"]
                if path not in tree_placements:
                    tree_placements[path] = []
                tree_placements[path].append({
                    "x": x, "y": y, "z": 0,
                    "yaw": random.uniform(0, 360),
                    "scale": random.uniform(0.8, 1.3)
                })
        
        # Send batch placement per mesh type
        total_trees = 0
        for path, placements in tree_placements.items():
            count = batch_place(path, placements, "forest")
            total_trees += count
        self.placed += total_trees
        print(f"  Total trees placed: {total_trees} via HISM")
    
    def build_rock_zones(self):
        """Rocky outcrops near the perimeter, between forest zones.
        Uses HISM batch_place for performance."""
        print("\n=== Building Rock Zones (HISM Batch) ===")
        if not self.all_rocks:
            print("  No rocks available")
            return
        
        rock_placements = {}  # mesh_path -> list of placement dicts
        
        # Scattered rocks in forest areas (scaled by variety_mult)
        vm = self.variety_mult
        for i in range(30 * vm):
            angle = random.uniform(0, 2 * math.pi)
            radius = random.uniform(800, 1300)
            x = math.cos(angle) * radius
            y = math.sin(angle) * radius
            rock = self._pick(self.all_rocks)
            path = rock["path"]
            if path not in rock_placements:
                rock_placements[path] = []
            rock_placements[path].append({
                "x": x, "y": y, "z": 0,
                "yaw": random.uniform(0, 360),
                "scale": random.uniform(0.5, 2.0)
            })
        
        # Send batch placement per mesh type
        total_rocks = 0
        for path, placements in rock_placements.items():
            count = batch_place(path, placements, "forest rock")
            total_rocks += count
        self.placed += total_rocks
        print(f"  Total rocks placed: {total_rocks} via HISM")
    
    def build_roadside_details(self):
        """Lamps, fences, and props along the ring road."""
        print("\n=== Building Roadside Details ===")
        
        # Lamps every few road points
        if self.lamps:
            for i in range(0, len(self.road_points), 6):
                x, y, angle = self.road_points[i]
                # Offset lamp to side of road
                lx = x + math.cos(angle) * 60
                ly = y + math.sin(angle) * 60
                lamp = self._pick(self.lamps)
                self._p(lamp["path"], lx, ly, 0, math.degrees(angle), 1.0, "road lamp")
        
        # Fences along road segments near monuments
        if self.fences:
            fence = self.fences[0]
            for m in self.monuments:
                # Short fence line from monument toward road
                fx = m["x"] + math.cos(m["angle"]) * 250
                fy = m["y"] + math.sin(m["angle"]) * 250
                for step in range(3):
                    x = fx + math.cos(m["angle"] + math.pi/2) * (step * 80 - 80)
                    y = fy + math.sin(m["angle"] + math.pi/2) * (step * 80 - 80)
                    self._p(fence["path"], x, y, 0, math.degrees(m["angle"]), 1.0, "road fence")
        
        # Bushes scattered along roads
        if self.bushes:
            for i in range(0, len(self.road_points), 4):
                x, y, angle = self.road_points[i]
                bx = x + math.cos(angle) * random.uniform(80, 120) * random.choice([-1, 1])
                by = y + math.sin(angle) * random.uniform(80, 120) * random.choice([-1, 1])
                bush = self._pick(self.bushes)
                self._p(bush["path"], bx, by, 0, random.uniform(0, 360), random.uniform(0.7, 1.2), "roadside bush")
    
    def build_water_features(self):
        """Rocks and boats at the harbor monument area, extending outward."""
        print("\n=== Building Water Features ===")
        
        # Find harbor monument
        harbor = next((m for m in self.monuments if m["type"] == "harbor"), None)
        if harbor:
            # Extend water edge with rocks
            if self.all_rocks:
                for i in range(15):
                    angle = harbor["angle"]
                    x = harbor["x"] + math.cos(angle) * random.uniform(200, 400)
                    y = harbor["y"] + math.sin(angle) * random.uniform(200, 400)
                    rock = self._pick(self.all_rocks)
                    self._p(rock["path"], x, y, 0, random.uniform(0, 360), random.uniform(0.5, 2.0), "water rock")
    
    def print_summary(self):
        print("\nLayout:")
        for m in self.monuments:
            print(f"  {m['name']:20s} at ({m['x']:.0f}, {m['y']:.0f})")
        print(f"  Ring road: {len(self.road_points)} segments")
        print(f"  All props on single WorldBuilder_Props actor (no crash)")

def main():
    print("=== Lute Coherent World Builder ===")
    print(f"Map radius: {MAP_RADIUS}m")
    
    if not wait_for_server():
        print("ERROR: Server not responding.")
        sys.exit(1)
    
    print("\nGathering all props...")
    all_props = list_props("", 2000)
    print(f"Total props: {len(all_props)}")
    
    if not all_props:
        print("ERROR: No props found.")
        sys.exit(1)
    
    lib = PropLibrary(all_props)
    gen = MapGen(lib)
    gen.generate()

if __name__ == "__main__":
    main()
