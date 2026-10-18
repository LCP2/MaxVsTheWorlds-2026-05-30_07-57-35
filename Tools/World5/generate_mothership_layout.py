#!/usr/bin/env python3
"""MV-1233: generates the Mothership's full 30-area layout into mothership_config.json.

Stdlib only (zipfile + xml.etree parses the Areas tab directly out of the .xlsx — no openpyxl
dependency). This is ONE of the two programs the ticket asks for; the checker
(check_mothership_layout.py) is a SEPARATE program that never imports this one — it re-reads
only the JSON this script writes, the same "a generator cannot certify its own output" split the
generate-then-verify skill asks for.

Area 1 (Hangar Bay) is MV-1232's own hand-authored build and is carried through UNCHANGED except
for its "role" (no longer the slice's exit now that 29 more areas follow it). Areas 2-30 are
generated here from the Areas tab of the design workbook, chained onto area 1's own existing
(fixed) exit gate.

Run: python Tools/World5/generate_mothership_layout.py
Writes:
  Assets/_Project/Resources/Worlds/mothership_config.json  (the shipped config)
  C:/Dev/MaxVsTheWorlds-Images/MV-1233.mothership_config.json  (the ticket's own record copy)
"""
import hashlib
import json
import pathlib
import xml.etree.ElementTree as ET
import zipfile

REPO_ROOT = pathlib.Path(__file__).resolve().parents[2]
CONFIG_PATH = REPO_ROOT / "Assets" / "_Project" / "Resources" / "Worlds" / "mothership_config.json"
RECORD_COPY_PATH = pathlib.Path(r"C:\Dev\MaxVsTheWorlds-Images\MV-1233.mothership_config.json")
DESIGN_WORKBOOK = pathlib.Path(r"C:\Dev\Worlds\World5\Design\MVW_World5_Design.xlsx")

NS = {"s": "http://schemas.openxmlformats.org/spreadsheetml/2006/main"}

# ---------------------------------------------------------------------------------------
# Level-design constants (Assets/_Project/Resources/Worlds/level_design_constraints.json,
# MapValidation.cs, and the World tab of the design workbook) — the generator builds to these
# so the checker (which re-asserts the same numbers independently) has something to find clean.
# ---------------------------------------------------------------------------------------
MIN_DOORWAY = 3.0
MIN_ROOM_DIM = 18.0
MIN_FREE_CHANNEL = 2.0
MIN_ROBOT_COVER_GAP = 2.0          # level_design_constraints.json minRobotToCoverGapMetres
COVER_HEIGHT = 1.6                 # World tab B10: "Max cover height 1.6 m — nothing taller"
LOW_COVER_HEIGHT = 0.6
CRADLE_SIZE = 3.0                  # WorldMapLoader.CradleSize
CRADLE_PAD_HALF = 1.0              # AssemblyCradle's own 2x2 m out pad
CRADLE_LANE_HALF_LEN = 1.5         # the ticket's own 3x2 m lane
CRADLE_LANE_HALF_WIDTH = 1.0
WALL_MARGIN = 4.0                  # generic "stay clear of walls" inset for cover/cradles
ROBOT_GRID_STEP = 2.0

# Facing -> outward unit vector, MapValidation.FacingDirection's own N=+Z/E=+X/S=-Z/W=-X compass.
FACING_DIR = {"N": (0.0, 1.0), "E": (1.0, 0.0), "S": (0.0, -1.0), "W": (-1.0, 0.0)}


def rects_overlap(r1, r2, tol=0.01):
    ax0, ax1, az0, az1 = r1
    bx0, bx1, bz0, bz1 = r2
    return ax0 < bx1 - tol and ax1 > bx0 + tol and az0 < bz1 - tol and az1 > bz0 + tol

VALID_KINDS_AT_PICKUP = {
    "rusher", "turret", "gunner", "brute", "bruiser", "heavy", "launcher", "blinker",
    "bolter", "skitter", "breaker",
}

# area index a kind first becomes available, per the Robots tab + Areas tab "Purpose" notes.
# Welder/Tractor/Orbitals/plasma vents are later tickets (MV-1244/1238/-/1240) and never appear.
UNLOCK_AREA = {
    "rusher": 1, "turret": 1, "gunner": 1, "bruiser": 1, "launcher": 2, "blinker": 3,
    "bolter": 5, "heavy": 5, "skitter": 6, "brute": 9, "breaker": 19,
}
WEIGHT = {
    "rusher": 10, "turret": 4, "gunner": 5, "bruiser": 5, "launcher": 3, "blinker": 3,
    "bolter": 3, "heavy": 3, "skitter": 4, "brute": 3, "breaker": 2,
}


def _cell_text(cells, col):
    return cells.get(col, "")


def read_areas_tab():
    """Parses the Areas tab (sheet11.xml inside the .xlsx, a plain zip) with stdlib only."""
    z = zipfile.ZipFile(DESIGN_WORKBOOK)
    data = z.read("xl/worksheets/sheet11.xml")
    root = ET.fromstring(data)
    sheet_data = root.find("s:sheetData", NS)

    rows = {}
    for row in sheet_data.findall("s:row", NS):
        r = int(row.get("r"))
        cells = {}
        for c in row.findall("s:c", NS):
            col = "".join(ch for ch in c.get("r") if ch.isalpha())
            is_el = c.find("s:is", NS)
            v_el = c.find("s:v", NS)
            if is_el is not None:
                t_el = is_el.find("s:t", NS)
                val = t_el.text if t_el is not None else ""
            elif v_el is not None:
                val = v_el.text
            else:
                val = ""
            cells[col] = val
        rows[r] = cells

    areas = {}
    for r in range(4, 34):
        c = rows.get(r, {})
        if not c:
            continue
        idx = int(_cell_text(c, "A"))
        name = _cell_text(c, "C")
        structure = _cell_text(c, "D")
        size_raw = _cell_text(c, "E")
        cradles = int(_cell_text(c, "F") or 0)
        robots = int(_cell_text(c, "G") or 0)
        w, d = parse_size(size_raw)
        areas[idx] = dict(index=idx, name=name, structure=structure, w=w, d=d,
                           cradles=cradles, robots=robots, size_raw=size_raw)
    return areas


def parse_size(raw):
    """'48 x 30' / '44 x 44 octagon' / '48 round' -> (w, d). A round area's bounding box is a
    square of its one given dimension (the only shape info a rect-only engine footprint can use;
    see the ticket's own "no polygon area support" finding — octagon/round are cover-dressing
    only, never a true footprint change)."""
    raw = raw.replace("\u00d7", "x").replace("\ufffd", "x")
    parts = raw.replace("x", " ").split()
    nums = [float(p) for p in parts if p.replace(".", "", 1).isdigit()]
    if len(nums) == 1:
        return nums[0], nums[0]
    return nums[0], nums[1]


# ---------------------------------------------------------------------------------------
# Composition (per-area kind mix) — deterministic, proportional, largest-remainder allocation
# over whichever kinds are unlocked by this area's index. Area 30 is forced to carry exactly
# one Breaker (Areas tab H33: "One Breaker among the 30 robots").
# ---------------------------------------------------------------------------------------
def composition_for(area_index, total):
    available = [k for k, unlock in UNLOCK_AREA.items() if area_index >= unlock]
    weights = [WEIGHT[k] for k in available]
    wsum = sum(weights)
    raw = [total * w / wsum for w in weights]
    base = [int(r) for r in raw]
    remainder = total - sum(base)
    order = sorted(range(len(available)), key=lambda i: -(raw[i] - base[i]))
    i = 0
    while remainder > 0:
        base[order[i % len(order)]] += 1
        remainder -= 1
        i += 1

    counts = {k: v for k, v in zip(available, base)}

    if area_index == 30:
        if counts.get("breaker", 0) < 1:
            donor = max((k for k in counts if k != "breaker"), key=lambda k: counts[k])
            counts[donor] -= 1
            counts["breaker"] = counts.get("breaker", 0) + 1

    return {k: v for k, v in counts.items() if v > 0}


# ---------------------------------------------------------------------------------------
# Cover templates — every generator here keeps gaps far above MIN_FREE_CHANNEL by construction
# (modest piece sizes, generous spacing) rather than being checked after the fact.
# ---------------------------------------------------------------------------------------
def pillar_row(area, n, dressing="ship_bulkhead_stub", size=1.6):
    w, d = area["w"], area["d"]
    out = []
    for i in range(n):
        fx = 0.3 + 0.4 * (i / max(n - 1, 1))
        fz = 0.35 if i % 2 == 0 else 0.65
        out.append(dict(x=area["x"] + w * fx, z=area["z"] + d * fz,
                         w=size, h=COVER_HEIGHT, d=size, shape="box", dressing=dressing))
    return out


def grid_cover(area, rows, cols, cell_w, cell_d, dressing, margin=WALL_MARGIN):
    w, d = area["w"], area["d"]
    usable_w = w - 2 * margin
    usable_d = d - 2 * margin
    out = []
    for r in range(rows):
        for c in range(cols):
            fx = (c + 0.5) / cols
            fz = (r + 0.5) / rows
            out.append(dict(x=area["x"] + margin + usable_w * fx, z=area["z"] + margin + usable_d * fz,
                             w=cell_w, h=COVER_HEIGHT, d=cell_d, shape="box", dressing=dressing))
    return out


def vent_maze(area, n_fingers, dressing="ship_bulkhead_stub"):
    w, d = area["w"], area["d"]
    finger_len = w * 0.6
    finger_w = 1.2
    spacing = d / (n_fingers + 1)
    out = []
    for i in range(n_fingers):
        z = area["z"] + spacing * (i + 1)
        from_west = i % 2 == 0
        x = area["x"] + finger_len / 2 if from_west else area["x"] + w - finger_len / 2
        out.append(dict(x=x, z=z, w=finger_len, h=COVER_HEIGHT, d=finger_w, shape="box", dressing=dressing))
    return out


def corner_cuts(area, cut_frac=0.22, dressing="ship_bulkhead_stub"):
    w, d = area["w"], area["d"]
    cut_w, cut_d = w * cut_frac, d * cut_frac
    out = []
    for cx, cz in ((0, 0), (1, 0), (0, 1), (1, 1)):
        x = area["x"] + (cut_w / 2 if cx == 0 else w - cut_w / 2)
        z = area["z"] + (cut_d / 2 if cz == 0 else d - cut_d / 2)
        out.append(dict(x=x, z=z, w=cut_w, h=COVER_HEIGHT, d=cut_d, shape="box", dressing=dressing))
    return out


TEMPLATE_COVER = {
    "Spine": lambda a: pillar_row(a, 2),
    "Cargo": lambda a: grid_cover(a, 3, 4, 3.0, 3.0, "ship_crate"),
    "Junction": lambda a: corner_cuts(a, 0.16),
    "Vent Hall": lambda a: vent_maze(a, 3),
    "Quarters": lambda a: grid_cover(a, 4, 3, 1.6, 3.0, "ship_crate_low"),
    "Pod Racks": lambda a: grid_cover(a, 2, 3, 1.4, 1.4, "ship_drop_pod", margin=6.0),
}


def hero_cover(area_index, area):
    if area_index == 8:  # Assembly Line
        w, d = area["w"], area["d"]
        conveyor = dict(x=area["x"] + w * 0.5, z=area["z"] + d * 0.5, w=w * 0.7, h=LOW_COVER_HEIGHT,
                         d=2.0, shape="box", dressing="ship_conveyor")
        gantries = [
            dict(x=area["x"] + w * 0.2, z=area["z"] + d * 0.2, w=2.0, h=COVER_HEIGHT, d=2.0,
                 shape="box", dressing="ship_bulkhead_stub"),
            dict(x=area["x"] + w * 0.8, z=area["z"] + d * 0.8, w=2.0, h=COVER_HEIGHT, d=2.0,
                 shape="box", dressing="ship_bulkhead_stub"),
        ]
        return [conveyor] + gantries
    if area_index == 15:  # Specimen Vault
        cases = grid_cover(area, 2, 2, 2.0, 2.0, "ship_glass_case", margin=10.0)
        return corner_cuts(area, 0.14) + cases
    if area_index == 22:  # Reactor Ring
        w, d = area["w"], area["d"]
        column = dict(x=area["x"] + w * 0.5, z=area["z"] + d * 0.5, w=8.0, h=COVER_HEIGHT, d=8.0,
                       shape="cylinder", dressing="none")
        return corner_cuts(area, 0.16) + [column]
    if area_index == 30:  # The Bridge
        consoles = grid_cover(area, 2, 2, 2.5, 1.5, "ship_console_bank", margin=12.0)
        return corner_cuts(area, 0.14) + consoles
    return []


# ---------------------------------------------------------------------------------------
# Cradle placement — searches a coarse candidate grid, crossed with all 4 facings, for a spot
# whose body+pad+lane clears every cover box already placed in this area AND every
# already-picked cradle's own reserved footprint (cover is generated first; cradles slot in
# around it, never the other way round).
# ---------------------------------------------------------------------------------------
def _cradle_reserved_rect(cx, cz, facing):
    dx, dz = FACING_DIR[facing]
    half = CRADLE_SIZE / 2
    pad_cx, pad_cz = cx + dx * (half + CRADLE_PAD_HALF), cz + dz * (half + CRADLE_PAD_HALF)
    lane_cx = pad_cx + dx * (CRADLE_PAD_HALF + CRADLE_LANE_HALF_LEN)
    lane_cz = pad_cz + dz * (CRADLE_PAD_HALF + CRADLE_LANE_HALF_LEN)
    far_x = lane_cx + dx * CRADLE_LANE_HALF_LEN
    far_z = lane_cz + dz * CRADLE_LANE_HALF_LEN
    near_x, near_z = cx - dx * half, cz - dz * half
    xs = [cx - half, cx + half, pad_cx - CRADLE_PAD_HALF, pad_cx + CRADLE_PAD_HALF,
          lane_cx - CRADLE_LANE_HALF_WIDTH, lane_cx + CRADLE_LANE_HALF_WIDTH, far_x, near_x]
    zs = [cz - half, cz + half, pad_cz - CRADLE_PAD_HALF, pad_cz + CRADLE_PAD_HALF,
          lane_cz - CRADLE_LANE_HALF_WIDTH, lane_cz + CRADLE_LANE_HALF_WIDTH, far_z, near_z]
    return min(xs), max(xs), min(zs), max(zs)


def cradle_reserved_rects(area, cradles):
    return [_cradle_reserved_rect(cr["x"], cr["z"], cr["facing"]) for cr in cradles]


def place_cradles(area_id, area, count, cover):
    if count == 0:
        return []

    fp = (area["x"], area["x"] + area["w"], area["z"], area["z"] + area["d"])
    cover_rects = [(c["x"] - c["w"] / 2, c["x"] + c["w"] / 2, c["z"] - c["d"] / 2, c["z"] + c["d"] / 2)
                   for c in cover]

    def clear(rect):
        if not (fp[0] + 0.5 <= rect[0] and rect[1] <= fp[1] - 0.5 and
                fp[2] + 0.5 <= rect[2] and rect[3] <= fp[3] - 0.5):
            return False
        return not any(rects_overlap(rect, cr) for cr in cover_rects)

    step = 2.0
    picked = []
    for facing in ("S", "N", "W", "E"):
        gx = area["x"] + 4.0
        while gx < area["x"] + area["w"] - 4.0 and len(picked) < count:
            gz = area["z"] + 4.0
            while gz < area["z"] + area["d"] - 4.0 and len(picked) < count:
                rect = _cradle_reserved_rect(gx, gz, facing)
                if clear(rect) and not any(rects_overlap(rect, p[2]) for p in picked):
                    picked.append((gx, gz, rect, facing))
                gz += step
            gx += step
        if len(picked) >= count:
            break

    if len(picked) < count:
        raise RuntimeError(f"area '{area_id}': could only place {len(picked)} of {count} cradles clear of cover")

    cradles = []
    for i, (cx, cz, _rect, facing) in enumerate(picked[:count]):
        comp = {"rusher": 1} if i == 0 else {"turret": 1}
        cradles.append(dict(id=f"{area_id}_cradle{i + 1}" if count > 1 else f"{area_id}_cradle",
                             x=round(cx, 2), z=round(cz, 2), facing=facing, composition=comp))
    return cradles


# ---------------------------------------------------------------------------------------
# Garrison placement — a 3 m candidate grid across the room interior, filtered to keep
# MIN_ROBOT_COVER_GAP clear of every cover box and clear of every cradle's reserved rect, then
# the first N candidates (deterministic snake order) are taken.
# ---------------------------------------------------------------------------------------
def place_garrison(area, cover, cradle_rects, composition):
    w, d = area["w"], area["d"]
    candidates = []
    gx = WALL_MARGIN
    row = 0
    while gx < w - WALL_MARGIN:
        gz = WALL_MARGIN
        col_points = []
        while gz < d - WALL_MARGIN:
            col_points.append((area["x"] + gx, area["z"] + gz))
            gz += ROBOT_GRID_STEP
        if row % 2 == 1:
            col_points.reverse()
        candidates.extend(col_points)
        gx += ROBOT_GRID_STEP
        row += 1

    def clear_of_cover(px, pz):
        for c in cover:
            half_w, half_d = c["w"] / 2 + MIN_ROBOT_COVER_GAP, c["d"] / 2 + MIN_ROBOT_COVER_GAP
            if abs(px - c["x"]) < half_w and abs(pz - c["z"]) < half_d:
                return False
        for (xmin, xmax, zmin, zmax) in cradle_rects:
            if xmin - MIN_ROBOT_COVER_GAP < px < xmax + MIN_ROBOT_COVER_GAP and \
               zmin - MIN_ROBOT_COVER_GAP < pz < zmax + MIN_ROBOT_COVER_GAP:
                return False
        return True

    free_points = [p for p in candidates if clear_of_cover(*p)]

    total = sum(composition.values())
    if len(free_points) < total:
        raise RuntimeError(f"area '{area['id']}': only {len(free_points)} free points for {total} robots")

    garrison = []
    i = 0
    for kind, count in composition.items():
        for _ in range(count):
            px, pz = free_points[i]
            garrison.append(dict(kind=kind, x=round(px, 2), z=round(pz, 2), level=0))
            i += 1
    return garrison


# ---------------------------------------------------------------------------------------
# Layout chaining — see the ticket's own research notes: every area is an axis-aligned rect
# (origin + size); gates require exactly-coincident touching walls. Gate axis strictly
# alternates N/S <-> E/W (gateAxisAlternation, level_design_constraints.json), continuing the
# already-built a1->a1_out_stub gate's own N/S axis. Placement look-ahead keeps every area's
# footprint entirely beyond the previous same-axis area's own far edge, which (proven in the
# ticket's own design pass) guarantees zero overlap between ANY two areas, not just neighbours.
# ---------------------------------------------------------------------------------------
def build_layout(areas_tab):
    a1 = dict(x=5.0, z=0.0, w=48.0, d=30.0)
    placed = {1: a1}
    gates = []

    exit_point = 5.0 + 0.854166666667 * 48.0  # a1's own fixed, already-shipped N-wall gate x = 46.0
    prev_rect = a1
    from_pos_for_gate1 = 0.854166666667  # a1's own fixed pos -- reused for g1's "from"

    for k in range(2, 31):
        tab = areas_tab[k]
        w, d = tab["w"], tab["d"]
        axis = "NS" if k % 2 == 0 else "EW"

        if axis == "NS":
            origin_z = prev_rect["z"] + prev_rect["d"]
            origin_x = exit_point - w / 2
            new = dict(x=origin_x, z=origin_z, w=w, d=d)
            to_pos = (exit_point - origin_x) / w
            from_wall, to_wall = "N", "S"
        else:
            origin_x = prev_rect["x"] + prev_rect["w"]
            origin_z = exit_point - d / 2
            new = dict(x=origin_x, z=origin_z, w=w, d=d)
            to_pos = (exit_point - origin_z) / d
            from_wall, to_wall = "E", "W"

        assert 0.05 < to_pos < 0.95, f"area{k}: gate pos {to_pos} too close to an edge"

        from_pos = from_pos_for_gate1 if k == 2 else 0.5
        gates.append(dict(id=f"g{k - 1}", from_area=f"a{k - 1}", from_wall=from_wall, from_pos=from_pos,
                           to_area=f"a{k}", to_wall=to_wall, to_pos=to_pos, axis=axis))

        placed[k] = new

        next_axis = "EW" if axis == "NS" else "NS"
        if k < 30:
            w_next, d_next = areas_tab[k + 1]["w"], areas_tab[k + 1]["d"]
            margin = 2.0
            if next_axis == "EW":
                far_edge = new["z"] + d
                naive_center = new["z"] + d / 2
                exit_point = max(naive_center, far_edge - d_next / 2 + margin)
            else:
                far_edge = new["x"] + w
                naive_center = new["x"] + w / 2
                exit_point = max(naive_center, far_edge - w_next / 2 + margin)
        prev_rect = new

    return placed, gates


def verify_no_overlaps(placed):
    ids = list(placed.keys())
    for i in range(len(ids)):
        for j in range(i + 1, len(ids)):
            a, b = placed[ids[i]], placed[ids[j]]
            ra = (a["x"], a["x"] + a["w"], a["z"], a["z"] + a["d"])
            rb = (b["x"], b["x"] + b["w"], b["z"], b["z"] + b["d"])
            if rects_overlap(ra, rb):
                raise RuntimeError(f"generator bug: area{ids[i]} overlaps area{ids[j]}")


def build_area_content(k, tab, rect):
    area_id = f"a{k}"
    area = dict(id=area_id, x=rect["x"], z=rect["z"], w=rect["w"], d=rect["d"])

    structure = tab["structure"]
    if structure == "Hero":
        cover = hero_cover(k, area)
    else:
        cover = TEMPLATE_COVER[structure](area)

    cradles = place_cradles(area_id, area, tab["cradles"], cover)
    cradle_rects = cradle_reserved_rects(area, cradles)
    composition = composition_for(k, tab["robots"])
    garrison = place_garrison(area, cover, cradle_rects, composition)

    assert sum(composition.values()) == tab["robots"]
    assert len(garrison) == tab["robots"]
    assert len(cradles) == tab["cradles"]
    for kind in composition:
        assert kind in VALID_KINDS_AT_PICKUP, f"area{k} authored an unavailable kind '{kind}'"

    role = "exit" if k == 30 else "normal"

    return dict(
        id=area_id, index=k, name=tab["name"], role=role,
        origin=dict(x=round(rect["x"], 4), z=round(rect["z"], 4)),
        size=dict(w=rect["w"], d=rect["d"]),
        hasShed=False, garrisonDensity="none",
        cover=[dict(id=f"{area_id}_cover{i + 1}", x=round(c["x"], 2), z=round(c["z"], 2),
                     width=c["w"], height=c["h"], depth=c["d"], shape=c["shape"], dressing=c["dressing"],
                     coverKind="solid")
               for i, c in enumerate(cover)],
        composition=composition,
        garrison=garrison,
        cradles=cradles,
    )


def main():
    areas_tab = read_areas_tab()
    assert sum(a["cradles"] for a in areas_tab.values()) == 47, "Areas tab cradle sum must be 47"
    assert sum(a["robots"] for a in areas_tab.values()) == 903, "Areas tab robot sum must be 903"

    placed, gate_plan = build_layout(areas_tab)
    verify_no_overlaps(placed)

    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)

    # area 1 is MV-1232's own build, untouched except role (no longer the slice's own exit now
    # that 29 more areas follow it).
    a1_entry = next(a for a in cfg["areas"] if a["id"] == "a1")
    a1_entry["role"] = "normal"

    # drop the MV-1232 placeholder stub; areas 2-30 replace it.
    cfg["areas"] = [a for a in cfg["areas"] if a["id"] != "a1_out_stub"]

    new_areas = [build_area_content(k, areas_tab[k], placed[k]) for k in range(2, 31)]
    cfg["areas"].extend(new_areas)

    # g1 (a1's own existing gate) is repointed from the placeholder stub onto the real a2.
    g1 = next(g for g in cfg["gates"] if g["id"] == "g1")
    g1["to"] = dict(area="a2", wall="S", pos=round(gate_plan[0]["to_pos"], 6))
    g1["opensWith"] = "primary"

    for g in gate_plan[1:]:
        cfg["gates"].append(dict(
            id=g["id"],
            **{"from": dict(area=g["from_area"], wall=g["from_wall"], pos=round(g["from_pos"], 6))},
            to=dict(area=g["to_area"], wall=g["to_wall"], pos=round(g["to_pos"], 6)),
            width=3,
            opensWith="primary",
        ))

    cfg["dials"]["areaCount"] = 30

    cfg["route"] = [dict(area=f"a{k}", level=0) for k in range(1, 31)]

    cfg["revision"] = ("WORLD 5 V3 2026-10-19 (MV-1233): full 30-area layout generated from "
                        "Tools/World5/generate_mothership_layout.py, reading the Areas tab of "
                        "MVW_World5_Design.xlsx (47 cradles, 903 robots). Area 1 (MV-1232) carried "
                        "unchanged; areas 2-30 are new. Welder, Tractor, Orbitals and plasma vents "
                        "are later tickets and author no slots here.")

    # newline="\n" on purpose (MV-1233 fix): Python's default text-mode write translates "\n" to
    # the platform line ending, which on Windows silently turns every LF into CRLF -- the file
    # that lands on disk then hashes differently from the in-memory `text` this function already
    # built, so a SHA-256 taken from `text` directly would quote a hash that does not match what
    # was actually saved. Writing LF-only and then hashing the REAL bytes read back off disk
    # keeps this correct regardless of platform.
    text = json.dumps(cfg, indent=2) + "\n"
    with open(CONFIG_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)

    RECORD_COPY_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(RECORD_COPY_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)

    digest = hashlib.sha256(CONFIG_PATH.read_bytes()).hexdigest()
    assert digest == hashlib.sha256(RECORD_COPY_PATH.read_bytes()).hexdigest(), \
        "the shipped config and the record copy must be byte-identical"
    print(f"wrote {CONFIG_PATH}")
    print(f"wrote {RECORD_COPY_PATH}")
    print(f"SHA-256: {digest}")


if __name__ == "__main__":
    main()
