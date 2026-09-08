#!/usr/bin/env python
"""One-off generator for world3_config.json (MV-712). Not part of the build; run manually,
inspect/commit the output. Lays out a branching, revisit-loop world on a single row of 20m-wide
"columns" so every ordinary gate is a simple touching E/W join, and computes gate Z-targets from
each pair's actual wall-span overlap so nothing has to be hand-solved.
"""
import json

NORMAL_D = 20.0
TALL_D = 44.0
GAP_D = 4.0  # unused void between the two stacked limbs inside a tall column
COL_W = 20.0

WORLD1_PACING = [
    0.35, 1.1, 1.15, 0.9, 1.1, 1.2, 0.9, 1.25, 1.0, 1.05,
    1.3, 0.95, 1.1, 1.35, 0.9, 1.15, 1.4, 1.3, 0.354, 1.126,
    1.191, 0.943, 1.166, 1.286, 0.976, 1.37, 1.108, 1.176, 1.472, 1.087,
]

areas = {}
gates = []
cursor_x = 0.0


def add_area(area_id, index, role, w, d, z=0.0, **extra):
    a = {
        "id": area_id, "index": index, "name": extra.pop("name", area_id),
        "role": role,
        "origin": {"x": cursor_x, "z": z}, "size": {"w": w, "d": d},
        "garrisonDensity": extra.pop("garrisonDensity", "normal"),
    }
    a.update(extra)
    areas[area_id] = a
    return a


def z_span(a):
    return a["origin"]["z"], a["origin"]["z"] + a["size"]["d"]


def gate(gid, a_id, a_wall, b_id, b_wall, opens_with="primary", width=3.0):
    a, b = areas[a_id], areas[b_id]
    a_lo, a_hi = z_span(a)
    b_lo, b_hi = z_span(b)
    lo, hi = max(a_lo, b_lo), min(a_hi, b_hi)
    assert hi - lo >= width, f"{gid}: overlap {hi - lo} too small"
    target = (lo + hi) / 2.0
    a_pos = (target - a_lo) / (a_hi - a_lo)
    b_pos = (target - b_lo) / (b_hi - b_lo)
    gates.append({
        "id": gid,
        "from": {"area": a_id, "wall": a_wall, "pos": round(a_pos, 4)},
        "to": {"area": b_id, "wall": b_wall, "pos": round(b_pos, 4)},
        "width": width,
        "opensWith": opens_with,
    })


# --- stub -----------------------------------------------------------------------------------
cursor_x = -6.0
add_area("stub", 0, "entry", 6.0, NORMAL_D, z=0.0)
cursor_x = 0.0

# --- straight run a1-a2 ----------------------------------------------------------------------
add_area("a1", 1, "normal", COL_W, NORMAL_D, hasShed=True,
         shed={"id": "a1_shed", "x": cursor_x + 10.0, "z": 10.0, "produces": "reinforcements + power-up on destroy"})
cursor_x += COL_W
add_area("a2", 2, "normal", COL_W, NORMAL_D)
cursor_x += COL_W


def fork_merge(fork_id, fork_idx, limb_a_id, limb_a_idx, limb_b_id, limb_b_idx, merge_id, merge_idx):
    global cursor_x
    add_area(fork_id, fork_idx, "normal", COL_W, TALL_D,
              name=f"{fork_id} - Branch point")
    cursor_x += COL_W
    add_area(limb_a_id, limb_a_idx, "normal", COL_W, NORMAL_D, z=24.0, name=f"{limb_a_id} - Upper limb")
    add_area(limb_b_id, limb_b_idx, "normal", COL_W, NORMAL_D, z=0.0, name=f"{limb_b_id} - Lower limb")
    cursor_x += COL_W
    add_area(merge_id, merge_idx, "normal", COL_W, TALL_D, name=f"{merge_id} - Branch rejoin")
    cursor_x += COL_W

    n = fork_idx  # use fork's own index to keep gate ids traceable/unique
    gate(f"g_f{n}_a", fork_id, "E", limb_a_id, "W")
    gate(f"g_f{n}_b", fork_id, "E", limb_b_id, "W")
    gate(f"g_m{n}_a", limb_a_id, "E", merge_id, "W")
    gate(f"g_m{n}_b", limb_b_id, "E", merge_id, "W")


# --- branch 1: a3 forks to a4/a5, rejoins at a6 -----------------------------------------------
fork_merge("a3", 3, "a4", 4, "a5", 5, "a6", 6)

# --- revisit block a7-a12 (visited twice: floor pass then bridge-deck pass) ------------------
REVISIT = ["a7", "a8", "a9", "a10", "a11", "a12"]
for i, aid in enumerate(REVISIT):
    idx = 7 + i
    add_area(aid, idx, "normal", COL_W, NORMAL_D,
             name=f"Area {idx} - The Reef (double-pass)")
    if i > 0:
        gate(f"g{idx - 1}", REVISIT[i - 1], "E", aid, "W")
    cursor_x += COL_W

# --- branch 2: a13 forks to a14/a15, rejoins at a16 -------------------------------------------
fork_merge("a13", 13, "a14", 14, "a15", 15, "a16", 16)

# --- straight run a17-a19, boss1 at a18 --------------------------------------------------------
add_area("a17", 17, "normal", COL_W, NORMAL_D, hasShed=True,
         shed={"id": "a17_shed", "x": cursor_x + 10.0, "z": 10.0, "produces": "reinforcements + power-up on destroy"})
cursor_x += COL_W
add_area("a18", 18, "boss", COL_W, NORMAL_D, garrisonDensity="none",
         boss={"id": "a18_boss", "x": cursor_x + 10.0, "z": 10.0, "size": {"w": 3.6, "d": 3.6}})
cursor_x += COL_W
add_area("a19", 19, "normal", COL_W, NORMAL_D)
cursor_x += COL_W

# --- branch 3: a20 forks to a21/a22, rejoins at a23 -------------------------------------------
fork_merge("a20", 20, "a21", 21, "a22", 22, "a23", 23)

# --- straight run a24-a25, boss2 at a26 ---------------------------------------------------------
add_area("a24", 24, "normal", COL_W, NORMAL_D, hasShed=True,
         shed={"id": "a24_shed", "x": cursor_x + 10.0, "z": 10.0, "produces": "reinforcements + power-up on destroy"})
cursor_x += COL_W
add_area("a25", 25, "normal", COL_W, NORMAL_D)
cursor_x += COL_W
add_area("a26", 26, "boss", COL_W, NORMAL_D, garrisonDensity="none",
         boss={"id": "a26_boss", "x": cursor_x + 10.0, "z": 10.0, "size": {"w": 3.6, "d": 3.6}})
cursor_x += COL_W

# --- straight run a27-a29, final boss (Anchorhead) at a30 ---------------------------------------
add_area("a27", 27, "normal", COL_W, NORMAL_D, hasShed=True,
         shed={"id": "a27_shed", "x": cursor_x + 10.0, "z": 10.0, "produces": "reinforcements + power-up on destroy"})
cursor_x += COL_W
add_area("a28", 28, "normal", COL_W, NORMAL_D)
cursor_x += COL_W
add_area("a29", 29, "normal", COL_W, NORMAL_D)
cursor_x += COL_W
add_area("a30", 30, "boss+exit", COL_W, NORMAL_D, garrisonDensity="none",
         name="a30 - Anchorhead's Cargo Hold",
         boss={"id": "anchorhead", "x": cursor_x + 10.0, "z": 10.0, "size": {"w": 4.0, "d": 4.0}})
cursor_x += COL_W

# --- the main chain gates (columns/forks/merges wired in area-creation order) ------------------
ORDER = [
    ("stub", "a1"), ("a1", "a2"), ("a2", "a3"),
    # a3 fork handled inside fork_merge()
    ("a6", "a7"),
    # a7..a12 internal gates handled in the loop above
    ("a12", "a13"),
    # a13 fork handled inside fork_merge()
    ("a16", "a17"), ("a17", "a18"), ("a18", "a19"), ("a19", "a20"),
    # a20 fork handled inside fork_merge()
    ("a23", "a24"), ("a24", "a25"), ("a25", "a26"), ("a26", "a27"),
    ("a27", "a28"), ("a28", "a29"), ("a29", "a30"),
]
main_gates = []
n = 100
for a_id, b_id in ORDER:
    opens = "start" if a_id == "stub" else "primary"
    gid = "g0" if a_id == "stub" else f"g{n}"
    n += 1
    main_gates.append((gid, a_id, b_id, opens))

# Insert these into `gates` interleaved isn't necessary — order doesn't matter to the engine —
# but build them now (gate() appends to the global `gates` list already populated by fork_merge
# and the revisit loop above, so just append the rest here).
for gid, a_id, b_id, opens in main_gates:
    gate(gid, a_id, "E", b_id, "W", opens_with=opens)

# --- garrison split for the revisit block (MV-711: level selects the visit) --------------------
for aid in REVISIT:
    a = areas[aid]
    ox, oz = a["origin"]["x"], a["origin"]["z"]
    a["garrison"] = [
        {"kind": "rusher", "level": 0, "x": ox + 6.0, "z": oz + 6.0},
        {"kind": "rusher", "level": 0, "x": ox + 14.0, "z": oz + 6.0},
        {"kind": "rusher", "level": 1, "x": ox + 6.0, "z": oz + 14.0},
        {"kind": "rusher", "level": 1, "x": ox + 14.0, "z": oz + 14.0},
    ]

# --- bridges: 4 spans within the revisit block, each skipping one interior room, staggered in Z
bridges = [
    {"id": "b1", "width": 4.0, "height": 4.0,
     "from": {"area": "a7", "wall": "E", "pos": 0.15}, "to": {"area": "a9", "wall": "W", "pos": 0.15}},
    {"id": "b2", "width": 4.0, "height": 4.0,
     "from": {"area": "a8", "wall": "E", "pos": 0.375}, "to": {"area": "a10", "wall": "W", "pos": 0.375}},
    {"id": "b3", "width": 4.0, "height": 4.0,
     "from": {"area": "a9", "wall": "E", "pos": 0.6}, "to": {"area": "a11", "wall": "W", "pos": 0.6}},
    {"id": "b4", "width": 4.0, "height": 4.0,
     "from": {"area": "a10", "wall": "E", "pos": 0.825}, "to": {"area": "a12", "wall": "W", "pos": 0.825}},
]

# --- route[]: 36 visits, a7-a12 twice (level 0 then level 1) -----------------------------------
route = []
for idx in range(1, 31):
    aid = f"a{idx}"
    if aid in REVISIT:
        route.append({"area": aid, "level": 0})
        route.append({"area": aid, "level": 1})
    else:
        route.append({"area": aid, "level": 0})

assert len(route) == 36, len(route)

cfg = {
    "$schema": "MaxVsTheWorlds/world-config@0.6-draft",
    "world": "World 3 — The Reef",
    "revision": "WORLD 3 v1 2026-09-09: areas a1-a30 authored per MV-712 from the MVW_World3_Level_Design_V1.xlsx dial table. Composition is fully dial-solved (no per-area authoring) with enemyTypes pinned to the engine's own ThreatValues placeholders, so the solved threat budget tracks each area's target budget by construction.",
    "note": "MV-712: baseThreat/threatGrowth/toughnessCurve are the ticket's proposed dial table, unchanged here. pacingRhythm is reused verbatim from world1_config.json (the framework's own saw-tooth shape, not bespoke to a world identity).",
    "units": "1 world unit = 1 metre = 1 Max-width (Max footprint 1x1)",
    "wallHeight": 1.5,
    "dials": {
        "areaCount": 30,
        "baseThreat": 45.0,
        "threatGrowth": 0.09,
        "band": {"up": 0.40, "down": -0.15},
        "pacingRhythm": WORLD1_PACING,
        "toughnessCurve": {
            "heavyFromArea": 1,
            "bruteFromArea": 3,
            "toughSubstitutionPct": 0.35,
            "tankShareEnd": 0.80,
            "gunnerFromArea": 1,
            "launcherFromArea": 1,
            "blinkerFromArea": 2,
            "specialSharePct": 18,
        },
        "powerupCadence": 2,
    },
    "enemyTypes": {
        "small": {"thv": 1.0},
        "large": {"thv": 2.5},
        "heavy": {"thv": 4.5},
        "brute": {"thv": 7.0},
        "gunner": {"thv": 3.0},
        "launcher": {"thv": 3.6},
        "blinker": {"thv": 3.3},
    },
    "gates": gates,
    "bridges": bridges,
    "route": route,
    "areas": [areas["stub"]] + [areas[f"a{i}"] for i in range(1, 31)],
}

with open("Assets/_Project/Resources/Worlds/world3_config.json", "w", encoding="utf-8") as f:
    json.dump(cfg, f, indent=1)
    f.write("\n")

print("areas:", len(cfg["areas"]), "gates:", len(cfg["gates"]), "bridges:", len(cfg["bridges"]), "route:", len(cfg["route"]))
