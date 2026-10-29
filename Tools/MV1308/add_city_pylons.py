#!/usr/bin/env python3
"""MV-1308: adds "pylons[]" placements to every City relay area (a2-a29) in city_config.json.

a30 is MV-1309's own (already authored, untouched here). Per the ticket + MV-1315's decided
clearance rule:
  a2-a12: 1 pylon per relay; a13-a29: 2 pylons per relay (98 pylon instances total, matching
  MV1315PylonClearanceRuleTests' own derivation off city_config.json's real per-area relay
  counts).
  Each pylon: 6-10 m from its relay (centre to centre); >= RequiredPylonCoverClearance(feedsId)
  (MapValidation.cs -- 2.0 m, except a5_rep1's pylon at 1.85 m) from every cover piece's own
  footprint edge and every gate's own resolved world point, both measured from the pylon's CENTRE
  (a point placement, not its own footprint box) -- same convention every other point-authored
  placement in this schema already uses (a canister/garrison entry/parts-cache against cover, and a
  canister against a gate: Tools/MV1302's own is_candidate_clear, WorldMapLoader.IsClearForCache,
  MapValidation.WorldGarrison). Confirmed against Lee's own measured a5_rep1 exemption value
  (1.85 m, MV-1315): that number only reproduces as a point-to-footprint-edge distance, not a
  footprint-to-footprint one. >= 3 m from every other pylon, centre to centre (same convention as
  MapValidation.MinBossSeparation). Preferred, not hard-gated: the far
  side of the relay from the area's own entry gate (so reaching it means crossing the fight) --
  candidates are ranked by how far "past" the relay they land, away from the entry gate, and the
  highest-ranked one clearing every hard constraint is chosen.

  The cable route (Pylon.cs BuildCable's own L: horizontal from the pylon to the relay's X, then
  vertical to the relay) is never checked explicitly against a gate mouth here because it cannot
  cross one by construction: a gate's "fixed" world coordinate is always exactly one of its own
  area's four wall lines, and both the pylon (walkable-floor margin below) and the relay (already
  authored, interior to its own room) sit strictly inside those four lines -- so neither of the L's
  two axis-aligned legs, which only ever span the sub-rectangle between the pylon and the relay, can
  reach a wall line at all. check_city_pylons.py re-derives this and asserts it holds for real,
  rather than taking the geometric argument on faith.

  "On walkable floor" -- the City authors no sludge/hazard rects (unlike other worlds), so "inside
  the area, with the pylon's own 0.7 m half-footprint clear of every wall" is the whole of it.

One-off DATA patch, same "edit the already-shipped JSON directly, never re-run a generator on
content it doesn't own" pattern Tools/MV1302/add_canisters_and_cover.py already uses. Deterministic:
a fine but fixed polar grid per relay (1 degree x 0.05 m over the authored 6-10 m ring), so a second
run with no config changes reproduces byte-identical output.

Run: python Tools/MV1308/add_city_pylons.py
"""
import json
import math
import pathlib

import numpy as np

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
CITY_PATH = REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "city_config.json"
CITY_INDENT = 1  # matches city_config.json's own on-disk formatting (Tools/MV1302's own table)

PYLON_FOOTPRINT = 1.4          # WorldMapLoader.PylonFootprint
PYLON_HALF = PYLON_FOOTPRINT / 2.0
MIN_PYLON_COVER_CLEARANCE = 2.0  # MapValidation.MinPylonCoverClearance
PYLON_CLEARANCE_EXEMPT_FEEDS = "a5_rep1"
PYLON_CLEARANCE_EXEMPT_VALUE = 1.85  # MapValidation.PylonClearanceExemptValue
MIN_PYLON_SEPARATION = 3.0
RING_MIN = 6.0
RING_MAX = 10.0
WALL_MARGIN = PYLON_HALF  # the pylon's own footprint must fit entirely inside the area

ANGLE_STEP_DEG = 0.5
RADIUS_STEP = 0.02

# Coordinates are rounded for the JSON (ROUND_DECIMALS) -- which can itself erode a razor-thin
# clearance below its own bound. SAFETY_MARGIN pads every hard bound at search time so the rounded
# result still clears it with room to spare; a5_rep1 is exempted from the margin (not from rounding)
# because its 1.85 m exemption IS the geometric best case, with ~0.0003 m of its own slack above
# 1.85 -- there is no margin-plus-1.85 candidate anywhere in its ring.
ROUND_DECIMALS = 6
SAFETY_MARGIN = 0.01


def required_clearance(feeds_id):
    return PYLON_CLEARANCE_EXEMPT_VALUE if feeds_id == PYLON_CLEARANCE_EXEMPT_FEEDS else MIN_PYLON_COVER_CLEARANCE


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
    """Matches WorldMapLoader.ResolveDoorPosition exactly (Tools/MV1302's own gate_position)."""
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


def area_rect(area):
    x0, z0 = area["origin"]["x"], area["origin"]["z"]
    w, d = area["size"]["w"], area["size"]["d"]
    return x0, z0, x0 + w, z0 + d


def place_relay_pylons(area, relay, count, entry_point, cover_list, placed_this_area, rng_seed):
    """Returns `count` (x, z) pylon positions for `relay`, each 6-10 m from it, clear of
    `cover_list`/the area's own entry+exit gate points by required_clearance(relay id), and >= 3 m
    from every pylon already placed in this area (across every relay, earlier in iteration order)."""
    exempt = relay["id"] == PYLON_CLEARANCE_EXEMPT_FEEDS
    clearance = required_clearance(relay["id"])
    margin = 0.0 if exempt else SAFETY_MARGIN
    x0, z0, x1, z1 = area_rect(area)
    rx, rz = relay["x"], relay["z"]

    angles = np.deg2rad(np.arange(0.0, 360.0, ANGLE_STEP_DEG))
    radii = np.arange(RING_MIN, RING_MAX + 1e-9, RADIUS_STEP)
    aa, rr = np.meshgrid(angles, radii)
    px = (rx + rr * np.cos(aa)).ravel()
    pz = (rz + rr * np.sin(aa)).ravel()
    pr = rr.ravel()

    mask = (px >= x0 + WALL_MARGIN) & (px <= x1 - WALL_MARGIN) & \
           (pz >= z0 + WALL_MARGIN) & (pz <= z1 - WALL_MARGIN) & \
           (pr >= RING_MIN + margin) & (pr <= RING_MAX - margin)

    for cov in cover_list:
        cx0 = cov["x"] - cov["width"] / 2.0
        cx1 = cov["x"] + cov["width"] / 2.0
        cz0 = cov["z"] - cov["depth"] / 2.0
        cz1 = cov["z"] + cov["depth"] / 2.0
        dx = np.maximum(0.0, np.maximum(cx0 - px, px - cx1))
        dz = np.maximum(0.0, np.maximum(cz0 - pz, pz - cz1))
        dist = np.sqrt(dx * dx + dz * dz)
        mask &= dist >= clearance + margin - 1e-9

    for gx, gz in (area["_entry_point"], area["_exit_point"]):
        dist = np.sqrt((px - gx) ** 2 + (pz - gz) ** 2)
        mask &= dist >= clearance + margin - 1e-9

    idx = np.nonzero(mask)[0]
    if idx.size == 0:
        raise RuntimeError(f"relay '{relay['id']}': no candidate in the 6-10 m ring clears "
                            f"{clearance} m from cover/gates inside '{area['id']}'")

    # Preference: far side of the relay from the entry gate -- candidates that continue past the
    # relay, away from the entry point, rank first.
    ex, ez = entry_point
    dirx, dirz = rx - ex, rz - ez
    dirlen = math.hypot(dirx, dirz)
    if dirlen > 1e-6:
        dirx, dirz = dirx / dirlen, dirz / dirlen
    proj = (px[idx] - rx) * dirx + (pz[idx] - rz) * dirz

    order = np.argsort(-proj)  # highest projection (farthest past the relay) first
    idx = idx[order]

    chosen = []
    for i in idx:
        cx, cz = float(px[i]), float(pz[i])
        if any(math.hypot(cx - ox, cz - oz) < MIN_PYLON_SEPARATION + SAFETY_MARGIN - 1e-9
               for ox, oz in placed_this_area):
            continue
        if any(math.hypot(cx - ox, cz - oz) < MIN_PYLON_SEPARATION + SAFETY_MARGIN - 1e-9
               for ox, oz in chosen):
            continue
        chosen.append((cx, cz))
        if len(chosen) == count:
            break

    if len(chosen) < count:
        raise RuntimeError(f"relay '{relay['id']}': only placed {len(chosen)} of {count} pylons "
                            f">= {MIN_PYLON_SEPARATION} m apart inside '{area['id']}'")

    return chosen


def main():
    with open(CITY_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    areas_by_id = {a["id"]: a for a in cfg["areas"]}
    gates_by_from = {g["from"]["area"]: g for g in cfg["gates"]}
    gates_by_to = {g["to"]["area"]: g for g in cfg["gates"]}

    total_pylons = 0
    areas_touched = 0

    for area in cfg["areas"]:
        aid = area["id"]
        if not (aid[0] == "a" and aid[1:].isdigit()):
            continue
        idx = area["index"]
        if idx < 2 or idx > 29:
            continue  # a30 is MV-1309's own; a1/stub carry no relay

        entry_gate = gates_by_to.get(aid)
        exit_gate = gates_by_from.get(aid)
        if entry_gate is None or exit_gate is None:
            raise RuntimeError(f"area '{aid}': expected exactly one entry and one exit gate")

        area["_entry_point"] = gate_position(areas_by_id, entry_gate)
        area["_exit_point"] = gate_position(areas_by_id, exit_gate)

        per_relay = 1 if idx <= 12 else 2
        cover_list = area.get("cover") or []
        placed_this_area = []
        new_pylons = []

        for relay in area.get("replicators") or []:
            positions = place_relay_pylons(area, relay, per_relay, area["_entry_point"],
                                            cover_list, placed_this_area, f"{aid}:{relay['id']}")
            placed_this_area.extend(positions)
            for i, (px, pz) in enumerate(positions):
                new_pylons.append({
                    "id": f"{relay['id']}_pylon{i + 1}",
                    "x": round(px, ROUND_DECIMALS),
                    "z": round(pz, ROUND_DECIMALS),
                    "feeds": relay["id"],
                })

        area["pylons"] = new_pylons
        del area["_entry_point"]
        del area["_exit_point"]
        total_pylons += len(new_pylons)
        areas_touched += 1

    if total_pylons != 98:
        raise RuntimeError(f"placed {total_pylons} pylons total, expected 98 — aborting without writing")

    cfg["revision"] = (
        cfg.get("revision", "") +
        f" MV-1308: added \"pylons\" to a2-a29 ({total_pylons} total across {areas_touched} areas; "
        f"a30's own four, MV-1309, untouched) via Tools/MV1308/add_city_pylons.py."
    )

    text = json.dumps(cfg, indent=CITY_INDENT) + "\n"

    import re
    elite_inline = "[" + ", ".join(f'"{v}"' for v in cfg["dials"]["eliteAffixes"]) + "]"
    text = re.sub(r'"eliteAffixes": \[\s*[^\]]*?\]', f'"eliteAffixes": {elite_inline}', text, flags=re.S)

    with open(CITY_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)

    print(f"wrote {CITY_PATH}")
    print(f"pylons added: {total_pylons} across {areas_touched} areas")


if __name__ == "__main__":
    main()
