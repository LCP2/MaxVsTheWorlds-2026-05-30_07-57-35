#!/usr/bin/env python3
"""MV-1302: independently checks city_config.json/world3_config.json/mothership_config.json against
the canister-placement and destructible-cover rules.

This is a SEPARATE program from add_canisters_and_cover.py -- it NEVER imports it, and it only ever
re-reads the JSON that generator wrote (generate-then-verify: a generator cannot certify its own
output). Exit 0 means clean; exit non-zero prints every rule it found broken, naming the rule.

Run: python Tools/MV1302/check_canisters_and_cover.py
"""
import json
import math
import pathlib
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
WORLDS = [
    ("city", REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "city_config.json"),
    ("reef", REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "world3_config.json"),
    ("mothership", REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "mothership_config.json"),
]

WALL_CLEARANCE = 2.0
GATE_CLEARANCE = 2.0
COVER_CLEARANCE = 1.5
BOSS_CLEARANCE = 6.0
GARRISON_PROXIMITY = 6.0
MIN_CANISTERS = 2
MAX_CANISTERS = 4

FOOTPRINT_CAP_SQM = 10.0
HULL_CARVEOUT_SQM = 6.0
DESTRUCTIBLE_GATE_CLEARANCE = 2.0
REQUIRED_DESTRUCTIBLE_AREAS = 20


def is_hull_dressing(dressing):
    return "bulkhead" in (dressing or "").lower()


def wall_coord(area, wall):
    if wall == "N":
        return area["origin"]["z"] + area["size"]["d"]
    if wall == "S":
        return area["origin"]["z"]
    if wall == "E":
        return area["origin"]["x"] + area["size"]["w"]
    return area["origin"]["x"]


def wall_span(area, wall):
    if wall in ("N", "S"):
        return area["origin"]["x"], area["origin"]["x"] + area["size"]["w"]
    return area["origin"]["z"], area["origin"]["z"] + area["size"]["d"]


def gate_position(areas_by_id, gate):
    from_area = areas_by_id[gate["from"]["area"]]
    to_area = areas_by_id[gate["to"]["area"]]
    from_wall, to_wall = gate["from"]["wall"], gate["to"]["wall"]
    from_lo, from_hi = wall_span(from_area, from_wall)
    to_lo, to_hi = wall_span(to_area, to_wall)
    pos_from = from_lo + min(max(gate["from"]["pos"], 0.0), 1.0) * (from_hi - from_lo)
    pos_to = to_lo + min(max(gate["to"]["pos"], 0.0), 1.0) * (to_hi - to_lo)
    along = (pos_from + pos_to) / 2.0
    fixed = wall_coord(from_area, from_wall)
    if from_wall in ("N", "S"):
        return along, fixed
    return fixed, along


def area_boss_points(area):
    pts = []
    b = area.get("boss")
    if b:
        pts.append((b["x"], b["z"]))
    for b in area.get("bosses") or []:
        pts.append((b["x"], b["z"]))
    return pts


def area_anchor_points(area):
    for key in ("garrison", "cradles", "replicators", "pylons"):
        pts = [(e["x"], e["z"]) for e in (area.get(key) or [])]
        if pts:
            return pts
    boss_pts = area_boss_points(area)
    if boss_pts:
        return boss_pts
    x0, z0 = area["origin"]["x"], area["origin"]["z"]
    w, d = area["size"]["w"], area["size"]["d"]
    return [(x0 + w / 2.0, z0 + d / 2.0)]


def check_world(world_key, path, violations):
    with open(path, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    areas_by_id = {a["id"]: a for a in cfg["areas"]}
    gate_points = [gate_position(areas_by_id, g) for g in cfg["gates"]]
    combat_areas = [a for a in cfg["areas"] if a["id"][0] == "a" and a["id"][1:].isdigit()]

    for area in combat_areas:
        aid = area["id"]
        canisters = area.get("canisters") or []

        if area["index"] < 3:
            if canisters:
                violations.append(f"[{world_key}] '{aid}' (index {area['index']}) authors canisters "
                                   f"but index < 3")
            continue

        n = len(canisters)
        if not (MIN_CANISTERS <= n <= MAX_CANISTERS):
            violations.append(f"[{world_key}] canisterCount: '{aid}' has {n} canisters, "
                               f"expected {MIN_CANISTERS}-{MAX_CANISTERS}")

        x0, z0 = area["origin"]["x"], area["origin"]["z"]
        w, d = area["size"]["w"], area["size"]["d"]
        cover = area.get("cover") or []
        boss_points = area_boss_points(area)
        anchors = area_anchor_points(area)

        any_near_anchor = False
        for c in canisters:
            px, pz = c["x"], c["z"]

            if not (x0 + WALL_CLEARANCE - 1e-6 <= px <= x0 + w - WALL_CLEARANCE + 1e-6 and
                    z0 + WALL_CLEARANCE - 1e-6 <= pz <= z0 + d - WALL_CLEARANCE + 1e-6):
                violations.append(f"[{world_key}] canisterWall: '{c['id']}' in '{aid}' is within "
                                   f"{WALL_CLEARANCE} m of a wall")

            for gx, gz in gate_points:
                gap = math.hypot(px - gx, pz - gz)
                if gap < GATE_CLEARANCE - 1e-6:
                    violations.append(f"[{world_key}] canisterGate: '{c['id']}' in '{aid}' is "
                                       f"{gap:.2f} m from a gate, under {GATE_CLEARANCE} m")

            for cov in cover:
                half_w = cov["width"] / 2.0 + COVER_CLEARANCE
                half_d = cov["depth"] / 2.0 + COVER_CLEARANCE
                if abs(px - cov["x"]) < half_w - 1e-6 and abs(pz - cov["z"]) < half_d - 1e-6:
                    violations.append(f"[{world_key}] canisterCover: '{c['id']}' in '{aid}' is within "
                                       f"{COVER_CLEARANCE} m of cover '{cov['id']}'")

            for bx, bz in boss_points:
                gap = math.hypot(px - bx, pz - bz)
                if gap < BOSS_CLEARANCE - 1e-6:
                    violations.append(f"[{world_key}] canisterBoss: '{c['id']}' in '{aid}' is "
                                       f"{gap:.2f} m from the boss spawn, under {BOSS_CLEARANCE} m")

            if any(math.hypot(px - ax, pz - az) <= GARRISON_PROXIMITY for ax, az in anchors):
                any_near_anchor = True

        if canisters and not any_near_anchor:
            violations.append(f"[{world_key}] canisterAnchor: no canister in '{aid}' is within "
                               f"{GARRISON_PROXIMITY} m of a garrison/anchor point")

    # destructible cover
    areas_with_destructible = 0
    for area in cfg["areas"]:
        aid = area["id"]
        marked_here = False
        for c in area.get("cover") or []:
            if c.get("hp", 0) <= 0:
                continue
            marked_here = True
            footprint = c["width"] * c["depth"]
            if footprint > FOOTPRINT_CAP_SQM:
                violations.append(f"[{world_key}] destructibleFootprint: '{c['id']}' in '{aid}' is "
                                   f"{footprint:.2f} sqm, over {FOOTPRINT_CAP_SQM} sqm")
            if is_hull_dressing(c.get("dressing")):
                if world_key != "mothership":
                    violations.append(f"[{world_key}] destructibleHull: '{c['id']}' in '{aid}' is "
                                       f"hull-dressed ('{c.get('dressing')}') and must never be "
                                       f"destructible outside the Mothership carve-out")
                elif footprint > HULL_CARVEOUT_SQM:
                    violations.append(f"[{world_key}] destructibleHullCarveOut: '{c['id']}' in '{aid}' "
                                       f"is hull-dressed at {footprint:.2f} sqm, over the "
                                       f"{HULL_CARVEOUT_SQM} sqm Mothership carve-out")
            if c.get("coverKind", "solid") != "solid":
                violations.append(f"[{world_key}] destructibleNotSolid: '{c['id']}' in '{aid}' is "
                                   f"coverKind '{c.get('coverKind')}' but marked destructible")
        if marked_here:
            areas_with_destructible += 1

    # gate clearance re-checked separately (kept out of the loop above for clarity)
    gate_points_by_area = gate_points
    for area in cfg["areas"]:
        aid = area["id"]
        for c in area.get("cover") or []:
            if c.get("hp", 0) <= 0:
                continue
            for gx, gz in gate_points_by_area:
                gap = math.hypot(c["x"] - gx, c["z"] - gz)
                if gap < DESTRUCTIBLE_GATE_CLEARANCE - 1e-6:
                    violations.append(f"[{world_key}] destructibleGate: '{c['id']}' in '{aid}' is "
                                       f"{gap:.2f} m from a gate, under {DESTRUCTIBLE_GATE_CLEARANCE} m")

    if areas_with_destructible < REQUIRED_DESTRUCTIBLE_AREAS:
        violations.append(f"[{world_key}] destructibleAreaCount: only {areas_with_destructible} areas "
                           f"carry destructible cover, required >= {REQUIRED_DESTRUCTIBLE_AREAS}")

    print(f"{world_key}: {areas_with_destructible} areas with destructible cover")


def main():
    violations = []
    for world_key, path in WORLDS:
        check_world(world_key, path, violations)

    if violations:
        print(f"FAIL: {len(violations)} violation(s)")
        for v in violations:
            print(f"  - {v}")
        return 1

    print("OK: all rules clean across city/reef/mothership")
    return 0


if __name__ == "__main__":
    sys.exit(main())
