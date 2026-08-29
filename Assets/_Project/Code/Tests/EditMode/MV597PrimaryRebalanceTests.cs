using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-597 (PRIMARY rebalance): PIERCE (<c>p_prc</c>) deleted outright, FLOW renamed ENDURANCE
    /// everywhere a player can see it and rebalanced so level 1 already pays out, and
    /// <see cref="RigState.RestoreSnapshot"/> made defensive against a save captured before either
    /// change. One test, per CC_AUTONOMY's "at most one new test per ticket" rule — this is that one;
    /// the cap-value coverage this ticket also changed lives in the existing
    /// <see cref="WeaponCatalogTests"/>/<see cref="WeaponSystemStateTests"/> suites, updated in place
    /// rather than duplicated here.
    ///
    /// Proven to fail pre-fix: <c>RigBoard.Exists("p_prc")</c> was true, <c>RestoreSnapshot</c> had no
    /// drop/clamp logic at all (so the stale/out-of-range levels below would have round-tripped
    /// unchanged), the drain multiplier read 1.0 (a no-op) at level 1 instead of 0.90, the board's own
    /// label Text still read "FLOW", and the Settings panel still carried a knob literally named
    /// "Depletion rate".
    /// </summary>
    public sealed class MV597PrimaryRebalanceTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            RigState.Reset();
            RigFusionState.Reset();
            WeaponSystemState.Reset();
            PickupWallet.Reset();
        }

        /// <summary>Same reflection idiom <see cref="MV525SliderBoundsInvariantTests"/> already uses to
        /// read <c>SettingsPanel</c>'s registered knobs without building the full uGUI tree — needs no
        /// Canvas/EventSystem.</summary>
        private static List<string> BuildAllKnobNames()
        {
            var go = new GameObject("SettingsPanel Knob Probe");
            try
            {
                var panel = go.AddComponent<SettingsPanel>();
                var panelType = panel.GetType();
                panelType.GetMethod("BuildKnobs", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(panel, null);

                var knobsField = panelType.GetField("_knobs", BindingFlags.NonPublic | BindingFlags.Instance);
                var knobs = (IEnumerable)knobsField.GetValue(panel);

                var names = new List<string>();
                foreach (var knob in knobs)
                    names.Add((string)knob.GetType().GetField("Name").GetValue(knob));
                return names;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PierceIsGone_CapacityCurveHasNoDeadLevel_SavesDropOrClampStaleLevels_AndNoScreenStillSaysFlow()
        {
            // ---------------------------------------------------------------- AC1: PIERCE deleted, board loads with no orphan/mis-parent
            Assert.That(RigBoard.Exists("p_prc"), Is.False, "p_prc must no longer exist in rig_board.json");

            foreach (string id in RigBoard.AllIds)
            {
                string parent = RigBoard.Parent(id);
                Assert.That(string.IsNullOrEmpty(parent) || RigBoard.Exists(parent), Is.True,
                    $"'{id}' names a parent ('{parent}') that does not exist in the tree — an orphaned reference");
            }

            // ---------------------------------------------------------------- AC2 + AC6: RestoreSnapshot drops a retired id and clamps out-of-range ones
            var staleCheckpoint = new Dictionary<string, int>
            {
                { "p_prc", 2 },   // a node an older save owned, since deleted this ticket
                { "p_dmg", 6 },   // above the new cap of 4
                { "p_spr", 9 },   // above the new cap of 4
                { "p_flw", 3 },   // in range — must survive the restore untouched
            };
            RigState.RestoreSnapshot(staleCheckpoint, new[] { "PRIMARY" });

            Assert.That(RigState.SnapshotLevels().ContainsKey("p_prc"), Is.False,
                "a retired node id must not be stranded in the restored state — no dangling reference");
            Assert.That(RigState.Level("p_dmg"), Is.EqualTo(4), "an out-of-range p_dmg level must clamp to its new cap, not persist or throw");
            Assert.That(RigState.Level("p_spr"), Is.EqualTo(4), "an out-of-range p_spr level must clamp to its new cap, not persist or throw");
            Assert.That(RigState.Level("p_flw"), Is.EqualTo(3), "an already in-range level must survive the restore exactly as saved");

            // ---------------------------------------------------------------- AC3: the drain multiplier pays out from level 1 with no dead level
            float previous = float.PositiveInfinity;
            for (int level = 1; level <= WeaponCatalog.MaxLevel(WeaponTrackKind.Capacity); level++)
            {
                float drain = WeaponCatalog.EffectiveDrainPerSecond(10f, level, WeaponCatalog.DefaultRcdaDepletionRatePerLevel);
                Assert.That(drain, Is.LessThan(previous), $"level {level} must drain strictly slower than level {level - 1} — no dead level");
                previous = drain;
            }
            Assert.That(WeaponCatalog.EffectiveDrainPerSecond(10f, 1, WeaponCatalog.DefaultRcdaDepletionRatePerLevel),
                Is.EqualTo(9f).Within(1e-4f), "level 1 must already cut the drain to 0.90x (9 from a base of 10)");
            Assert.That(WeaponCatalog.EffectiveDrainPerSecond(10f, 8, WeaponCatalog.DefaultRcdaDepletionRatePerLevel),
                Is.EqualTo(2f).Within(1e-4f), "level 8 must land exactly on the 0.20x drain floor (2 from a base of 10)");

            // ---------------------------------------------------------------- AC7: no user-facing string still renders FLOW / DEPLETION RATE for this node
            RigState.Reset();
            // p_flw's board render only shows its real label once it's a live draft candidate
            // (RigState.IsCellUnlockable — needs its parent p_dmg at level >= 2, tighter than merely
            // "reached"); at the bare Reset() baseline (p_dmg level 1) it would still read the LOCK
            // placeholder "? ? ?", same as any other parent-gated node (see MV516's own p_rng fixture).
            RigState.RaiseLevel("p_dmg");
            var go = new GameObject("MV597_WeaponsScreen");
            try
            {
                var screen = go.AddComponent<WeaponsScreen>();
                screen.Open();

                var node = screen.BoardNode("p_flw");
                Assert.That(node, Is.Not.Null, "p_flw must still have a built board node");
                var label = node.Find("Text")?.GetComponent<Text>();
                Assert.That(label, Is.Not.Null, "p_flw's board node must carry a label Text component");
                Assert.That(label.text, Is.EqualTo("CAPACITY"), "MV-609: the board's own built Text must read CAPACITY, not ENDURANCE or FLOW");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }

            Assert.That(WeaponCatalog.DisplayName(WeaponTrackKind.Capacity), Is.EqualTo("CAPACITY"),
                "MV-609: WeaponCatalog.DisplayName must also read CAPACITY, not ENDURANCE or DEPLETION RATE");

            // The always-compiled, real in-game Settings panel (SettingsPanel.cs doc: "ALWAYS compiled
            // into every build") has its own live "Depletion rate" knob tuning the tank's BASE drain —
            // a different concept from this track's own per-level curve, but a name a player could read
            // as the same thing. Assert on the actual registered knob names, not a source constant.
            var knobNames = BuildAllKnobNames();
            foreach (string name in knobNames)
            {
                string upper = name.ToUpperInvariant();
                Assert.That(upper, Does.Not.Contain("FLOW"), $"knob '{name}' must not still say FLOW");
                Assert.That(upper, Does.Not.Contain("DEPLETION"), $"knob '{name}' must not still say DEPLETION");
            }
        }
    }
}
