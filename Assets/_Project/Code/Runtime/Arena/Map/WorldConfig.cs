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
    /// <c>MowerHutch</c> that makes <see cref="WorldArea.hasShed"/> real.</summary>
    [Serializable]
    public sealed class WorldShed
    {
        public float x;
        public float z;
        public string produces;
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
        public WorldBoss boss;

        /// <summary>Shrubbery/hedge rows authored into this area (MV-318) — obstacles a robot or Max
        /// must go around, not through, but never enough of them to seal a path (the ordinary Cover
        /// invariants in <see cref="MapValidation"/> enforce that, same as they always have). Optional
        /// — most areas carry none until authored.</summary>
        public WorldCover[] cover = Array.Empty<WorldCover>();

        public float XMin => origin?.x ?? 0f;
        public float XMax => XMin + (size?.w ?? 0f);
        public float ZMin => origin?.z ?? 0f;
        public float ZMax => ZMin + (size?.d ?? 0f);

        public Vector2 CenterXz => new Vector2((XMin + XMax) * 0.5f, (ZMin + ZMax) * 0.5f);
        public Rect Footprint => new Rect(XMin, ZMin, XMax - XMin, ZMax - ZMin);

        public bool IsEntryRole => HasRole("entry");
        public bool IsBossRole => HasRole("boss");
        public bool IsExitRole => HasRole("exit");

        private bool HasRole(string token) =>
            role != null && role.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

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
    /// onto a shared centre-line (MV-267). <see cref="opensWith"/> is data the difficulty/origination
    /// engines (MV-268/MV-269) act on — this ticket only carries and validates the word, it does not
    /// wire any gameplay behind it (gates still open by their own HP, <see cref="AreaGate"/>).</summary>
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
        public int bomberFromArea = 3;
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
            bomberFromArea = bomberFromArea,
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
        public WorldEnemyTypeEntry bomber;
        public WorldEnemyTypeEntry blinker;

        public float Thv(EnemyKind kind)
        {
            WorldEnemyTypeEntry e = kind switch
            {
                EnemyKind.Bruiser => large,
                EnemyKind.Heavy => heavy,
                EnemyKind.Brute => brute,
                EnemyKind.Gunner => gunner,
                EnemyKind.Bomber => bomber,
                EnemyKind.Blinker => blinker,
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
            c.Gunner * Thv(EnemyKind.Gunner) + c.Bomber * Thv(EnemyKind.Bomber) +
            c.Blinker * Thv(EnemyKind.Blinker);
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
