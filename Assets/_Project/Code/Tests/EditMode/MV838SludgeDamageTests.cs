using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-838 (Lee, 2026-09-17 decision): "Sludge slows AND damages Max, using the flood's damage
    /// logic. If Max has shield on there is no damage anyway." Fails to COMPILE on the pre-fix base
    /// commit (10fce02): <c>MapSludgeDamageRunner</c>/<c>MapSludgeDamage</c>/<c>MapSludgeDamageTicker</c>
    /// do not exist there (CS0246 "the type or namespace name 'MapSludgeDamageRunner' could not be
    /// found") before a single assertion runs.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), one narrative per AC bullet, same idiom
    /// as <c>MV836FloodOffTests</c>/<c>MV837DeckSludgeTests</c>: loads the real, shipped World 2 config
    /// through <see cref="WorldLibrary"/>/<see cref="WorldMapLoader"/> (no hand-authored fixture) and
    /// drives the real <see cref="MapSludgeDamageRunner.TickSludgeDamage"/> path.
    /// </summary>
    public sealed class MV838SludgeDamageTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo AbsorbRemainingField =
            typeof(PlayerAbilities).GetField("_forceFieldAbsorbRemaining", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            WeaponSystemState.Reset();
            RigState.Reset();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            RigState.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void SludgeDamagesMaxAtFloorLevel_SkipsWithForceFieldUp_NeverTouchesADeckOrARobot()
        {
            // Same BuildBody collider-strip [Error] every full-World2-build EditMode test in this suite
            // carries once Build() actually runs — see other World2 EditMode tests' own note.
            LogAssert.ignoreFailingMessages = true;

            GameObject pathGo = null, playerGo = null, runnerGo = null, rusherGo = null;
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
                Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                // StormdrainFlood.IsFlooded/MapGeometry.SpeedMultiplierAt both resolve their zone off
                // EnemyNavigation.Map, which finds the map through a live BackyardPath, not through the
                // local `map` this test already has — same stub MV836FloodOffTests/MV837DeckSludgeTests
                // install for the same reason.
                pathGo = new GameObject("MV838-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                WorldArea a3 = cfg.Area("a3");
                Assert.IsNotNull(a3, "setup failure: World 2 must author area 'a3'");
                WorldSludge a3Sludge1 = Array.Find(a3.sludge, s => s.id == "a3_sludge1");
                Assert.IsNotNull(a3Sludge1, "setup failure: a3 must author sludge 'a3_sludge1'");
                Rect a3SludgeRect = a3.WorldRectOf(a3Sludge1.x, a3Sludge1.z, a3Sludge1.w, a3Sludge1.d);
                Vector3 floorPoint = new Vector3(a3SludgeRect.center.x, 0f, a3SludgeRect.center.y);

                playerGo = new GameObject("Player") { tag = "Player" };
                playerGo.AddComponent<CharacterController>();
                playerGo.AddComponent<PlayerController>();
                var abilities = playerGo.AddComponent<PlayerAbilities>();
                var health = playerGo.AddComponent<PlayerHealth>();
                health.Initialize(); // MV-464: exposed publicly so an EditMode test can invoke it directly
                playerGo.transform.position = floorPoint;

                runnerGo = new GameObject("MV838 sludge runner");
                var runner = runnerGo.AddComponent<MapSludgeDamageRunner>();

                const float simulatedSeconds = 2f;
                const float step = 0.1f;

                // === AC5: the flood switch is off by design (MV-836) — sludge damage must not depend
                // === on it at all. ===
                Assert.IsFalse(StormdrainFlood.FloodEnabled, "setup failure: MV-836 must still have the flood switched off");

                // === AC1: Max at floor level (y 0) inside a3_sludge1, Force Field down, 2.0s -> loses
                // === 12 HP (6/s), within the AC's own ±1.5 tolerance. ===
                float beforeFloor = health.Current;
                for (float t = 0f; t < simulatedSeconds; t += step) runner.TickSludgeDamage(step);
                float lost = beforeFloor - health.Current;
                Assert.AreEqual(12f, lost, 1.5f,
                    "MV-838: 2s standing in floor-level map sludge with the Force Field down must cost ~12 HP");

                // === AC3: Max on a17_deck1 (y == map.deckHeight), above the same a3 sludge channel,
                // === 2.0s -> 0 damage. Force Field is still down here. (a17 was a19 before MV-865
                // === renumbered World 2's areas in play order; a3 itself is unchanged.) ===
                WorldArea a17 = cfg.Area("a17");
                Assert.IsNotNull(a17, "setup failure: World 2 must author area 'a17'");
                WorldDeck a17Deck1 = Array.Find(a17.decks, d => d.id == "a17_deck1");
                Assert.IsNotNull(a17Deck1, "setup failure: a17 must author deck 'a17_deck1'");
                Rect a17DeckRect = a17.WorldRectOf(a17Deck1.x, a17Deck1.z, a17Deck1.w, a17Deck1.d);
                var overlap = new Vector2(a17DeckRect.center.x, a3SludgeRect.center.y);
                Assert.IsTrue(a3SludgeRect.Contains(overlap), "setup failure: the probe point must fall inside a3's sludge rect");
                Assert.IsTrue(a17DeckRect.Contains(overlap), "setup failure: the probe point must fall inside a17's deck rect");

                health.Revive();
                playerGo.transform.position = new Vector3(overlap.x, map.deckHeight, overlap.y);
                float healthOnDeck = health.Current;
                for (float t = 0f; t < simulatedSeconds; t += step) runner.TickSludgeDamage(step);
                Assert.AreEqual(healthOnDeck, health.Current, 0.001f,
                    "MV-838: standing on a17_deck1 above a3's sludge channel must take no sludge damage");

                // === AC2: back at floor level, Force Field UP, 2.0s -> both HP and the field's own
                // === remaining absorb budget are unchanged (it ignores sludge ticks entirely, never
                // === absorbs them from its budget). ===
                health.Revive();
                playerGo.transform.position = floorPoint;
                abilities.ForceActivateForceFieldForTuning();
                float healthBeforeFF = health.Current;
                float absorbBeforeFF = (float)AbsorbRemainingField.GetValue(abilities);
                for (float t = 0f; t < simulatedSeconds; t += step) runner.TickSludgeDamage(step);
                Assert.AreEqual(healthBeforeFF, health.Current, 0.001f,
                    "MV-838: sludge must deal no damage at all while the Force Field is up");
                Assert.AreEqual(absorbBeforeFF, (float)AbsorbRemainingField.GetValue(abilities), 0.001f,
                    "MV-838: sludge must never drain the Force Field's absorb budget");

                // === AC4: a Rusher standing in the same floor-level sludge, 2.0s -> 0 damage. Map sludge
                // === only ever hurts Max (MV-795) — MapSludgeDamageRunner never wires a robot in at all. ===
                rusherGo = new GameObject("Rusher");
                var rusher = rusherGo.AddComponent<RobotEnemy>();
                rusher.Apply(EnemyArchetype.Rusher);
                rusherGo.transform.position = floorPoint;
                float rusherHealthBefore = rusher.HealthCurrent;
                for (float t = 0f; t < simulatedSeconds; t += step) runner.TickSludgeDamage(step);
                Assert.AreEqual(rusherHealthBefore, rusher.HealthCurrent, 0.001f,
                    "MV-838: a robot standing in map sludge must take no damage from it");
            }
            finally
            {
                if (rusherGo != null) UnityEngine.Object.DestroyImmediate(rusherGo);
                if (runnerGo != null) UnityEngine.Object.DestroyImmediate(runnerGo);
                if (playerGo != null) UnityEngine.Object.DestroyImmediate(playerGo);
                if (pathGo != null) UnityEngine.Object.DestroyImmediate(pathGo);
            }
        }
    }
}
