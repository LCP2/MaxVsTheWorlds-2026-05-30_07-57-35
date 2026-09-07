using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Arena
{
    /// <summary>Which wall of an area, in the world-config schema (MV-267, Confluence MVW 34439170
    /// §7). A rectangle has exactly four; a gate names one on each of the two areas it joins.</summary>
    public enum Wall { N, E, S, W }

    public static class WallEnums
    {
        public static bool TryParse(string s, out Wall wall) =>
            Enum.TryParse(s, ignoreCase: true, out wall);

        /// <summary>The wall a gate on the far side of this one connects to on a rectangular area —
        /// N faces S, E faces W. A gate that does not join opposite walls is not a doorway between two
        /// neighbouring rooms, it is two rooms poking through each other's corner.</summary>
        public static Wall Opposite(Wall wall) => wall switch
        {
            Wall.N => Wall.S,
            Wall.S => Wall.N,
            Wall.E => Wall.W,
            Wall.W => Wall.E,
            _ => wall,
        };
    }

    [Serializable]
    public sealed class WorldAreaOrigin
    {
        public float x;
        public float z;
    }

    [Serializable]
    public sealed class WorldAreaSize
    {
        public float w;
        public float d;
    }

    /// <summary>A shed area's factory position (MV-270) — where <see cref="WorldMapLoader"/> builds the
    /// <c>MowerHutch</c> that makes <see cref="WorldArea.hasShed"/> real. <see cref="id"/> is optional,
    /// authoring-only reference — <see cref="WorldArea.ShedId"/> is what actually names the built
    /// entity (MV-475), not this field.</summary>
    [Serializable]
    public sealed class WorldShed
    {
        public string id;
        public float x;
        public float z;
        public string produces;

        /// <summary>True once a world authors this shed to walk (MV-562 schema; MV-548 runtime). A
        /// mobile shed is validated exactly like a static one (MV-655) — the same spawn-ring cover
        /// clearance every shed gets, and the same <see cref="MapValidation.MinShedWallMargin"/> that
        /// only applies once an area carries more than one shed. Propagated onto the built
        /// <see cref="MapEntity"/> by <see cref="WorldMapLoader"/> and read by
        /// <see cref="MapRuntime.Build"/>/<see cref="MaxWorlds.Factories.MowerHutch"/>.</summary>
        public bool mobile;
    }

    /// <summary>One authored, exact robot placement (MV-559) — a designer's "this Blinker stands
    /// here" override for <see cref="MaxWorlds.Enemies.Garrison.SeedPositions"/>'s otherwise-even
    /// ring. <see cref="kind"/> is the same lowercase key set <see cref="WorldComposition"/> uses.</summary>
    [Serializable]
    public sealed class WorldGarrisonEntry
    {
        public string kind;
        public float x;
        public float z;

        /// <summary>Elevation tier (MV-697), default 0 (floor). 1 means this robot stands on a deck —
        /// <see cref="MaxWorlds.Enemies.Garrison.SeedSlots(WorldArea, int, WorldConfig)"/> resolves its
        /// spawn Y to the deck it lands on instead of the floor, and it is what
        /// <see cref="MaxWorlds.Enemies.RobotEnemy.SetLevel"/> reads to leash its steering to that same
        /// deck rather than letting it climb down.</summary>
        public int level;
    }

    /// <summary>Where the boss stands in its arena (MV-270) — the compost clearing's Big Bermuda.</summary>
    [Serializable]
    public sealed class WorldBoss
    {
        public string id;
        public float x;
        public float z;
        public WorldAreaSize size;
    }

    /// <summary>An area's authored, exact enemy composition (MV-365) — when present on a
    /// <see cref="WorldArea"/>, this replaces <see cref="WorldConfig.SolveComposition"/>'s
    /// dial-derived budget solve for that area entirely: the designer's own counts are the answer,
    /// not a target the solver approximates. This is what makes per-area composition "authored and
    /// tunable without a code change" (MV-365 AC5) — retuning an arena's enemy mix is a JSON edit to
    /// this block, same as retuning its cover.</summary>
    [Serializable]
    public sealed class WorldComposition
    {
        public int rusher;
        public int bruiser;
        public int heavy;
        public int brute;
        public int gunner;
        public int launcher;
        public int blinker;
        public int bolter; // MV-539: the fourth ranged/special kind, authored-composition only for now

        /// <summary>True if any kind actually has a count — the real "was this authored" signal.
        /// <c>JsonUtility</c> materialises a non-null <see cref="WorldComposition"/> for every area
        /// once ANY area in the array carries the field, even ones whose JSON omits it entirely (a
        /// documented JsonUtility quirk: nested <c>[Serializable]</c> class fields round-trip through
        /// their own default constructor rather than staying null when absent) — so a plain
        /// null-check would wrongly treat an un-authored area as "authored: 0 robots everywhere".</summary>
        public bool IsAuthored =>
            rusher > 0 || bruiser > 0 || heavy > 0 || brute > 0 || gunner > 0 || launcher > 0 ||
            blinker > 0 || bolter > 0;

        public DifficultyEngine.Composition ToEngineComposition() =>
            new DifficultyEngine.Composition(rusher, bruiser, heavy, brute, gunner, launcher, blinker, bolter);
    }

    /// <summary>A world's partial restat/reskin of one <see cref="EnemyKind"/> (MV-701) — e.g. World 2's
    /// Rusher wears this as the "Scrap Rat": same <see cref="EnemyKind"/>, same <see cref="EnemyShape"/>
    /// and collider (never overridable, per the ticket's own "do not re-raise"), different stats and
    /// look. <see cref="kind"/> is the same lowercase key set <see cref="WorldComposition"/>/
    /// <see cref="WorldGarrisonEntry"/> already use. Every OTHER field is optional: 0/empty/zero means
    /// "not authored" (the same idiom <see cref="WorldConfig.wallHeight"/> already uses), so a world
    /// that only wants to rename a kind doesn't have to also repeat its full base stat block. Resolved
    /// onto the base table by <see cref="MaxWorlds.Enemies.EnemyArchetype.For"/>.</summary>
    [Serializable]
    public sealed class WorldEnemyOverride
    {
        public string kind;
        public string displayName;
        public float moveSpeed;
        public float maxHealth;
        public float contactDamage;
        public Vector3 bodyScale;
        public string colourRole;
        public string skin;
    }

    /// <summary>A sludge slow-zone (MV-692) — a rect in AREA-LOCAL metres (like <see cref="WorldArea.origin"/>,
    /// <see cref="x"/>/<see cref="z"/> are the rect's MIN corner, not its centre), authored inside the
    /// area that carries it. Any mover whose feet fall inside it is slowed to
    /// <see cref="WorldDials.sludgeSpeedMultiplier"/> of its normal speed — see <see cref="MapGeometry.SlowZones"/>.</summary>
    [Serializable]
    public sealed class WorldSludge
    {
        public string id;
        public float x;
        public float z;
        public float w;
        public float d;
    }

    /// <summary>A walkable deck rect (MV-692) — same area-local MIN-corner convention as
    /// <see cref="WorldSludge"/>. <see cref="height"/> is the elevation of its walkable top surface in
    /// metres; 0 (unauthored) falls back to <see cref="WorldDials.deckHeight"/>.</summary>
    [Serializable]
    public sealed class WorldDeck
    {
        public string id;
        public float x;
        public float z;
        public float w;
        public float d;
        public float height;
    }

    /// <summary>A ramp rect joining the floor to an adjacent deck cell (MV-692) — same area-local
    /// MIN-corner convention as <see cref="WorldSludge"/>. Carries no orientation of its own: it must
    /// touch exactly one edge of exactly one authored <see cref="WorldDeck"/> rect, and the loader
    /// derives which way it climbs from that adjacency (<see cref="WorldMapLoader"/>).</summary>
    [Serializable]
    public sealed class WorldRamp
    {
        public string id;
        public float x;
        public float z;
        public float w;
        public float d;
    }

    /// <summary>A locked deck-cell opening (MV-697) — same area-local MIN-corner convention as
    /// <see cref="WorldSludge"/>, authored on the same <see cref="WorldArea"/> record as the
    /// <see cref="WorldDeck"/> it sits on. Built as an <see cref="AreaGate"/> lying flat at the deck's
    /// height rather than upright in a wall. <see cref="opensWith"/> carries the same
    /// <see cref="GateCondition"/> syntax a wall <see cref="WorldGate"/> does and MV-703's
    /// <see cref="MapValidation"/> rules validate it the same way — but unlike a wall gate, nothing yet
    /// wires a built hatch into <see cref="WorldRunner"/>'s condition-gate lock/unlock loop, so a hatch
    /// still behaves as if it read <c>"primary"</c> at runtime: breakable by sustained primary fire like
    /// an ordinary gate, whatever condition is actually authored here. That runtime wiring is a
    /// follow-up, not this ticket's.</summary>
    [Serializable]
    public sealed class WorldHatch
    {
        public string id;
        public float x;
        public float z;
        public float w;
        public float d;
        public string opensWith = "primary";
    }

    /// <summary>A Replicator's authored position and doubling budget (MV-706) — World 2's factory:
    /// a box that never spawns on its own, but doubles any robot that reaches its hatch. <see cref="x"/>/
    /// <see cref="z"/> are area-local metres (box centre), same convention as <see cref="WorldShed"/>.
    /// <see cref="capacity"/> is the maximum number of doublings this one box can ever perform — Lee's
    /// exact-count rule: the worst case per area is the authored composition plus Σ every replicator's
    /// capacity, by design, never a dial retuned live.</summary>
    [Serializable]
    public sealed class WorldReplicator
    {
        public string id;
        public float x;
        public float z;
        public int capacity;
    }

    /// <summary>One authored obstacle in an area — shrubbery, a hedge row, a planter (MV-318). Carries
    /// the same fields as <see cref="MapEntity"/>'s cover shape so <see cref="WorldMapLoader"/> can
    /// hand it straight to the engine that already knows how to build, validate and dress cover
    /// (<see cref="MapRuntime.BuildCover"/>, <see cref="MapValidation"/>, <see cref="BackyardDressing"/>'s
    /// hedge case) — an area's shrubbery is nothing new to that pipeline, only a new source feeding it.</summary>
    [Serializable]
    public sealed class WorldCover
    {
        public string id;
        public float x;
        public float z;
        public float width = 1f;
        public float height = 1f;
        public float depth = 1f;
        public string shape = "box";
        public string dressing = "none";
    }

    /// <summary>One area of a world map: a 2D rectangle at an arbitrary origin — NOT constrained to a
    /// shared centre-line the way the old corridor engine's rooms were (MV-267). <see cref="origin"/>
    /// is the rectangle's MIN corner (matches how the design board's <c>world1_config.json</c> is
    /// authored), unlike <see cref="MapZone"/> which is authored by its centre.</summary>
    [Serializable]
    public sealed class WorldArea
    {
        public string id;
        public int index;
        public string name;

        /// <summary>What the area is for — free text, but "entry", "boss" and "exit" are read as
        /// substrings so "boss+exit" (the compost clearing, which is both) says both at once without
        /// a combinable-flags field.</summary>
        public string role = "normal";

        public WorldAreaOrigin origin;
        public WorldAreaSize size;

        // Origination fields (garrison/sheds/threat budget) — parsed since MV-267 so a world config
        // that carries them round-trips without loss; read for real by the origination engine
        // (MV-269): hasShed drives SupplyLineNetwork, garrisonDensity drives Garrison.SeedCount.
        public bool hasShed;
        public string garrisonDensity;

        // World-content fields (MV-270): where a shed area's factory body actually stands. Optional —
        // most areas carry none.
        public float targetThreatBudget;
        public string notes;
        public WorldShed shed;

        /// <summary>Multiple sheds in one area (MV-475) — world 1's level design places up to 7 in a
        /// single area, which the single <see cref="shed"/> field cannot express. Not read directly by
        /// anything; call <see cref="Sheds"/> instead, which resolves this against the legacy
        /// <see cref="shed"/> field so callers never branch on which one a config authored.</summary>
        public WorldShed[] sheds;

        /// <summary>Replicators authored into this area (MV-706) — World 2's factory in place of a
        /// shed. Optional; most areas (and every World 1 area) carry none. Unlike <see cref="Sheds"/>
        /// there is no legacy single-field fallback to resolve — replicators shipped only after the
        /// array form already existed.</summary>
        public WorldReplicator[] replicators = Array.Empty<WorldReplicator>();

        public WorldBoss boss;

        /// <summary>Multiple bosses in one area (MV-561) — world 1 v4 puts two in a20 and three in a30,
        /// which the single <see cref="boss"/> field cannot express. Not read directly by anything; call
        /// <see cref="Bosses"/> instead, which resolves this against the legacy <see cref="boss"/> field
        /// so callers never branch on which one a config authored — same shape as <see cref="sheds"/>
        /// vs <see cref="shed"/> (MV-475).</summary>
        public WorldBoss[] bosses;

        /// <summary>Shrubbery/hedge rows authored into this area (MV-318) — obstacles a robot or Max
        /// must go around, not through, but never enough of them to seal a path (the ordinary Cover
        /// invariants in <see cref="MapValidation"/> enforce that, same as they always have). Optional
        /// — most areas carry none until authored.</summary>
        public WorldCover[] cover = Array.Empty<WorldCover>();

        /// <summary>This area's authored enemy composition (MV-365) — null means "not authored yet,
        /// fall back to the dial-derived budget solve" (<see cref="WorldConfig.SolveComposition"/>).
        /// Most Phase B areas carry one; nothing requires every area to.</summary>
        public WorldComposition composition;

        /// <summary>Authored, exact garrison placements (MV-559) — when non-empty,
        /// <see cref="MaxWorlds.Enemies.Garrison.SeedPositions"/> places these first, in authored order,
        /// each at its authored <c>kind</c>, and only fills any remaining seed slots from the ring.
        /// Optional; most areas carry none and behave exactly as before.</summary>
        public WorldGarrisonEntry[] garrison = Array.Empty<WorldGarrisonEntry>();

        /// <summary>Sludge slow-zones authored into this area (MV-692), area-local rects. Optional —
        /// most areas carry none.</summary>
        public WorldSludge[] sludge = Array.Empty<WorldSludge>();

        /// <summary>Walkable decks authored into this area (MV-692), area-local rects. Optional — most
        /// areas carry none.</summary>
        public WorldDeck[] decks = Array.Empty<WorldDeck>();

        /// <summary>Ramps joining the floor to a deck cell (MV-692), area-local rects. Optional — most
        /// areas carry none.</summary>
        public WorldRamp[] ramps = Array.Empty<WorldRamp>();

        /// <summary>Locked deck-cell openings (MV-697), area-local rects — see <see cref="WorldHatch"/>.
        /// Optional — most areas carry none.</summary>
        public WorldHatch[] hatches = Array.Empty<WorldHatch>();

        /// <summary>Elevation tier (MV-697): 0 is the floor, 1 is a deck. Most areas never author this —
        /// a plain floor room stays 0. An area authored with <see cref="overlays"/> set is always the
        /// deck side of the pair and carries 1.</summary>
        public int level;

        /// <summary>The id of the area THIS one is a vertical overlay of (MV-697) — a second visit to a
        /// footprint World 2 already built once, on the gantries over it (e.g. Junction Hall's floor
        /// area re-entered later, at deck height, as its own area record). An overlay area shares its
        /// target's <see cref="origin"/>/<see cref="size"/> by definition:
        /// <see cref="WorldMapLoader.TryLoad"/> copies them from the target once
        /// <see cref="MapValidation"/> has proven any authored value here agrees, and it builds neither a
        /// second floor/fence nor a second sludge/cover pass for it — only this area's OWN
        /// <see cref="decks"/>/<see cref="ramps"/>/<see cref="hatches"/>/<see cref="garrison"/>/
        /// <see cref="composition"/> and its own gates. Null (the common case) means an ordinary,
        /// independent area.</summary>
        public string overlays;

        /// <summary>A named encounter shape for this area (MV-365) — data only, read by
        /// <see cref="MaxWorlds.Enemies.AreaAccumulationDirector"/> to bias WHERE within the room a
        /// particular kind spawns (composition alone only says how many; some scenarios need
        /// placement too). Currently understood: <c>"centerDenial"</c> — Launcher-kind spawns bias
        /// toward the room's centre instead of the usual far-side-from-door band, so its missile
        /// barrage reads as "denied ground in the middle" rather than another far-wall cluster.
        /// Empty/unrecognised values fall back to ordinary placement.</summary>
        public string scenario = "";

        public float XMin => origin?.x ?? 0f;
        public float XMax => XMin + (size?.w ?? 0f);
        public float ZMin => origin?.z ?? 0f;
        public float ZMax => ZMin + (size?.d ?? 0f);

        public Vector2 CenterXz => new Vector2((XMin + XMax) * 0.5f, (ZMin + ZMax) * 0.5f);
        public Rect Footprint => new Rect(XMin, ZMin, XMax - XMin, ZMax - ZMin);

        /// <summary>This area's sheds, resolved (MV-475): <see cref="sheds"/> when authored, else the
        /// legacy single <see cref="shed"/> wrapped in a one-element array, else none. Gated on
        /// <see cref="hasShed"/> exactly as every call site already gated itself before this
        /// existed — <c>shed</c> alone is not a reliable "was this authored" signal, since JsonUtility
        /// materialises a non-null default for it on every area the moment ANY area in the config's
        /// array authors one (same quirk documented on <see cref="WorldComposition.IsAuthored"/>).
        /// The one place that branches, so no caller has to.</summary>
        public WorldShed[] Sheds()
        {
            if (!hasShed) return Array.Empty<WorldShed>();
            if (sheds != null && sheds.Length > 0) return sheds;
            return shed != null ? new[] { shed } : Array.Empty<WorldShed>();
        }

        /// <summary>The stable, unique entity id for the shed at <paramref name="index"/> of
        /// <paramref name="count"/> total sheds in this area (MV-475): <c>"{id}_shed"</c> when there is
        /// exactly one — so a legacy single-shed config's entity id, and every save/test that names it,
        /// is unchanged — or <c>"{id}_shed{n}"</c> (1-based) when there is more than one. Every caller
        /// that builds or resolves a shed entity (<see cref="WorldMapLoader"/>, <see cref="WorldRunner"/>,
        /// <see cref="MaxWorlds.Enemies.SupplyLineNetwork"/>) uses this same rule so their ids never
        /// disagree.</summary>
        public string ShedId(int index, int count) => count <= 1 ? $"{id}_shed" : $"{id}_shed{index + 1}";

        /// <summary>This area's bosses, resolved (MV-561): <see cref="bosses"/> when authored, else the
        /// legacy single <see cref="boss"/> wrapped in a one-element array, else none. <see cref="boss"/>
        /// is a nested <c>[Serializable]</c> field, so JsonUtility default-constructs it even when the
        /// JSON never authors a "boss" object at all (the same quirk <see cref="WorldComposition.IsAuthored"/>
        /// documents) — an unauthored one reads back as (0,0), not null, so it is only treated as real
        /// once it carries a non-origin position. The one place that branches, so
        /// <see cref="WorldMapLoader"/> doesn't have to.</summary>
        public WorldBoss[] Bosses()
        {
            if (bosses != null && bosses.Length > 0) return bosses;
            return boss != null && (boss.x != 0f || boss.z != 0f) ? new[] { boss } : Array.Empty<WorldBoss>();
        }

        /// <summary>The entity id for the boss at <paramref name="index"/> of <paramref name="count"/>
        /// resolved bosses in this area (MV-561): <paramref name="b"/>'s own authored <see cref="WorldBoss.id"/>
        /// when it has one — so <c>world1_config.json</c>'s "big_bermuda" keeps meaning exactly what it
        /// always has, unchanged — or <c>"{id}_boss"</c> (single) / <c>"{id}_boss{n}"</c> (multiple),
        /// 1-based, when it doesn't. Unlike <see cref="ShedId"/>, a boss's authored id is real content
        /// (a fight needs a name), not an authoring-only reference, so it takes priority over the
        /// fallback rather than being ignored by it.</summary>
        public string BossId(WorldBoss b, int index, int count) =>
            !string.IsNullOrEmpty(b?.id) ? b.id : (count <= 1 ? $"{id}_boss" : $"{id}_boss{index + 1}");

        public bool IsEntryRole => HasRole("entry");
        public bool IsBossRole => HasRole("boss");
        public bool IsExitRole => HasRole("exit");

        /// <summary>A crossing area rather than a fight room (MV-692, e.g. a gantry over a sludge
        /// channel) — <see cref="MapValidation.MinFightRoomWidth"/> is not applied to one.</summary>
        public bool IsBridgeRole => HasRole("bridge");

        private bool HasRole(string token) =>
            role != null && role.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>An area-local <c>{x,z,w,d}</c> rect (MV-692: <see cref="WorldSludge"/>,
        /// <see cref="WorldDeck"/>, <see cref="WorldRamp"/>) resolved to a world-space footprint — same
        /// MIN-corner convention <see cref="origin"/>/<see cref="size"/> already use.</summary>
        public Rect WorldRectOf(float localX, float localZ, float w, float d) =>
            new Rect(XMin + localX, ZMin + localZ, w, d);

        /// <summary>The fixed coordinate a wall sits on — every point on the N/S wall shares a Z, every
        /// point on the E/W wall shares an X.</summary>
        public float WallCoord(Wall wall) => wall switch
        {
            Wall.N => ZMax,
            Wall.S => ZMin,
            Wall.E => XMax,
            Wall.W => XMin,
            _ => 0f,
        };

        /// <summary>The span a wall runs along — X for the N/S walls, Z for the E/W walls. A gate's
        /// <c>pos</c> (0..1) is a fraction of this.</summary>
        public Span WallSpan(Wall wall) =>
            (wall == Wall.N || wall == Wall.S) ? new Span(XMin, XMax) : new Span(ZMin, ZMax);

        /// <summary>True if this wall's line is a constant Z (so the span it runs along is X).</summary>
        public bool WallRunsAlongX(Wall wall) => wall == Wall.N || wall == Wall.S;
    }

    /// <summary>One end of a gate: which area, which of its four walls, and how far along that wall
    /// (0 = the wall's first corner, 1 = its last).</summary>
    [Serializable]
    public sealed class WorldGateEndpoint
    {
        public string area;
        public string wall;
        public float pos;
    }

    /// <summary>A doorway between two areas' walls, placed at a fraction along each rather than forced
    /// onto a shared centre-line (MV-267). <see cref="opensWith"/> parses into a
    /// <see cref="GateCondition"/> (MV-703, validated by <see cref="MapValidation"/>) — Start/Primary/
    /// Sluice gates open by their own HP alone (<see cref="AreaGate"/>); the shed/replicator conditions
    /// hold the gate <see cref="AreaGate.Locked"/> until <see cref="WorldRunner.RefreshGateLocks"/>
    /// resolves them true.</summary>
    [Serializable]
    public sealed class WorldGate
    {
        public string id;
        public WorldGateEndpoint from;
        public WorldGateEndpoint to;
        public float width;
        public string opensWith = "start";
    }

    /// <summary>The <c>band</c> dial — how far the fun ratio R = MPL÷EPL is allowed to swing in Max's
    /// favour (<see cref="up"/>) or against him (<see cref="down"/>), Confluence MVW 34439170 §4/§8.4.
    /// Carried for calibration reference; the band's actual enforcement is
    /// <see cref="MaxWorlds.Enemies.PowerScoring.BandLow"/>/<c>BandHigh</c>, which this ticket does not
    /// derive from these two numbers automatically — that mapping is a playtest-tuning question for
    /// ticket 4/MV-270, not this ticket's engine.</summary>
    [Serializable]
    public sealed class WorldBand
    {
        public float up;
        public float down;
    }

    /// <summary>The <c>toughnessCurve</c> sub-dial (Confluence MVW 34439170 §8.6). Field names match
    /// the locked <c>world1_config.json</c> exactly — not <see cref="MaxWorlds.Enemies.ToughnessCurve"/>'s
    /// own field names — so the config round-trips without loss; <see cref="ToEngineCurve"/> bridges to
    /// the engine's own model. <see cref="toughSubstitutionPct"/> is carried losslessly but not read by
    /// this ticket's engine — <see cref="tankShareEnd"/> is what actually drives
    /// <see cref="MaxWorlds.Enemies.DifficultyEngine.SolveComposition"/> here.</summary>
    [Serializable]
    public sealed class WorldToughnessCurve
    {
        public int heavyFromArea = 5;
        public int bruteFromArea = 8;
        public float toughSubstitutionPct;
        public float tankShareEnd = 0.70f;

        // MV-293's ranged/teleport kinds (MV-310) — same intro-area idiom as heavy/bruteFromArea
        // above, carried losslessly through the JSON round-trip.
        public int gunnerFromArea = 2;
        public int launcherFromArea = 3;
        public int blinkerFromArea = 4;
        public float specialSharePct = 12f;

        /// <summary>Bridges to the engine's own linear-drift model (MV-268). Tank share starts at 0 at
        /// <see cref="heavyFromArea"/> (nothing tanky before then, matching the engine class's own
        /// default) and drifts to <see cref="tankShareEnd"/> by <paramref name="lastArea"/>. Reads the
        /// Settings panel's World-dial overrides (<see cref="DevTuning.WorldHeavyFromArea"/> etc.) the
        /// same <c>Or()</c> idiom as every other live-tunable number in the game.</summary>
        public ToughnessCurve ToEngineCurve(int lastArea) => new ToughnessCurve
        {
            heavyFromArea = Mathf.RoundToInt(DevTuning.Or(DevTuning.WorldHeavyFromArea, heavyFromArea)),
            bruteFromArea = Mathf.RoundToInt(DevTuning.Or(DevTuning.WorldBruteFromArea, bruteFromArea)),
            tankShareAtHeavyIntro = 0f,
            tankShareAtEnd = DevTuning.Or(DevTuning.WorldTankShareEnd, tankShareEnd),
            lastArea = Mathf.Max(1, lastArea),
            gunnerFromArea = gunnerFromArea,
            launcherFromArea = launcherFromArea,
            blinkerFromArea = blinkerFromArea,
            specialSharePct = specialSharePct,
        };
    }

    /// <summary>The ~8 designer dials (Confluence MVW 34439170 §8, MV-269): everything a world tunes
    /// by hand, with per-area budgets/composition/pacing all derived from these by the engine (MV-268)
    /// rather than authored per-area.</summary>
    [Serializable]
    public sealed class WorldDials
    {
        public int areaCount;
        public float baseThreat;
        public float threatGrowth;
        public WorldBand band;
        public float[] pacingRhythm = Array.Empty<float>();
        public WorldToughnessCurve toughnessCurve;
        public int powerupCadence;

        /// <summary>Speed a mover is scaled to while its feet are inside a <see cref="WorldSludge"/>
        /// rect (MV-692). 0 in the JSON means "not authored" would read as "sludge stops you dead",
        /// which is never intended — so this carries the design default directly rather than through
        /// <see cref="WorldMapLoader"/>'s usual "0 means fall back" idiom.</summary>
        public float sludgeSpeedMultiplier = 0.6f;

        /// <summary>Elevation, in metres, a <see cref="WorldDeck"/> builds at when it doesn't author its
        /// own <see cref="WorldDeck.height"/> (MV-692).</summary>
        public float deckHeight = 2.5f;

        public PacingRhythm EnginePacing =>
            new PacingRhythm(pacingRhythm != null && pacingRhythm.Length > 0 ? pacingRhythm : new[] { 1f });
    }

    /// <summary>One robot archetype's Threat Value (Confluence MVW 34439170 §4). The JSON's
    /// <c>enemyTypes</c> object is keyed by a fixed, closed set of archetype names matching
    /// <see cref="EnemyKind"/> (small/large/heavy/brute) — <c>JsonUtility</c> has no dictionary support,
    /// but since the set never grows without a new <see cref="EnemyKind"/> to match, four named fields
    /// round-trip it exactly as well as a map would.</summary>
    [Serializable]
    public sealed class WorldEnemyTypeEntry
    {
        public float thv;
    }

    /// <summary>The <c>enemyTypes</c> THV table (Confluence MVW 34439170 §4, MV-269) — a world's own
    /// per-archetype Threat Values, read instead of <see cref="ThreatValues"/>'s flat placeholders once
    /// a world config supplies them.</summary>
    [Serializable]
    public sealed class WorldEnemyTypes
    {
        public WorldEnemyTypeEntry small;
        public WorldEnemyTypeEntry large;
        public WorldEnemyTypeEntry heavy;
        public WorldEnemyTypeEntry brute;

        // MV-293's ranged/teleport kinds (MV-310) — optional in the JSON; a world authored before
        // these existed simply omits them, and Thv() below falls back to the engine's flat
        // ThreatValues placeholder rather than reading a missing 0.
        public WorldEnemyTypeEntry gunner;
        public WorldEnemyTypeEntry launcher;
        public WorldEnemyTypeEntry blinker;

        // MV-539: optional in the JSON, same "a world authored before this existed simply omits it"
        // fallback as gunner/launcher/blinker above.
        public WorldEnemyTypeEntry bolter;

        public float Thv(EnemyKind kind)
        {
            WorldEnemyTypeEntry e = kind switch
            {
                EnemyKind.Bruiser => large,
                EnemyKind.Heavy => heavy,
                EnemyKind.Brute => brute,
                EnemyKind.Gunner => gunner,
                EnemyKind.Launcher => launcher,
                EnemyKind.Blinker => blinker,
                EnemyKind.Bolter => bolter,
                _ => small,
            };
            return e != null ? e.thv : ThreatValues.Of(kind);
        }

        /// <summary>Σ THV of a solved area composition, weighted by THIS world's own table rather than
        /// the engine's flat placeholders — what <see cref="WorldConfig.SigmaThreatValue"/> and, through
        /// it, EPL (<see cref="MaxWorlds.Enemies.PowerScoring"/>) actually spend.</summary>
        public float WeightedThv(DifficultyEngine.Composition c) =>
            c.Rusher * Thv(EnemyKind.Rusher) + c.Bruiser * Thv(EnemyKind.Bruiser) +
            c.Heavy * Thv(EnemyKind.Heavy) + c.Brute * Thv(EnemyKind.Brute) +
            c.Gunner * Thv(EnemyKind.Gunner) + c.Launcher * Thv(EnemyKind.Launcher) +
            c.Blinker * Thv(EnemyKind.Blinker) + c.Bolter * Thv(EnemyKind.Bolter);
    }

    /// <summary>A whole world's map, in the 2D-area-placement schema (MV-267, Confluence MVW 34439170
    /// §7-8), plus the 8 designer dials and per-type threat values a world tunes (MV-269, §4/§8). The
    /// schema is <c>MaxVsTheWorlds/world-config@0.6-draft</c> — <c>$schema</c> and <c>revision</c> are
    /// free-text provenance, not read by any engine, so JsonUtility (which cannot bind a field named
    /// <c>$schema</c>) simply ignores the former; <see cref="revision"/> is carried because it IS a
    /// valid identifier.</summary>
    [Serializable]
    public sealed class WorldConfig
    {
        public string world = "Untitled World";
        public string revision;

        /// <summary>Height, in metres, of the arena's walls/fences (MV-277). 0 (the JSON default when
        /// the field is omitted) means "not authored" — <see cref="WorldMapLoader.TryLoad"/> falls back
        /// to <see cref="MapData.DefaultWallHeight"/> rather than building unwalkable zero-height walls.</summary>
        public float wallHeight;

        public WorldArea[] areas = Array.Empty<WorldArea>();
        public WorldGate[] gates = Array.Empty<WorldGate>();

        public WorldDials dials;
        public WorldEnemyTypes enemyTypes;

        /// <summary>Per-world restat/reskin overrides (MV-701) — see <see cref="WorldEnemyOverride"/>.
        /// Empty for every world that doesn't need one (World 1's whole roster is the base table).</summary>
        public WorldEnemyOverride[] enemyOverrides = Array.Empty<WorldEnemyOverride>();

        /// <summary>This world's override for <paramref name="kind"/>, or null if it authors none —
        /// what <see cref="MaxWorlds.Enemies.EnemyArchetype.For"/> applies over the base table.</summary>
        public WorldEnemyOverride EnemyOverrideFor(EnemyKind kind)
        {
            if (enemyOverrides == null) return null;
            foreach (WorldEnemyOverride ov in enemyOverrides)
            {
                if (ov == null || string.IsNullOrEmpty(ov.kind)) continue;
                if (EnemyKindNames.TryParse(ov.kind, out EnemyKind k) && k == kind) return ov;
            }
            return null;
        }

        public WorldArea Area(string areaId)
        {
            if (areas == null || string.IsNullOrEmpty(areaId)) return null;
            foreach (WorldArea a in areas)
                if (a != null && a.id == areaId) return a;
            return null;
        }

        /// <summary>The combat area at the dials' 1-based <paramref name="index"/> (matches
        /// <c>WorldArea.index</c> for a1..aN — distinct from the entry stub at 0 and the boss room at
        /// N+1, neither of which carry a threat budget).</summary>
        public WorldArea AreaByIndex(int index)
        {
            if (areas == null) return null;
            foreach (WorldArea a in areas)
                if (a != null && a.index == index) return a;
            return null;
        }

        /// <summary>This world's solved enemy composition for combat area <paramref name="areaIndex"/>
        /// (MV-268's budget solver, driven by THIS world's own <see cref="dials"/>) — the single source
        /// both <see cref="MaxWorlds.Enemies.Garrison"/> and <see cref="MaxWorlds.Enemies.PowerScoring"/>
        /// solve against, so garrison seeding and EPL scoring can never quietly disagree about how many
        /// enemies area N has. Zero areas outside <c>[1, dials.areaCount]</c> — there is no budget for
        /// the entry stub, the boss room, or anywhere past the world's own end.</summary>
        public DifficultyEngine.Composition SolveComposition(int areaIndex)
        {
            if (dials == null || areaIndex < 1 || areaIndex > dials.areaCount) return default;

            // MV-365: an authored composition IS the answer for this area — it replaces the
            // dial-derived budget solve below entirely rather than being blended with it, so an
            // authored arena reads exactly as designed instead of as an approximation of a budget.
            WorldComposition authored = AreaByIndex(areaIndex)?.composition;
            if (authored != null && authored.IsAuthored) return authored.ToEngineComposition();

            float baseThreat = DevTuning.Or(DevTuning.WorldBaseThreat, dials.baseThreat);
            float threatGrowth = DevTuning.Or(DevTuning.WorldThreatGrowth, dials.threatGrowth);
            float budget = DifficultyEngine.TargetBudget(areaIndex, baseThreat, threatGrowth, dials.EnginePacing);
            ToughnessCurve toughness = dials.toughnessCurve?.ToEngineCurve(dials.areaCount);
            return DifficultyEngine.SolveComposition(areaIndex, budget, toughness);
        }

        /// <summary>Σ THV for combat area <paramref name="areaIndex"/>, weighted by this world's own
        /// <see cref="enemyTypes"/> table — the per-area term EPL sums (Confluence MVW 34439170 §4).</summary>
        public float SigmaThreatValue(int areaIndex) => enemyTypes?.WeightedThv(SolveComposition(areaIndex)) ?? 0f;
    }
}
