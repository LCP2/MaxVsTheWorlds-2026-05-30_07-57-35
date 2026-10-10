using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Audio;
using MaxWorlds.Core;
using MaxWorlds.Pickups;
using MaxWorlds.Rendering;
using MaxWorlds.Weapons;

namespace MaxWorlds.Arena
{
    /// <summary>Stable per-world names (MV-1141) that never change when worlds are reordered or a new
    /// one is inserted between two existing ones — unlike a 0-based world index, which shifts the moment
    /// the City lands between World 2 and today's World 3.</summary>
    public static class WorldIds
    {
        public const string Backyard = "backyard";
        public const string Stormdrain = "stormdrain";
        public const string Reef = "reef";
    }

    /// <summary>Which dressing pass <c>BackyardPath.ApplyWorldMaterials</c> runs for a world's own kit
    /// (MV-1141) — today's three cosmetic passes, named instead of picked by comparing a world index.</summary>
    public enum WorldKit
    {
        Garden,
        Stormdrain,
        Reef,
    }

    /// <summary>Which gate skin a world's own doors wear (MV-1141) — read by both
    /// <c>BackyardPath</c>'s own gate dressing and <c>WorldJoinSequence.ApplyDestinationSkin</c>'s
    /// corridor-arrival door, so the two sites agree by construction rather than by two separately
    /// maintained world checks.</summary>
    public enum WorldGateSkin
    {
        None,
        Stormdrain,
        Reef,
    }

    /// <summary>A world's Sentinel look (MV-1141) — moved here from <c>Sentinel</c>'s own private
    /// <c>Styles</c> table, which used to be the one other per-world row living outside this catalog.</summary>
    public readonly struct SentinelStyle
    {
        public readonly Color Body;
        public readonly Color Accent;
        public readonly Color Eye;
        public readonly float EmissionFactor;
        public readonly Color BoltColor;
        public readonly float BoltThicknessScale;

        public SentinelStyle(Color body, Color accent, Color eye, float emissionFactor,
            Color boltColor, float boltThicknessScale)
        {
            Body = body;
            Accent = accent;
            Eye = eye;
            EmissionFactor = emissionFactor;
            BoltColor = boltColor;
            BoltThicknessScale = boltThicknessScale;
        }
    }

    /// <summary>Everything that makes a world look, light, dress, arm its sentinels and sound like
    /// itself (MV-1141) — one row per world, in play order, read by every system that used to decide the
    /// same thing from a raw world number compared in place.</summary>
    public sealed class WorldDefinition
    {
        /// <summary>Stable, order-independent name — see <see cref="WorldIds"/>.</summary>
        public string Id;

        /// <summary>The <c>Resources/Worlds/*.json</c> key — see <see cref="WorldLibrary"/>.</summary>
        public string ConfigKey;

        public BiomePalette Palette;
        public BackyardLook Look;

        /// <summary>Whether this world's own skybox should be removed — today true for a sealed
        /// interior (Stormdrain, Reef), false for the Backyard's open sky. Derived from
        /// <see cref="Palette"/> rather than independently authored: <see cref="BackyardLighting"/>
        /// (Rendering) can only ever read <see cref="Palette"/> itself off the active biome, never this
        /// catalog (Arena/Gameplay), so the one place this fact can live without drifting out of sync
        /// is the palette's own data field — see <see cref="BiomePalette.RemovesSkybox"/>. That field
        /// is excluded from <see cref="BiomePalette"/>'s own <c>Equals</c> (see its doc) specifically so
        /// adding it never breaks a pre-existing "unchanged field-for-field" test snapshot.</summary>
        public bool RemovesSkybox => Palette.RemovesSkybox;

        public WorldKit Kit;
        public WorldGateSkin GateSkin;
        public SentinelStyle Style;

        /// <summary>True only for World 1 — see <c>Sentinel.FireBeam</c>.</summary>
        public bool FiresWaterBeam;

        public MusicWorld Music;

        /// <summary>How far a pickup's ground ring alpha is scaled back on this world's own floor — see
        /// <c>PickupArtDirector.ShowRing</c>. 1 = no scaling.</summary>
        public float RingAlphaScale;

        // --- MV-1142: weapons, RIG board and economy ---

        /// <summary>The <c>Resources</c> path for this world's own RIG board — see
        /// <see cref="MaxWorlds.Weapons.RigBoardLibrary.ForWorld"/>.</summary>
        public string RigBoardResourcePath;

        /// <summary>Which PRIMARY weapon this world's Weapon Core morph grants — see
        /// <see cref="MaxWorlds.Weapons.WeaponSystemState.ApplyWorldLoadout"/>.</summary>
        public WeaponCatalog.PrimaryKind PrimaryWeapon;

        /// <summary>Which SECONDARY weapon this world's Weapon Core morph grants.</summary>
        public SecondaryKind SecondaryWeapon;

        /// <summary>Whether SECONDARY's levels and unlock carry in from the world played before this
        /// one, instead of resetting to a fresh locked tree — true only for a world that keeps the
        /// previous world's SECONDARY ids on its own board (today, the Reef keeps World 2's Shoulder
        /// Rack). See <see cref="MaxWorlds.Weapons.WeaponSystemState.ApplyWeaponCoreMorph"/>.</summary>
        public bool CarrySecondaryAcrossMorph;

        /// <summary>Whether SECONDARY arrives mystery-locked (<c>RigState.ActivateSecondaryMystery</c>)
        /// the moment the Weapon Core morph lands in this world — false wherever
        /// <see cref="CarrySecondaryAcrossMorph"/> is true, since there is nothing left to
        /// mystery-lock.</summary>
        public bool SecondaryMysteryLockedOnMorph;

        /// <summary>Whether this world's PRIMARY arrives with its SPLIT node (<c>p_spr</c>) already
        /// owned at level 1 the instant the Weapon Core morph grants it (MV-1128) — true only for a
        /// world whose PRIMARY is already a multi-beam weapon (today, the Reef's UNDERTOW).</summary>
        public bool PrimarySplitSeeded;

        /// <summary>Whether FORGE fusions are playable in this world — see
        /// <see cref="MaxWorlds.Weapons.RigFusionState.EnabledInWorld"/>.</summary>
        public bool FusionsEnabled;

        /// <summary>The cell-cost multiplier applied to this world's own PRIMARY/SECONDARY nodes — see
        /// <see cref="MaxWorlds.Weapons.CellSpend"/>. ENERGY/MOVE/SUPPORT are never scaled by this.</summary>
        public float PrimarySecondaryCostMultiplier;

        /// <summary>MV-1165: this world's own across-the-board cell-cost scale, applied on top of
        /// <see cref="PrimarySecondaryCostMultiplier"/> to every node in every family (including
        /// <c>u_slt</c>'s flat price) — see <see cref="MaxWorlds.Weapons.CellSpend"/>. 1 = no change
        /// from the global ladder; the Stormdrain's row is the only one below 1 (Lee, 11 Oct: World 2's
        /// upgrades read too expensive).</summary>
        public float UpgradeCostScale;

        /// <summary>This world's per-area Parts budget multiplier — see
        /// <see cref="MaxWorlds.Pickups.CellEconomyTuning.WorldPartsMultiplier"/>. A delegate rather
        /// than a plain float because the Reef's row stays live-tunable via the Settings panel's "W3
        /// parts x" knob (<see cref="MaxWorlds.Core.DevTuning.World3PartsMultiplier"/>) for the life of
        /// the process, not just at <see cref="WorldCatalog.BuildShippedRows"/> time.</summary>
        public Func<float> PartsMultiplier;

        /// <summary>A Supercell is granted every this-many areas in this world — 1 means every area.
        /// See <see cref="MaxWorlds.Pickups.PickupDirector"/>'s own Supercell rule.</summary>
        public int SupercellCadenceAreas;

        /// <summary>Whether the Rack Module pickup drops in this world — see
        /// <see cref="MaxWorlds.Pickups.PickupDirector"/>'s own Rack Module rule.</summary>
        public bool RackModuleDropsHere;
    }

    /// <summary>The one table of worlds (MV-1141) — introduced so a fourth world (the City) can be
    /// inserted between today's World 2 and World 3 without silently inheriting the Reef's palette, kit,
    /// sentinels and music the way a raw <c>worldIndex &gt;= 2</c> comparison used to hand it.</summary>
    public static class WorldCatalog
    {
        // --- Sentinel style tones, moved here from Sentinel.cs's own private table (MV-1141) ---

        /// <summary>MV-580: the primary's own blue.</summary>
        private static readonly Color BodyColor = new Color(0.35f, 0.55f, 0.75f);

        /// <summary>MV-1004: World 2 ("Lee: make them red / dark red").</summary>
        private static readonly Color BodyColorWorld2 = new Color(0.847f, 0.149f, 0.110f);

        /// <summary>MV-580: a lighter, cooler tint of <see cref="BodyColor"/>.</summary>
        private static readonly Color BodyAccent = new Color(0.62f, 0.78f, 0.90f);

        /// <summary>MV-1004/MV-1024: World 2's own Accent slot, brightened to #FF5A43.</summary>
        private static readonly Color BodyAccentWorld2 = new Color(1.000f, 0.353f, 0.263f);

        /// <summary>MV-580/MV-806: matches <see cref="SentinelBolt.BoltColor"/> — the eye moves with
        /// whatever the turret actually fires.</summary>
        private static readonly Color EyeColor = SentinelBolt.BoltColor;

        /// <summary>MV-1004: a pale gold lens — a red eye disappears on World 2's own red body.</summary>
        private static readonly Color EyeColorWorld2 = new Color(1.0f, 0.86f, 0.62f);

        /// <summary>MV-1069: World 3's own body/accent/bolt. Hex #1FA84A/#6DFF8A/#4DFF6A.</summary>
        private static readonly Color BodyColorWorld3 = new Color(0.1216f, 0.6588f, 0.2902f);
        private static readonly Color BodyAccentWorld3 = new Color(0.4275f, 1.0f, 0.5412f);
        private static readonly Color BoltColorWorld3 = new Color(0.3020f, 1.0f, 0.4157f);

        /// <summary>MV-1069: pale gold again — a green eye vanishes on World 3's own green body. Hex
        /// #FFDB9E.</summary>
        private static readonly Color EyeColorWorld3 = new Color(1.0f, 0.8588f, 0.6196f);

        /// <summary>MV-782: how far a pickup's ground ring alpha is scaled back on the Stormdrain's dark
        /// floor, moved here from <c>PickupArtDirector</c>'s own constant.</summary>
        private const float StormdrainRingAlphaScale = 0.45f;

        /// <summary>MV-767: World 2+'s own PRIMARY/SECONDARY cell-cost multiplier, moved here from
        /// <c>CellSpend</c>'s own private constant (MV-1142) — both the Stormdrain's and the Reef's row
        /// price the same 2.5x.</summary>
        private const float PrimarySecondaryCostMultiplierWorld2Plus = 2.5f;

        /// <summary>MV-1165 (Lee, 11 Oct 2026, "the cost of all upgrades is too high. Just notch it down
        /// by 10 or 15%"): 13%, the middle of his range, applied as a 0.87x scale on every Stormdrain
        /// RIG price.</summary>
        private const float UpgradeCostScaleWorld2 = 0.87f;

        private static IReadOnlyList<WorldDefinition> s_rows = BuildShippedRows();

        /// <summary>How many worlds exist.</summary>
        public static int Count => s_rows.Count;

        /// <summary>The row for a 0-based world index. Clamped into range: below 0 gives the first row
        /// (every one of today's own expressions already does this with -1), past the end gives the
        /// last row (today a few of those same expressions instead fell back to the Reef/row-0 — see
        /// MV-1141's "Do not re-raise" — accepted, since no shipped path asks for an index past the
        /// end).</summary>
        public static WorldDefinition Get(int worldIndex)
        {
            int clamped = Mathf.Clamp(worldIndex, 0, s_rows.Count - 1);
            return s_rows[clamped];
        }

        /// <summary>Whether a resolved world index's own row fires the water beam (MV-1141/MV-914) —
        /// false for a negative index, which means "no <c>BackyardPath</c> resolved" (a bare EditMode
        /// fixture), not "World 1". <see cref="Get"/>'s own -1-clamps-to-row-0 rule exists for every
        /// OTHER field (palette/look/style all need a sane fallback for that fixture case), but must
        /// not silently turn "no world resolved" into World 1's water beam — the one pre-MV-1141
        /// behaviour <see cref="Sentinel.FireBeam"/> relied on an actual <c>worldIndex == 0</c> for.
        /// Lives here, not as a literal comparison at the call site, so AC2's own grep guard (no
        /// <c>worldIndex &gt;= 0</c>/<c>== 0</c> comparison left in <c>Sentinel.cs</c>) still holds.</summary>
        public static bool FiresWaterBeamAt(int worldIndex) => worldIndex >= 0 && Get(worldIndex).FiresWaterBeam;

        /// <summary>The row index for a stable <see cref="WorldIds"/> name, or -1 if there is no such
        /// world.</summary>
        public static int IndexOf(string id)
        {
            for (int i = 0; i < s_rows.Count; i++)
            {
                if (s_rows[i].Id == id) return i;
            }
            return -1;
        }

        /// <summary>The row whose palette matches whatever <see cref="MaterialLibrary.Palette"/> is
        /// currently active, falling back to the first row when nothing matches — the same "no match =
        /// World 1" default the deleted <c>BackyardLighting.WorldIndexFromPalette</c> used. For callers
        /// (Max's own rig, a missile, a shed turret) that have only the active palette to go on, never a
        /// world index.</summary>
        public static WorldDefinition ForActivePalette()
        {
            for (int i = 0; i < s_rows.Count; i++)
            {
                if (MaterialLibrary.Palette.Equals(s_rows[i].Palette)) return s_rows[i];
            }
            return s_rows[0];
        }

        /// <summary>Problems with the current rows: an empty/duplicate <see cref="WorldDefinition.Id"/>,
        /// an empty <see cref="WorldDefinition.ConfigKey"/> or one that does not load, or a
        /// struct-valued field left at its type's default (never a legitimate authored value for
        /// <see cref="WorldDefinition.Palette"/>/<see cref="WorldDefinition.Look"/>/
        /// <see cref="WorldDefinition.Style"/>/<see cref="WorldDefinition.RingAlphaScale"/>). Later
        /// tickets add their own fields' checks here.</summary>
        public static List<string> Validate()
        {
            var problems = new List<string>();
            var seenIds = new HashSet<string>();

            for (int i = 0; i < s_rows.Count; i++)
            {
                WorldDefinition row = s_rows[i];

                if (string.IsNullOrEmpty(row.Id)) problems.Add($"row {i}: empty Id");
                else if (!seenIds.Add(row.Id)) problems.Add($"row {i}: duplicate Id '{row.Id}'");

                if (string.IsNullOrEmpty(row.ConfigKey)) problems.Add($"row {i}: empty ConfigKey");
                else if (WorldLibrary.Load(row.ConfigKey) == null)
                    problems.Add($"row {i} ('{row.Id}'): ConfigKey '{row.ConfigKey}' did not load");

                if (row.Palette.Equals(default(BiomePalette))) problems.Add($"row {i} ('{row.Id}'): Palette not set");
                if (row.Look.Equals(default(BackyardLook))) problems.Add($"row {i} ('{row.Id}'): Look not set");
                if (row.Style.Equals(default(SentinelStyle))) problems.Add($"row {i} ('{row.Id}'): Style not set");
                if (row.RingAlphaScale <= 0f) problems.Add($"row {i} ('{row.Id}'): RingAlphaScale not set");

                // MV-1142
                if (string.IsNullOrEmpty(row.RigBoardResourcePath))
                    problems.Add($"row {i} ('{row.Id}'): RigBoardResourcePath empty");
                else if (Resources.Load<TextAsset>(row.RigBoardResourcePath) == null)
                    problems.Add($"row {i} ('{row.Id}'): RigBoardResourcePath '{row.RigBoardResourcePath}' did not load as a TextAsset");

                if (row.SupercellCadenceAreas < 1)
                    problems.Add($"row {i} ('{row.Id}'): SupercellCadenceAreas below 1");
            }

            return problems;
        }

        /// <summary>Test seam (MV-1141). No <c>InternalsVisibleTo</c> back to the EditMode test assembly
        /// exists in this project (see MV747NameplateGroupingTests' own note), so this is public rather
        /// than internal, as the ticket's own fallback asks for.</summary>
        public static void UseForTests(IReadOnlyList<WorldDefinition> rows) => s_rows = rows;

        /// <summary>Restores the shipped rows after <see cref="UseForTests"/>.</summary>
        public static void ResetForTests() => s_rows = BuildShippedRows();

        private static IReadOnlyList<WorldDefinition> BuildShippedRows() => new[]
        {
            new WorldDefinition
            {
                Id = WorldIds.Backyard,
                ConfigKey = WorldLibrary.World1,
                Palette = BiomePalette.Backyard,
                Look = BackyardLook.Default,
                Kit = WorldKit.Garden,
                GateSkin = WorldGateSkin.None,
                Style = new SentinelStyle(BodyColor, BodyAccent, EyeColor, emissionFactor: 0f,
                    boltColor: SentinelBolt.BoltColor, boltThicknessScale: 1f),
                FiresWaterBeam = true,
                Music = MusicWorld.Backyard,
                RingAlphaScale = 1f,
                RigBoardResourcePath = RigBoardLibrary.World1ResourcePath,
                PrimaryWeapon = WeaponCatalog.PrimaryKind.Rcda,
                SecondaryWeapon = SecondaryKind.WaterBalloon,
                CarrySecondaryAcrossMorph = false,
                SecondaryMysteryLockedOnMorph = true,
                PrimarySplitSeeded = false,
                FusionsEnabled = true,
                PrimarySecondaryCostMultiplier = 1f,
                UpgradeCostScale = 1f,
                PartsMultiplier = () => 1f,
                SupercellCadenceAreas = 1,
                RackModuleDropsHere = false,
            },
            new WorldDefinition
            {
                Id = WorldIds.Stormdrain,
                ConfigKey = WorldLibrary.World2,
                Palette = BiomePalette.Stormdrain,
                Look = BackyardLook.Stormdrain,
                Kit = WorldKit.Stormdrain,
                GateSkin = WorldGateSkin.Stormdrain,
                Style = new SentinelStyle(BodyColorWorld2, BodyAccentWorld2, EyeColorWorld2, emissionFactor: 0.35f,
                    boltColor: SentinelBolt.BoltColor, boltThicknessScale: 1.6f),
                FiresWaterBeam = false,
                Music = MusicWorld.Stormdrain,
                RingAlphaScale = StormdrainRingAlphaScale,
                RigBoardResourcePath = RigBoardLibrary.World2ResourcePath,
                PrimaryWeapon = WeaponCatalog.PrimaryKind.Lppe,
                SecondaryWeapon = SecondaryKind.ShoulderRack,
                CarrySecondaryAcrossMorph = false,
                SecondaryMysteryLockedOnMorph = true,
                PrimarySplitSeeded = false,
                FusionsEnabled = false,
                PrimarySecondaryCostMultiplier = PrimarySecondaryCostMultiplierWorld2Plus,
                UpgradeCostScale = UpgradeCostScaleWorld2,
                PartsMultiplier = () => 1f,
                SupercellCadenceAreas = 2,
                RackModuleDropsHere = true,
            },
            new WorldDefinition
            {
                Id = WorldIds.Reef,
                ConfigKey = WorldLibrary.World3,
                Palette = BiomePalette.Reef,
                // MV-745/MV-1141: World 3 keeps the Backyard's own Default look for now — re-lighting it
                // is a different ticket's, not this one's (CLAUDE.md "Do not re-raise").
                Look = BackyardLook.Default,
                Kit = WorldKit.Reef,
                GateSkin = WorldGateSkin.Reef,
                Style = new SentinelStyle(BodyColorWorld3, BodyAccentWorld3, EyeColorWorld3, emissionFactor: 0.35f,
                    boltColor: BoltColorWorld3, boltThicknessScale: 1.6f),
                FiresWaterBeam = false,
                Music = MusicWorld.Reef,
                RingAlphaScale = 1f,
                RigBoardResourcePath = RigBoardLibrary.World3ResourcePath,
                PrimaryWeapon = WeaponCatalog.PrimaryKind.Undertow,
                SecondaryWeapon = SecondaryKind.ShoulderRack,
                CarrySecondaryAcrossMorph = true,
                SecondaryMysteryLockedOnMorph = false,
                PrimarySplitSeeded = true,
                FusionsEnabled = false,
                PrimarySecondaryCostMultiplier = PrimarySecondaryCostMultiplierWorld2Plus,
                UpgradeCostScale = 1f,
                // MV-1029/MV-1142: the Reef's row stays live-tunable via the Settings panel's "W3
                // parts x" knob -- see PartsMultiplier's own doc comment.
                PartsMultiplier = () => DevTuning.Or(DevTuning.World3PartsMultiplier, CellEconomyTuning.DefaultWorld3PartsMultiplier),
                SupercellCadenceAreas = 2,
                RackModuleDropsHere = false,
            },
        };
    }
}
