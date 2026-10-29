#!/usr/bin/env python3
"""MV-1308: independently checks city_config.json's "pylons[]" against the ticket's own rule.

A SEPARATE program from add_city_pylons.py -- it never imports it, and only re-reads the JSON that
generator already wrote (generate-then-verify: a generator cannot certify its own output). Exit 0
means clean; exit non-zero prints every rule it found broken, naming the rule.

Run: python Tools/MV1308/check_city_pylons.py
"""
import json
import math
import pathlib
import sys

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
CITY_PATH = REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "city_config.json"

MIN_PYLON_COVER_CLEARANCE = 2.0
PYLON_CLEARANCE_EXEMPT_FEEDS = "a5_rep1"
PYLON_CLEARANCE_EXEMPT_VALUE = 1.85
MIN_PYLON_SEPARATION = 3.0
RING_MIN = 6.0
RING_MAX = 10.0
PYLON_HALF = 0.7
GATE_MOUTH_EPS = 1e-6


def required_clearance(feeds_id):
    return PYLON_CLEARANCE_EXEMPT_VALUE if feeds_id == PYLON_CLEARANCE_EXEMPT_FEEDS else MIN_PYLON_COVER_CLEARANCE


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


def gate_geometry(areas_by_id, gate):
    """Returns (axis, fixed_coord, span_lo, span_hi) -- axis "x" means the gate sits on a
    constant-X wall (E/W) with its span running along Z; axis "z" means a constant-Z wall (N/S)
    with its span running along X. Matches WorldMapLoader.ResolveDoorPosition's own geometry."""
    from_area = areas_by_id[gate["from"]["area"]]
    to_area = areas_by_id[gate["to"]["area"]]
    from_wall, to_wall = gate["from"]["wall"], gate["to"]["wall"]
    from_lo, from_hi = wall_span(from_area, from_wall)
    to_lo, to_hi = wall_span(to_area, to_wall)
    pos_from = from_lo + min(max(gate["from"]["pos"], 0.0), 1.0) * (from_hi - from_lo)
    pos_to = to_lo + min(max(gate["to"]["pos"], 0.0), 1.0) * (to_hi - to_lo)
    along = (pos_from + pos_to) / 2.0
    fixed = wall_coord(from_area, from_wall)
    half = gate["width"] / 2.0
    axis = "z" if from_wall in ("N", "S") else "x"
    return axis, fixed, along - half, along + half


def gate_point(areas_by_id, gate):
    axis, fixed, lo, hi = gate_geometry(areas_by_id, gate)
    along = (lo + hi) / 2.0
    return (along, fixed) if axis == "z" else (fixed, along)


def cable_crosses_gate(px, pz, tx, tz, axis, fixed, lo, hi):
    """Pylon.cs's own BuildCable L-route: horizontal leg (z=pz, x: px->tx), then vertical leg
    (x=tx, z: pz->tz). True if either leg crosses this gate's wall line within its span."""
    if axis == "z":
        # vertical leg crosses a N/S wall (constant z) if that z lies strictly between the leg's
        # own z extent and tx falls inside the gate's x-span.
        zlo, zhi = min(pz, tz), max(pz, tz)
        if zlo + GATE_MOUTH_EPS < fixed < zhi - GATE_MOUTH_EPS and lo <= tx <= hi:
            return True
    else:
        # horizontal leg crosses an E/W wall (constant x) if that x lies strictly between the
        # leg's own x extent and pz falls inside the gate's z-span.
        xlo, xhi = min(px, tx), max(px, tx)
        if xlo + GATE_MOUTH_EPS < fixed < xhi - GATE_MOUTH_EPS and lo <= pz <= hi:
            return True
    return False


def main():
    with open(CITY_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    areas_by_id = {a["id"]: a for a in cfg["areas"]}
    gates_by_from = {g["from"]["area"]: g for g in cfg["gates"]}
    gates_by_to = {g["to"]["area"]: g for g in cfg["gates"]}

    violations = []
    total_pylons = 0

    for area in cfg["areas"]:
        aid = area["id"]
        if not (aid[0] == "a" and aid[1:].isdigit()):
            continue
        idx = area["index"]
        if idx == 30:
            continue  # MV-1309's own, not this ticket's

        pylons = area.get("pylons") or []
        relays = area.get("replicators") or []

        if idx < 2:
            if pylons:
                violations.append(f"'{aid}' (index {idx}) authors pylons but has no relay band")
            continue

        expected_per_relay = 1 if idx <= 12 else 2
        by_feeds = {}
        for p in pylons:
            by_feeds.setdefault(p["feeds"], []).append(p)

        relay_ids = {r["id"] for r in relays}
        if set(by_feeds.keys()) != relay_ids:
            violations.append(f"'{aid}': pylons feed {sorted(by_feeds.keys())}, relays are {sorted(relay_ids)}")

        for relay in relays:
            feeds_list = by_feeds.get(relay["id"], [])
            if len(feeds_list) != expected_per_relay:
                violations.append(f"'{relay['id']}' has {len(feeds_list)} pylon(s), expected {expected_per_relay}")

        x0, z0 = area["origin"]["x"], area["origin"]["z"]
        x1, z1 = x0 + area["size"]["w"], z0 + area["size"]["d"]
        cover = area.get("cover") or []

        entry_gate = gates_by_to.get(aid)
        exit_gate = gates_by_from.get(aid)
        gate_geoms = []
        if entry_gate is not None:
            gate_geoms.append(gate_geometry(areas_by_id, entry_gate))
        if exit_gate is not None:
            gate_geoms.append(gate_geometry(areas_by_id, exit_gate))
        gate_pts = [((lo + hi) / 2.0, fixed) if axis == "z" else (fixed, (lo + hi) / 2.0)
                    for axis, fixed, lo, hi in gate_geoms]

        relays_by_id = {r["id"]: r for r in relays}

        for p in pylons:
            total_pylons += 1
            pid = p["id"]
            px, pz = p["x"], p["z"]
            clearance = required_clearance(p["feeds"])

            if not (x0 + PYLON_HALF - 1e-6 <= px <= x1 - PYLON_HALF + 1e-6 and
                    z0 + PYLON_HALF - 1e-6 <= pz <= z1 - PYLON_HALF + 1e-6):
                violations.append(f"'{pid}' in '{aid}' is not on walkable floor (its own 1.4 m "
                                   f"footprint does not fit entirely inside the area)")

            relay = relays_by_id.get(p["feeds"])
            if relay is None:
                violations.append(f"'{pid}' feeds '{p['feeds']}', which is not a relay in '{aid}'")
                continue

            ring = math.hypot(px - relay["x"], pz - relay["z"])
            if not (RING_MIN - 1e-6 <= ring <= RING_MAX + 1e-6):
                violations.append(f"'{pid}' is {ring:.3f} m from '{relay['id']}', outside "
                                   f"[{RING_MIN}, {RING_MAX}] m")

            for cov in cover:
                cx0 = cov["x"] - cov["width"] / 2.0
                cx1 = cov["x"] + cov["width"] / 2.0
                cz0 = cov["z"] - cov["depth"] / 2.0
                cz1 = cov["z"] + cov["depth"] / 2.0
                dx = max(0.0, max(cx0 - px, px - cx1))
                dz = max(0.0, max(cz0 - pz, pz - cz1))
                dist = math.hypot(dx, dz)
                if dist < clearance - 1e-6:
                    violations.append(f"'{pid}' is {dist:.3f} m from cover '{cov['id']}' in '{aid}', "
                                       f"under {clearance} m")

            for gx, gz in gate_pts:
                dist = math.hypot(px - gx, pz - gz)
                if dist < clearance - 1e-6:
                    violations.append(f"'{pid}' is {dist:.3f} m from a gate in '{aid}', under {clearance} m")

            for axis, fixed, lo, hi in gate_geoms:
                if cable_crosses_gate(px, pz, relay["x"], relay["z"], axis, fixed, lo, hi):
                    violations.append(f"'{pid}'->'{relay['id']}' cable route crosses a gate mouth in '{aid}'")

        for i in range(len(pylons)):
            for j in range(i + 1, len(pylons)):
                dist = math.hypot(pylons[i]["x"] - pylons[j]["x"], pylons[i]["z"] - pylons[j]["z"])
                if dist < MIN_PYLON_SEPARATION - 1e-6:
                    violations.append(f"'{pylons[i]['id']}' and '{pylons[j]['id']}' in '{aid}' are "
                                       f"{dist:.3f} m apart, under {MIN_PYLON_SEPARATION} m")

    if total_pylons != 98:
        violations.append(f"total pylon count is {total_pylons}, expected 98")

    if violations:
        print(f"FAIL: {len(violations)} violation(s)")
        for v in violations:
            print(f"  - {v}")
        return 1

    print(f"OK: {total_pylons} pylons across a2-a29, all rules clean")
    return 0


if __name__ == "__main__":
    sys.exit(main())
