#!/usr/bin/env python3
"""MV-1311: adds one Welder garrison entry to each City area a21-a27 in city_config.json, standing
within 8 m of that area's own first pylon (city_config.json's own "pylons[0]" -- MV-1308's own
placement), so the Welder actually has a destroyed pylon to rebuild. Nothing else in these areas
moves.

One-off DATA patch, same "edit the already-shipped JSON directly" pattern Tools/MV1308/add_city_pylons.py
and Tools/World5/add_welder_placements.py both use. Deterministic: a fixed polar search starting at
1.5 m (clear of the pylon's own 0.7 m half-footprint, WorldMapLoader.PylonFootprint / 2) out to the
ticket's own 8 m ceiling, so a second run with no config changes reproduces byte-identical output.

Run: python Tools/MV1311/add_city_welders.py
"""
import json
import math
import pathlib

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
CITY_PATH = REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "city_config.json"
CITY_INDENT = 1  # matches city_config.json's own on-disk formatting (Tools/MV1308's own table)

WELDER_AREAS = list(range(21, 28))  # a21-a27
MAX_RANGE_FROM_PYLON = 8.0
MIN_START_RADIUS = 1.5  # clear of the pylon's own 0.7 m half-footprint
RADIUS_STEP = 0.5
ANGLE_STEP_DEG = 10
MIN_GARRISON_GAP = 1.0  # clear of any other garrison entry, centre to centre
MIN_COVER_GAP = 2.0     # same flat clearance Tools/World5/add_welder_placements.py uses


def is_clear(px, pz, area, cover, garrison):
    fx0, fx1 = area["origin"]["x"], area["origin"]["x"] + area["size"]["w"]
    fz0, fz1 = area["origin"]["z"], area["origin"]["z"] + area["size"]["d"]
    if not (fx0 + 0.5 <= px <= fx1 - 0.5 and fz0 + 0.5 <= pz <= fz1 - 0.5):
        return False
    for c in cover:
        half_w, half_d = c["width"] / 2 + MIN_COVER_GAP, c["depth"] / 2 + MIN_COVER_GAP
        if abs(px - c["x"]) < half_w and abs(pz - c["z"]) < half_d:
            return False
    for g in garrison:
        if (px - g["x"]) ** 2 + (pz - g["z"]) ** 2 < MIN_GARRISON_GAP ** 2:
            return False
    return True


def place_near_pylon(area, cover, garrison, anchor):
    radius = MIN_START_RADIUS
    while radius <= MAX_RANGE_FROM_PYLON:
        for angle_deg in range(0, 360, ANGLE_STEP_DEG):
            rad = math.radians(angle_deg)
            px = round(anchor["x"] + radius * math.cos(rad), 2)
            pz = round(anchor["z"] + radius * math.sin(rad), 2)
            if (px - anchor["x"]) ** 2 + (pz - anchor["z"]) ** 2 > MAX_RANGE_FROM_PYLON ** 2:
                continue
            if is_clear(px, pz, area, cover, garrison):
                return {"kind": "welder", "x": px, "z": pz, "level": 0}
        radius += RADIUS_STEP
    raise RuntimeError(f"area '{area['id']}': could not place a Welder within {MAX_RANGE_FROM_PYLON} m of its pylon")


def main():
    with open(CITY_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    areas_by_index = {a["index"]: a for a in cfg["areas"]}

    placed = 0
    for idx in WELDER_AREAS:
        area = areas_by_index[idx]
        pylons = area.get("pylons", [])
        if not pylons:
            raise RuntimeError(f"area '{area['id']}' has no pylons to garrison a Welder beside")
        anchor = pylons[0]
        cover = area.get("cover", [])
        garrison = area.get("garrison", [])
        entry = place_near_pylon(area, cover, garrison, anchor)
        area["garrison"].append(entry)
        area["composition"]["welder"] = area["composition"].get("welder", 0) + 1
        placed += 1

    cfg["revision"] = (cfg["revision"] + " MV-1311: added 1 Welder garrison entry each to areas "
                        "a21-a27, within 8 m of that area's own first pylon, via Tools/MV1311/add_city_welders.py.")

    text = json.dumps(cfg, indent=CITY_INDENT) + "\n"
    with open(CITY_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)

    print(f"wrote {CITY_PATH}")
    print(f"welder garrison entries added: {placed}")


if __name__ == "__main__":
    main()
