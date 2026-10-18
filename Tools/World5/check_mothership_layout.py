#!/usr/bin/env python3
"""MV-1233: independently checks mothership_config.json against the layout rules.

This is a SEPARATE program from generate_mothership_layout.py -- it NEVER imports it, and it only
ever re-reads the JSON that generator wrote (generate-then-verify: a generator cannot certify its
own output). Exit 0 means clean; exit non-zero prints every rule it found broken, naming the rule.

Run: python Tools/World5/check_mothership_layout.py [path-to-config.json]
"""
import json
import sys
import pathlib

DEFAULT_CONFIG = (pathlib.Path(__file__).resolve().parents[2] / "Assets" / "_Project" /
                   "Resources" / "Worlds" / "mothership_config.json")

MIN_DOORWAY = 3.0
MIN_ROOM_DIM = 18.0
MIN_FREE_CHANNEL = 2.0
MIN_ROBOT_COVER_GAP = 2.0
MIN_COVER_HEIGHT = 1.0
MIN_LOW_COVER_HEIGHT = 0.3
LOW_PROFILE_DRESSINGS = {"shipconveyor"}
OVERLAP_TOLERANCE = 0.01  # 1 cm, Geo.Epsilon
CRADLE_SIZE = 3.0
CRADLE_PAD_HALF = 1.0
CRADLE_LANE_HALF_LEN = 1.5
CRADLE_LANE_HALF_WIDTH = 1.0
FACING_DIR = {"N": (0.0, 1.0), "E": (1.0, 0.0), "S": (0.0, -1.0), "W": (-1.0, 0.0)}
EXPECTED_AREA_COUNT = 30
EXPECTED_CRADLE_TOTAL = 47
VALID_KINDS = {
    "rusher", "turret", "gunner", "brute", "bruiser", "heavy", "launcher", "blinker",
    "bolter", "lurker", "sludger", "charger", "dreadnought", "skitter", "breaker",
}


def clean_enum(s):
    return (s or "").replace("_", "").replace("-", "").replace(" ", "").lower()


def wall_coord(area, wall):
    if wall == "N":
        return area["origin"]["z"] + area["size"]["d"]
    if wall == "S":
        return area["origin"]["z"]
    if wall == "E":
        return area["origin"]["x"] + area["size"]["w"]
    if wall == "W":
        return area["origin"]["x"]
    raise ValueError(f"unknown wall '{wall}'")


def wall_span(area, wall):
    if wall in ("N", "S"):
        return area["origin"]["x"], area["origin"]["x"] + area["size"]["w"]
    return area["origin"]["z"], area["origin"]["z"] + area["size"]["d"]


def opposite(wall):
    return {"N": "S", "S": "N", "E": "W", "W": "E"}[wall]


def footprint(area):
    x0, z0 = area["origin"]["x"], area["origin"]["z"]
    return x0, x0 + area["size"]["w"], z0, z0 + area["size"]["d"]


def rects_overlap(r1, r2, tol=OVERLAP_TOLERANCE):
    ax0, ax1, az0, az1 = r1
    bx0, bx1, bz0, bz1 = r2
    return ax0 < bx1 - tol and ax1 > bx0 + tol and az0 < bz1 - tol and az1 > bz0 + tol


def cover_box(c):
    half_w, half_d = c["width"] / 2, c["depth"] / 2
    return c["x"] - half_w, c["x"] + half_w, c["z"] - half_d, c["z"] + half_d


def cradle_pad_and_lane(cr):
    dx, dz = FACING_DIR[cr["facing"]]
    half = CRADLE_SIZE / 2
    pad_cx, pad_cz = cr["x"] + dx * (half + CRADLE_PAD_HALF), cr["z"] + dz * (half + CRADLE_PAD_HALF)
    pad = (pad_cx - CRADLE_PAD_HALF, pad_cx + CRADLE_PAD_HALF, pad_cz - CRADLE_PAD_HALF, pad_cz + CRADLE_PAD_HALF)
    lane_cx = pad_cx + dx * (CRADLE_PAD_HALF + CRADLE_LANE_HALF_LEN)
    lane_cz = pad_cz + dz * (CRADLE_PAD_HALF + CRADLE_LANE_HALF_LEN)
    halfx = CRADLE_LANE_HALF_LEN if dx else CRADLE_LANE_HALF_WIDTH
    halfz = CRADLE_LANE_HALF_LEN if dz else CRADLE_LANE_HALF_WIDTH
    lane = (lane_cx - halfx, lane_cx + halfx, lane_cz - halfz, lane_cz + halfz)
    return pad, lane


def main():
    config_path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_CONFIG
    violations = []

    with open(config_path, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    areas_by_id = {a["id"]: a for a in cfg["areas"]}
    combat_areas = [a for a in cfg["areas"] if a["id"][1:].isdigit() and a["id"][0] == "a"]
    combat_areas.sort(key=lambda a: a["index"])

    # ---- area count / cradle total ----
    if len(combat_areas) != EXPECTED_AREA_COUNT:
        violations.append(f"area count: expected {EXPECTED_AREA_COUNT} combat areas, found {len(combat_areas)}")

    cradle_total = sum(len(a.get("cradles", [])) for a in combat_areas)
    if cradle_total != EXPECTED_CRADLE_TOTAL:
        violations.append(f"cradle total: expected {EXPECTED_CRADLE_TOTAL}, found {cradle_total}")

    # ---- per-area rules ----
    # Scoped to areas 2-30 (MV-1233's own generated content). Area 1 is MV-1232's already-shipped,
    # hand-authored Hangar Bay slice -- frozen per the ticket's own "must not change" instruction,
    # so it is not re-litigated against this checker's rules here.
    all_cover_rects = {}
    for area in combat_areas:
        aid = area["id"]
        if area["index"] < 2:
            continue
        fp = footprint(area)
        w, d = area["size"]["w"], area["size"]["d"]

        # rule: rooms >= 18 m (MinRoomDimension / MapValidation.MinFightRoomWidth)
        if min(w, d) < MIN_ROOM_DIM:
            violations.append(f"minRoomDimension: area '{aid}' is {min(w, d):.1f} m across, under {MIN_ROOM_DIM} m")

        cover_rects = []
        for c in area.get("cover", []):
            kind = clean_enum(c.get("dressing"))
            min_h = MIN_LOW_COVER_HEIGHT if kind in LOW_PROFILE_DRESSINGS else MIN_COVER_HEIGHT
            if c["height"] < min_h:
                violations.append(f"coverHeight: '{c['id']}' in '{aid}' is {c['height']} m tall, under {min_h} m")
            r = cover_box(c)
            if not (fp[0] - OVERLAP_TOLERANCE <= r[0] and r[1] <= fp[1] + OVERLAP_TOLERANCE and
                    fp[2] - OVERLAP_TOLERANCE <= r[2] and r[3] <= fp[3] + OVERLAP_TOLERANCE):
                violations.append(f"coverInArea: '{c['id']}' in '{aid}' falls outside its own area's floor")
            cover_rects.append((c["id"], r))
        all_cover_rects[aid] = cover_rects

        # rule: cover pieces in the same area must not overlap (1 cm tolerance)
        for i in range(len(cover_rects)):
            for j in range(i + 1, len(cover_rects)):
                if rects_overlap(cover_rects[i][1], cover_rects[j][1]):
                    violations.append(f"coverOverlap: '{cover_rects[i][0]}' overlaps "
                                       f"'{cover_rects[j][0]}' in '{aid}'")

        # rule: free 2 m channel at every depth slice of the room
        z = fp[2]
        while z <= fp[3] + 1e-6:
            blocked = []
            for _cid, r in cover_rects:
                if z < r[2] or z > r[3]:
                    continue
                blocked.append((max(r[0], fp[0]), min(r[1], fp[1])))
            blocked.sort()
            widest, cursor = 0.0, fp[0]
            for bmin, bmax in blocked:
                if bmin > cursor:
                    widest = max(widest, bmin - cursor)
                cursor = max(cursor, bmax)
            widest = max(widest, fp[1] - cursor)
            if widest < MIN_FREE_CHANNEL - 1e-3:
                violations.append(f"freeChannel: '{aid}' pinches shut at z={z:.1f} "
                                   f"(widest crossing {widest:.1f} m, floor {MIN_FREE_CHANNEL} m)")
                break
            z += 0.5

        # rule: cradle lane 3x2 m / pad 2x2 m clear of every cover piece in the same area
        for cr in area.get("cradles", []):
            pad, lane = cradle_pad_and_lane(cr)
            for cid, r in cover_rects:
                if rects_overlap(pad, r):
                    violations.append(f"cradlePad: cradle '{cr['id']}' in '{aid}' out-pad overlaps cover '{cid}'")
                if rects_overlap(lane, r):
                    violations.append(f"cradleLane: cradle '{cr['id']}' in '{aid}' lane overlaps cover '{cid}'")

        # rule: every garrison robot >= 2 m from every cover piece in the same area
        for g in area.get("garrison", []):
            if g.get("kind") not in VALID_KINDS:
                violations.append(f"robotKind: '{aid}' garrisons unrecognised kind '{g.get('kind')}'")
            px, pz = g["x"], g["z"]
            if not (fp[0] <= px <= fp[1] and fp[2] <= pz <= fp[3]):
                violations.append(f"robotInArea: a garrison entry in '{aid}' at ({px:.1f},{pz:.1f}) is outside it")
            for cid, r in cover_rects:
                gap_x = max(r[0] - px, 0.0, px - r[1])
                gap_z = max(r[2] - pz, 0.0, pz - r[3])
                gap = (gap_x ** 2 + gap_z ** 2) ** 0.5
                if gap < MIN_ROBOT_COVER_GAP - 1e-6:
                    violations.append(f"robotCoverGap: a '{g['kind']}' in '{aid}' at ({px:.1f},{pz:.1f}) is "
                                       f"{gap:.2f} m from cover '{cid}' (floor {MIN_ROBOT_COVER_GAP} m)")

        # rule: per-area garrison/robot count matches the Areas tab, and composition covers it
        composition = area.get("composition", {})
        garrison_by_kind = {}
        for g in area.get("garrison", []):
            garrison_by_kind[g["kind"]] = garrison_by_kind.get(g["kind"], 0) + 1
        for kind, count in garrison_by_kind.items():
            if composition.get(kind, 0) < count:
                violations.append(f"compositionCap: '{aid}' garrisons {count} {kind}(s) but composition "
                                   f"only authors {composition.get(kind, 0)}")

        for kind in composition:
            if kind not in VALID_KINDS:
                violations.append(f"compositionKind: '{aid}' composition authors unrecognised kind '{kind}'")

    # ---- area-to-area overlap (every pair, not just neighbours) ----
    for i in range(len(combat_areas)):
        for j in range(i + 1, len(combat_areas)):
            if rects_overlap(footprint(combat_areas[i]), footprint(combat_areas[j])):
                violations.append(f"areaOverlap: '{combat_areas[i]['id']}' overlaps '{combat_areas[j]['id']}'")

    # ---- gates: width, wall-coincidence, axis alternation, single route ----
    gates = cfg["gates"]
    prev_axis = None
    for g in gates:
        from_area = areas_by_id.get(g["from"]["area"])
        to_area = areas_by_id.get(g["to"]["area"])
        if from_area is None or to_area is None:
            violations.append(f"gateArea: gate '{g['id']}' references a missing area")
            continue

        from_wall, to_wall = g["from"]["wall"], g["to"]["wall"]
        if to_wall != opposite(from_wall):
            violations.append(f"gateWalls: gate '{g['id']}' joins non-opposite walls {from_wall}/{to_wall}")

        if abs(wall_coord(from_area, from_wall) - wall_coord(to_area, to_wall)) > OVERLAP_TOLERANCE:
            violations.append(f"gateCoincidence: gate '{g['id']}' walls do not sit on the same line")

        if g["width"] < MIN_DOORWAY:
            violations.append(f"gateWidth: gate '{g['id']}' is {g['width']} m, under {MIN_DOORWAY} m")

        axis = "NS" if from_wall in ("N", "S") else "EW"
        if prev_axis is not None and axis == prev_axis:
            violations.append(f"gateAxisAlternation: gate '{g['id']}' repeats the previous gate's "
                               f"{axis} axis")
        prev_axis = axis

    # ---- single route: every area reachable from the entry stub via exactly this gate chain,
    # and (since this is meant to be a single corridor, not a branching graph) every area has
    # at most 2 gates touching it. ----
    touches = {}
    for g in gates:
        touches.setdefault(g["from"]["area"], []).append(g["id"])
        touches.setdefault(g["to"]["area"], []).append(g["id"])
    for aid, gs in touches.items():
        if len(gs) > 2:
            violations.append(f"singleRoute: area '{aid}' touches {len(gs)} gates {gs} — not a single route")

    reached = set()
    entry = next((a["id"] for a in cfg["areas"] if "entry" in (a.get("role") or "")), None)
    if entry is None:
        violations.append("entry: no area carries role 'entry'")
    else:
        frontier = [entry]
        reached.add(entry)
        while frontier:
            here = frontier.pop()
            for g in gates:
                for a, b in ((g["from"]["area"], g["to"]["area"]), (g["to"]["area"], g["from"]["area"])):
                    if a == here and b not in reached:
                        reached.add(b)
                        frontier.append(b)
        for area in combat_areas:
            if area["id"] not in reached:
                violations.append(f"mainPath: area '{area['id']}' is not reachable from the entry stub")

    if violations:
        print(f"FAIL: {len(violations)} violation(s) in {config_path}")
        for v in violations:
            print(f"  - {v}")
        return 1

    print(f"OK: {config_path} — {len(combat_areas)} areas, {cradle_total} cradles, all rules clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
