#!/usr/bin/env python3
"""MV-1244: adds the Welder's own placements onto the already-generated mothership_config.json.

This is a one-off DATA patch, the same "hand-add garrison entries to an already-shipped config"
pattern MV-1238 used for the Tractor (see MV1233MothershipLayoutTests.cs's own comment on that) --
generate_mothership_layout.py is MV-1233's own one-time generator from the design workbook and is
never re-run for a later kind's placements, so this script edits the JSON directly instead.

Does two things, per the ticket:
  1. Adds 2 Welder garrison entries each to areas 8, 12, 22, 24, beside that area's own cradle 1,
     clear of cover/other garrison/every cradle's own pad+lane by the level design floor (2 m), and
     bumps each of those 4 areas' own composition.welder to 2 (every garrisoned kind must be covered
     by a composition count -- MapValidation's garrison-vs-composition rule).
  2. Adds "welder": 1 to every cradle's own per-cradle composition dict in areas 8 onward (so the
     cradle itself occasionally builds one), per the ticket's own "let cradles in areas 8 onward
     include welder in their composition".

Run: python Tools/World5/add_welder_placements.py
"""
import json
import math
import pathlib

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
CONFIG_PATH = REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "mothership_config.json"

MIN_ROBOT_COVER_GAP = 2.0
CRADLE_SIZE = 3.0
CRADLE_PAD_HALF = 1.0
CRADLE_LANE_HALF_LEN = 1.5
CRADLE_LANE_HALF_WIDTH = 1.0
FACING_DIR = {"N": (0.0, 1.0), "E": (1.0, 0.0), "S": (0.0, -1.0), "W": (-1.0, 0.0)}

WELDER_GARRISON_AREAS = [8, 12, 22, 24]
WELDER_PER_AREA = 2


def cradle_reserved_rect(cr):
    dx, dz = FACING_DIR[cr["facing"]]
    half = CRADLE_SIZE / 2
    pad_cx, pad_cz = cr["x"] + dx * (half + CRADLE_PAD_HALF), cr["z"] + dz * (half + CRADLE_PAD_HALF)
    lane_cx = pad_cx + dx * (CRADLE_PAD_HALF + CRADLE_LANE_HALF_LEN)
    lane_cz = pad_cz + dz * (CRADLE_PAD_HALF + CRADLE_LANE_HALF_LEN)
    far_x, far_z = lane_cx + dx * CRADLE_LANE_HALF_LEN, lane_cz + dz * CRADLE_LANE_HALF_LEN
    near_x, near_z = cr["x"] - dx * half, cr["z"] - dz * half
    xs = [cr["x"] - half, cr["x"] + half, pad_cx - CRADLE_PAD_HALF, pad_cx + CRADLE_PAD_HALF,
          lane_cx - CRADLE_LANE_HALF_WIDTH, lane_cx + CRADLE_LANE_HALF_WIDTH, far_x, near_x]
    zs = [cr["z"] - half, cr["z"] + half, pad_cz - CRADLE_PAD_HALF, pad_cz + CRADLE_PAD_HALF,
          lane_cz - CRADLE_LANE_HALF_WIDTH, lane_cz + CRADLE_LANE_HALF_WIDTH, far_z, near_z]
    return min(xs), max(xs), min(zs), max(zs)


def is_clear(px, pz, area, cover, cradle_rects, placed):
    fx0, fx1 = area["origin"]["x"], area["origin"]["x"] + area["size"]["w"]
    fz0, fz1 = area["origin"]["z"], area["origin"]["z"] + area["size"]["d"]
    if not (fx0 + 0.5 <= px <= fx1 - 0.5 and fz0 + 0.5 <= pz <= fz1 - 0.5):
        return False
    for c in cover:
        half_w, half_d = c["width"] / 2 + MIN_ROBOT_COVER_GAP, c["depth"] / 2 + MIN_ROBOT_COVER_GAP
        if abs(px - c["x"]) < half_w and abs(pz - c["z"]) < half_d:
            return False
    for (xmin, xmax, zmin, zmax) in cradle_rects:
        if xmin - MIN_ROBOT_COVER_GAP < px < xmax + MIN_ROBOT_COVER_GAP and \
           zmin - MIN_ROBOT_COVER_GAP < pz < zmax + MIN_ROBOT_COVER_GAP:
            return False
    for g in area.get("garrison", []) + placed:
        if (px - g["x"]) ** 2 + (pz - g["z"]) ** 2 < (MIN_ROBOT_COVER_GAP * 0.5) ** 2:
            return False
    return True


def place_beside_cradle(area, cover, cradle_rects, anchor, count):
    picked = []
    radius = 2.5
    while radius < 20.0 and len(picked) < count:
        for angle_deg in range(0, 360, 20):
            if len(picked) >= count:
                break
            rad = math.radians(angle_deg)
            px = round(anchor["x"] + radius * math.cos(rad), 2)
            pz = round(anchor["z"] + radius * math.sin(rad), 2)
            if is_clear(px, pz, area, cover, cradle_rects, picked):
                picked.append({"kind": "welder", "x": px, "z": pz, "level": 0})
        radius += 1.0
    if len(picked) < count:
        raise RuntimeError(f"area '{area['id']}': could only place {len(picked)} of {count} Welders beside a cradle")
    return picked


def main():
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    areas_by_index = {a["index"]: a for a in cfg["areas"] if a["id"][0] == "a" and a["id"][1:].isdigit()}

    for idx in WELDER_GARRISON_AREAS:
        area = areas_by_index[idx]
        cover = area.get("cover", [])
        cradles = area.get("cradles", [])
        cradle_rects = [cradle_reserved_rect(cr) for cr in cradles]
        anchor = cradles[0]
        new_entries = place_beside_cradle(area, cover, cradle_rects, anchor, WELDER_PER_AREA)
        area["garrison"].extend(new_entries)
        area["composition"]["welder"] = area["composition"].get("welder", 0) + WELDER_PER_AREA

    cradle_total = 0
    for area in cfg["areas"]:
        if area["index"] < 8:
            continue
        for cr in area.get("cradles", []):
            cr["composition"]["welder"] = 1
            cradle_total += 1

    cfg["revision"] = (cfg["revision"] + " MV-1244: added 2 Welder garrison entries each to areas "
                        "8/12/22/24 beside cradle 1, and \"welder\": 1 onto every cradle's own "
                        "composition in areas 8 onward.")

    text = json.dumps(cfg, indent=2) + "\n"
    with open(CONFIG_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)

    print(f"wrote {CONFIG_PATH}")
    print(f"welder garrison entries added: {len(WELDER_GARRISON_AREAS) * WELDER_PER_AREA}")
    print(f"cradles given a welder composition entry: {cradle_total}")


if __name__ == "__main__":
    main()
