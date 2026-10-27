#!/usr/bin/env python3
"""MV-1310: adds "airlocks[]" placements onto the already-generated mothership_config.json.

One-off DATA patch, same "edit the already-shipped JSON directly" pattern add_welder_placements.py
(MV-1244) used -- generate_mothership_layout.py is MV-1233's own one-time generator and is never
re-run for a later ticket's placements.

Per the ticket (rule AIRLOCKS, Lee 27 Oct 2026): a1-a3 get one airlock each, a4-a29 get one or two,
a30 gets exactly two (MV-1313 needs both). Every airlock sits ON the area's own outline (its x or z
pins to one of the area's 4 boundary lines, "facing" set to the opposite cardinal so the built door
opens INTO the room -- see WorldMapLoader.BuildAirlock/CradleFacingRotation), at least 4 m from
every gate's resolved doorway position and 6 m from every Assembly Cradle in the whole map, with a
12 m open-floor cone directly ahead of it (apex at the door, widening to the door's own 3 m built
width by 12 m out) clear of every SOLID cover piece in its own area. This script searches each
area's own 4 walls for a position meeting all of that; it does not move anything that already
exists.

Run: python Tools/World5/add_airlock_placements.py
"""
import json
import math
import pathlib
import re

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
CONFIG_PATH = REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "mothership_config.json"

MIN_GATE_GAP = 4.0
MIN_CRADLE_GAP = 6.0
MIN_SIBLING_GAP = 6.0
CONE_RANGE = 12.0

# The cone's apex sits at the door itself and widens to the door's OWN built width (3 m,
# WorldMapLoader.AirlockWidth) by the time it reaches CONE_RANGE -- so a robot pulled in from
# anywhere within it tracks back to a lane no wider than the door it is being pulled through.
CONE_HALF_WIDTH_AT_RANGE = 1.5
CONE_HALF_ANGLE = math.atan(CONE_HALF_WIDTH_AT_RANGE / CONE_RANGE)

WALL_INSET = 2.0  # keeps the 3 m-wide door clear of the wall's own corners
SCAN_STEP = 0.5

FACING_DIR = {"N": (0.0, 1.0), "E": (1.0, 0.0), "S": (0.0, -1.0), "W": (-1.0, 0.0)}
INWARD_FACING = {"N": "S", "S": "N", "E": "W", "W": "E"}  # the wall's own outward normal, reversed

# index -> airlock count. a1-a3: 1 each (ticket). a30: 2 (ticket, MV-1313). Everything else (a4-a29):
# 1 -- within the ticket's own authored "one or two each" range.
AIRLOCK_COUNT = {i: 1 for i in range(1, 31)}
AIRLOCK_COUNT[30] = 2


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


def point_in_cone(qx, qz, px, pz, dirx, dirz):
    dx, dz = qx - px, qz - pz
    depth = dx * dirx + dz * dirz
    if depth <= 0.0 or depth > CONE_RANGE:
        return False
    perp_x, perp_z = dx - depth * dirx, dz - depth * dirz
    lateral = math.hypot(perp_x, perp_z)
    return lateral <= depth * math.tan(CONE_HALF_ANGLE)


def cone_is_clear(cover, px, pz, dirx, dirz):
    for c in cover:
        if c.get("coverKind", "solid") != "solid":
            continue
        half_w, half_d = c["width"] / 2.0, c["depth"] / 2.0
        corners = [
            (c["x"] - half_w, c["z"] - half_d), (c["x"] + half_w, c["z"] - half_d),
            (c["x"] - half_w, c["z"] + half_d), (c["x"] + half_w, c["z"] + half_d),
            (c["x"], c["z"]),
        ]
        if any(point_in_cone(cx, cz, px, pz, dirx, dirz) for cx, cz in corners):
            return False
    return True


def find_positions(area, wall_order, gate_points, cradle_points, count):
    cover = area.get("cover", [])
    x0, z0 = area["origin"]["x"], area["origin"]["z"]
    w, d = area["size"]["w"], area["size"]["d"]
    picked = []

    for wall in wall_order:
        if len(picked) >= count:
            break
        if wall in ("N", "S"):
            lo, hi = x0 + WALL_INSET, x0 + w - WALL_INSET
            fixed = z0 if wall == "S" else z0 + d
        else:
            lo, hi = z0 + WALL_INSET, z0 + d - WALL_INSET
            fixed = x0 if wall == "W" else x0 + w
        if hi <= lo:
            continue

        facing = INWARD_FACING[wall]
        dirx, dirz = FACING_DIR[facing]
        mid = (lo + hi) / 2.0

        # Every candidate along this wall that clears gates/cradles/siblings/the cone, closest to
        # the wall's own midpoint first -- so a door reads as centred in its wall rather than jammed
        # into whichever corner the scan happens to reach first.
        candidates = []
        pos = lo
        while pos <= hi + 1e-9:
            px, pz = (pos, fixed) if wall in ("N", "S") else (fixed, pos)
            candidates.append((px, pz))
            pos += SCAN_STEP
        candidates.sort(key=lambda p: abs((p[0] if wall in ("N", "S") else p[1]) - mid))

        for px, pz in candidates:
            if len(picked) >= count:
                break
            clear = all(math.hypot(px - gx, pz - gz) >= MIN_GATE_GAP for gx, gz in gate_points)
            clear = clear and all(math.hypot(px - cx, pz - cz) >= MIN_CRADLE_GAP for cx, cz in cradle_points)
            clear = clear and all(math.hypot(px - sx, pz - sz) >= MIN_SIBLING_GAP for sx, sz, _ in picked)
            clear = clear and cone_is_clear(cover, px, pz, dirx, dirz)
            if clear:
                picked.append((px, pz, facing))

    return picked


def main():
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    areas_by_id = {a["id"]: a for a in cfg["areas"]}
    combat_areas = {a["index"]: a for a in cfg["areas"] if a["id"][0] == "a" and a["id"][1:].isdigit()}

    gate_points = [gate_position(areas_by_id, g) for g in cfg["gates"]]
    cradle_points = [(cr["x"], cr["z"]) for a in combat_areas.values() for cr in a.get("cradles", [])]

    wall_cycle = ["S", "E", "N", "W"]

    total_added = 0
    for idx in range(1, 31):
        area = combat_areas[idx]
        count = AIRLOCK_COUNT[idx]
        # Rotates which wall is tried first per area (index-keyed, deterministic) purely so doors
        # don't all land on the same compass side of every room -- not an AC requirement, just a
        # cheap way to avoid 30 identical-looking placements.
        wall_order = wall_cycle[idx % 4:] + wall_cycle[:idx % 4]
        positions = find_positions(area, wall_order, gate_points, cradle_points, count)
        if len(positions) < count:
            raise RuntimeError(
                f"area '{area['id']}': could only place {len(positions)} of {count} airlocks "
                f"clear of gates/cradles/cover")

        airlocks = []
        for i, (px, pz, facing) in enumerate(positions):
            airlocks.append({
                "id": f"{area['id']}_airlock{i + 1}",
                "x": round(px, 2),
                "z": round(pz, 2),
                "facing": facing,
            })
        area["airlocks"] = airlocks
        total_added += len(airlocks)

    cfg["revision"] = (cfg["revision"] + " MV-1310: added \"airlocks\" to every area a1-a30 (a1-a3 one "
                        "each, a30 two per MV-1313, every other area one), each on the area's own "
                        "outline, >= 4 m from every gate, >= 6 m from every cradle, with a clear 12 m "
                        "open-floor cone in front -- via Tools/World5/add_airlock_placements.py.")

    text = json.dumps(cfg, indent=2) + "\n"

    # json.dumps always expands every array, even a short flat one of strings -- collapse
    # "eliteAffixes" back onto its own original single line so this ticket's diff is airlocks only,
    # never an incidental reformat of something it does not touch ("nothing existing moves").
    elite_inline = "[" + ", ".join(f'"{v}"' for v in cfg["dials"]["eliteAffixes"]) + "]"
    text = re.sub(r'"eliteAffixes": \[\s*[^\]]*?\]', f'"eliteAffixes": {elite_inline}', text, flags=re.S)

    with open(CONFIG_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)

    print(f"wrote {CONFIG_PATH}")
    print(f"airlocks added: {total_added}")


if __name__ == "__main__":
    main()
