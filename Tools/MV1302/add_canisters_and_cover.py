#!/usr/bin/env python3
"""MV-1302: adds "canisters[]" placements and marks ~25% of eligible cover destructible (hp=150)
onto the already-generated city_config.json, world3_config.json (Reef) and mothership_config.json.

One-off DATA patch, same "edit the already-shipped JSON directly" pattern
Tools/World5/add_welder_placements.py (MV-1244) and add_airlock_placements.py (MV-1310) use --
nothing here re-runs any world's own one-time layout generator.

Per the ticket:
  Canisters: every combat area index >= 3 gets 2-4 WorldCanister{id,x,z} (absolute world metres,
  same convention as cover/garrison), each >= 2 m from its own area's walls, >= 2 m from every
  resolved gate mouth, >= 1.5 m from every cover piece, and >= 6 m clear of any boss spawn authored
  in that area. At least one canister per area is placed within 6 m of an "anchor" point (garrison
  entry, else cradle, else replicator, else pylon, else boss, else the area's own centre) so it is
  worth shooting. Never on the entry stub (index 0, excluded by construction below).

  Destructible cover: hp 150 on cover pieces that are solid, footprint <= 10 sqm, and not
  hull/outline dressing (any dressing whose name contains "bulkhead") -- except on the Mothership,
  where a hull-named piece <= 6 sqm is still eligible (MV-1314's carve-out: a small prop reusing
  that dressing, not wall-forming structure). Never within 2 m of a resolved gate mouth. This
  mirrors MapValidation.IsHullDressing/IsEligibleForDestructibleCover (MapValidation.cs) exactly,
  except Reef/City get NO hull carve-out at all (every reef_bulkhead stays excluded regardless of
  size -- the ticket's own wording: "Never on hull/structure pieces (Reef reef_bulkhead and other
  hull-fill...)" names no size threshold for Reef, unlike the Mothership-specific carve-out MV-1314
  decided). Selection floors at 1 piece per area with >= 1 eligible piece (not a flat 25% that could
  round to 0) so every eligible area actually counts toward AC1's ">= 20 areas" bar, while still
  averaging toward ~25% in areas with more than a handful of eligible pieces.

Run: python Tools/MV1302/add_canisters_and_cover.py
"""
import json
import math
import pathlib
import random
import re

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
# Each world's own already-shipped file was written with its own indent width (city/reef: 1 space
# per level; mothership: 2) -- re-dumping at the wrong width would rewrite the ENTIRE file (every
# line) instead of a diff scoped to what this ticket actually touches. Matched against each file's
# real on-disk formatting, not assumed.
WORLDS = [
    ("city", REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "city_config.json", 1),
    ("reef", REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "world3_config.json", 1),
    ("mothership", REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "mothership_config.json", 2),
]

# --- canister placement ---
WALL_CLEARANCE = 2.0
GATE_CLEARANCE = 2.0
COVER_CLEARANCE = 1.5
BOSS_CLEARANCE = 6.0
GARRISON_PROXIMITY = 6.0
MIN_CANISTER_SEPARATION = 1.5
MIN_CANISTERS = 2
MAX_CANISTERS = 4
SCAN_STEP = 1.0

# --- destructible cover ---
DESTRUCTIBLE_HP = 150.0
FOOTPRINT_CAP_SQM = 10.0          # MapValidation.DestructibleCoverFootprintCapSqm
HULL_CARVEOUT_SQM = 6.0           # MapValidation.DestructibleCoverHullCarveOutSqm (Mothership only)
DESTRUCTIBLE_GATE_CLEARANCE = 2.0  # MapValidation.DestructibleCoverGateClearance
DESTRUCTIBLE_SELECT_RATIO = 0.25


def is_hull_dressing(dressing):
    return "bulkhead" in (dressing or "").lower()


def eligible_for_destructible(cover, world_key):
    if cover.get("coverKind", "solid") != "solid":
        return False
    footprint = cover["width"] * cover["depth"]
    if footprint > FOOTPRINT_CAP_SQM:
        return False
    if is_hull_dressing(cover.get("dressing")):
        # MV-1314's carve-out is Mothership-only; Reef/City hull dressing stays excluded at any size.
        if world_key != "mothership" or footprint > HULL_CARVEOUT_SQM:
            return False
    return True


def wall_coord(area, wall):
    if wall == "N":
        return area["origin"]["z"] + area["size"]["d"]
    if wall == "S":
        return area["origin"]["z"]
    if wall == "E":
        return area["origin"]["x"] + area["size"]["w"]
    return area["origin"]["x"]  # "W"


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
    """Garrison entries, else cradles, else replicators, else pylons, else bosses, else the area's
    own centre -- the nearest thing to "where enemies cluster" this area authors."""
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


def is_candidate_clear(px, pz, gate_points, cover_list, boss_points, picked):
    for gx, gz in gate_points:
        if math.hypot(px - gx, pz - gz) < GATE_CLEARANCE:
            return False
    for c in cover_list:
        half_w = c["width"] / 2.0 + COVER_CLEARANCE
        half_d = c["depth"] / 2.0 + COVER_CLEARANCE
        if abs(px - c["x"]) < half_w and abs(pz - c["z"]) < half_d:
            return False
    for bx, bz in boss_points:
        if math.hypot(px - bx, pz - bz) < BOSS_CLEARANCE:
            return False
    for ox, oz in picked:
        if math.hypot(px - ox, pz - oz) < MIN_CANISTER_SEPARATION:
            return False
    return True


def place_canisters(area, gate_points, count, rng):
    x0, z0 = area["origin"]["x"], area["origin"]["z"]
    w, d = area["size"]["w"], area["size"]["d"]
    lo_x, hi_x = x0 + WALL_CLEARANCE, x0 + w - WALL_CLEARANCE
    lo_z, hi_z = z0 + WALL_CLEARANCE, z0 + d - WALL_CLEARANCE
    if hi_x <= lo_x or hi_z <= lo_z:
        raise RuntimeError(f"area '{area['id']}': too small for a {WALL_CLEARANCE} m wall clearance")

    cover_list = area.get("cover", [])
    boss_points = area_boss_points(area)
    anchors = area_anchor_points(area)

    candidates = []
    x = lo_x
    while x <= hi_x + 1e-9:
        z = lo_z
        while z <= hi_z + 1e-9:
            candidates.append((x, z))
            z += SCAN_STEP
        x += SCAN_STEP

    def nearest_anchor_dist(p):
        return min(math.hypot(p[0] - ax, p[1] - az) for ax, az in anchors)

    picked = []

    # First pick: the valid candidate closest to an anchor, so "at least one canister is within
    # 6 m of a garrison point" holds whenever the room geometry allows it at all.
    by_anchor = sorted(candidates, key=nearest_anchor_dist)
    for px, pz in by_anchor:
        if is_candidate_clear(px, pz, gate_points, cover_list, boss_points, picked):
            picked.append((px, pz))
            break

    # Remaining picks: farthest-point sampling from what's already placed, so canisters spread
    # out across the room instead of clustering at the anchor.
    remaining = [c for c in candidates if c not in picked]
    while len(picked) < count and remaining:
        remaining = [c for c in remaining
                     if is_candidate_clear(c[0], c[1], gate_points, cover_list, boss_points, picked)]
        if not remaining:
            break
        best = max(remaining, key=lambda c: min(math.hypot(c[0] - px, c[1] - pz) for px, pz in picked))
        picked.append(best)
        remaining.remove(best)

    if len(picked) < MIN_CANISTERS:
        raise RuntimeError(
            f"area '{area['id']}': could only place {len(picked)} of a required "
            f"{MIN_CANISTERS}-{MAX_CANISTERS} canisters clear of walls/gates/cover/boss")

    rng.shuffle(picked)
    return picked[:count]


def process_world(world_key, path, indent):
    with open(path, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    areas_by_id = {a["id"]: a for a in cfg["areas"]}
    gate_points = [gate_position(areas_by_id, g) for g in cfg["gates"]]
    combat_areas = [a for a in cfg["areas"] if a["id"][0] == "a" and a["id"][1:].isdigit()]
    combat_areas.sort(key=lambda a: a["index"])

    canisters_added = 0
    for area in combat_areas:
        if area["index"] < 3:
            continue
        rng = random.Random(f"{world_key}:{area['id']}:canisters")
        count = rng.randint(MIN_CANISTERS, MAX_CANISTERS)
        positions = place_canisters(area, gate_points, count, rng)
        area["canisters"] = [
            {"id": f"{area['id']}_canister{i + 1}", "x": round(px, 2), "z": round(pz, 2)}
            for i, (px, pz) in enumerate(positions)
        ]
        canisters_added += len(positions)

    areas_with_destructible = 0
    pieces_marked = 0
    for area in cfg["areas"]:
        if area["id"] == "stub":
            continue
        cover = area.get("cover") or []
        eligible = [c for c in cover if eligible_for_destructible(c, world_key)
                    and not any(math.hypot(c["x"] - gx, c["z"] - gz) < DESTRUCTIBLE_GATE_CLEARANCE
                                for gx, gz in gate_points)]
        if not eligible:
            continue
        k = max(1, round(len(eligible) * DESTRUCTIBLE_SELECT_RATIO))
        k = min(k, len(eligible))
        rng = random.Random(f"{world_key}:{area['id']}:cover")
        selected = eligible[:]
        rng.shuffle(selected)
        for c in selected[:k]:
            c["hp"] = DESTRUCTIBLE_HP
            pieces_marked += 1
        areas_with_destructible += 1

    cfg["revision"] = (
        cfg.get("revision", "") +
        f" MV-1302: added \"canisters\" to every area index>=3 (2-4 each, {canisters_added} total), "
        f"marked {pieces_marked} eligible cover piece(s) destructible (hp=150) across "
        f"{areas_with_destructible} areas -- via Tools/MV1302/add_canisters_and_cover.py."
    )

    text = json.dumps(cfg, indent=indent) + "\n"

    # json.dumps always expands every non-empty array, even a short flat one of strings -- collapse
    # "eliteAffixes" back onto its own original single line (every world authors it that way) so
    # this ticket's diff is canisters/destructible-cover only, never an incidental reformat of
    # something it does not touch ("nothing existing moves") -- same fix
    # Tools/World5/add_airlock_placements.py already applies for the same reason.
    elite_inline = "[" + ", ".join(f'"{v}"' for v in cfg["dials"]["eliteAffixes"]) + "]"
    text = re.sub(r'"eliteAffixes": \[\s*[^\]]*?\]', f'"eliteAffixes": {elite_inline}', text, flags=re.S)

    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)

    print(f"{world_key}: wrote {path}")
    print(f"{world_key}: canisters added: {canisters_added}")
    print(f"{world_key}: areas with destructible cover: {areas_with_destructible}, pieces marked: {pieces_marked}")


def main():
    for world_key, path, indent in WORLDS:
        process_world(world_key, path, indent)


if __name__ == "__main__":
    main()
