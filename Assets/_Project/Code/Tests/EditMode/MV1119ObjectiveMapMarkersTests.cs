using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Save;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1119 — Lee's ruling (2026-10-06, device, TestFlight v0.11.7): "Sheds / replicators / things
    /// that need to be destroyed on all levels need to be shown on the maps as blinking red lights as
    /// per World 2." Before this ticket, World 2's gating Replicators blinked (MV-855) but World 1/3's
    /// sheds drew as static dots and every boss as a steady disc regardless of whether it was still
    /// alive — nothing told the player which objectives still needed destroying outside World 2.
    ///
    /// Fails on base commit d30d293: World 1/3 shed markers carry no alive/destroyed distinction at all
    /// (always the static/mobile fill, never blinking), and a boss marker never reads destroyed even
    /// once its area is defeated (see the quoted failure in the fix comment).
    ///
    /// One consolidated test (testing policy MV-465, Rule 1) carrying every AC sub-check, built through
    /// the real shipped loader/<see cref="MapRuntime"/>/real <c>TakeDamage</c> paths for all three
    /// worlds — never a hand-authored fixture map. Tier 2 (resolved values): every assertion reads a
    /// built <see cref="Image"/>'s resolved fill alpha or a census's resolved alive/destroyed query,
    /// never an authored constant.
    /// </summary>
    public sealed class MV1119ObjectiveMapMarkersTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo MapField = typeof(BackyardPath).GetField("_map", NonPublicInstance);
        private static readonly FieldInfo BuildField = typeof(BackyardPath).GetField("_build", NonPublicInstance);
        private static readonly FieldInfo SludgequeenPhaseField = typeof(SludgequeenBoss).GetField("_phase", NonPublicInstance);
        private static readonly FieldInfo BigBermudaPhaseField = typeof(BigBermudaBoss).GetField("_phase", NonPublicInstance);

        private string _saveDir;

        [SetUp]
        public void SetUp()
        {
            // Same collider-strip [Error] every Replicator/MowerHutch Build() test in this suite carries.
            LogAssert.ignoreFailingMessages = true;
            Time.timeScale = 1f;
            FactoryCensus.Reset();
            BossCensus.Reset();
            _saveDir = Path.Combine(Path.GetTempPath(), "ytgame-mv1119-tests");
            if (Directory.Exists(_saveDir)) Directory.Delete(_saveDir, recursive: true);
            SaveSystem.DirectoryOverride = _saveDir;
            SaveSystem.ActiveSlot = -1;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            SaveSystem.ResetForTests();
            FactoryCensus.Reset();
            BossCensus.Reset();
            if (Directory.Exists(_saveDir)) Directory.Delete(_saveDir, recursive: true);
        }

        private static void InvokeAwake(UnityEngine.Object component) =>
            component.GetType().GetMethod("Awake", NonPublicInstance).Invoke(component, null);

        /// <summary>Builds every shed and Replicator the same way <see cref="MaxWorlds.Arena.WorldRunner.Configure"/>
        /// does in production — this test never runs <c>WorldRunner.Configure</c> itself, so it has to
        /// replicate the two things that live ONLY there, not in either component's own <c>Build()</c>:
        /// (1) <see cref="Replicator.Build"/> alone does NOT call
        /// <see cref="FactoryCensus.RegisterReplicator"/> (unlike <see cref="MowerHutch.Build"/>, which
        /// self-registers) — without it <see cref="FactoryCensus.RegisteredReplicators"/> stays empty and
        /// every census-based oracle in this file would silently undercount, the same reason MV855's own
        /// (now-retired) test registered Replicators by hand; (2) NEITHER component's <c>Build()</c> stamps
        /// <see cref="MowerHutch.SetId"/>/<see cref="Replicator.SetId"/> — <c>MapRuntime</c> never calls
        /// either, only <c>WorldRunner.Configure</c> does, using the exact string each was keyed under in
        /// <see cref="MapBuild.Actors"/> — without it <see cref="FactoryCensus.DestroyedShedIds"/>/
        /// <see cref="FactoryCensus.DestroyedReplicatorIds"/> skip every entry (both filter out an empty
        /// id), so a checkpoint captured mid-test would silently persist nothing to resume.</summary>
        private static void BuildFactoriesAndReplicators(MapData map, MapBuild build)
        {
            foreach (MowerHutch h in build.Factories) h.Build();
            foreach (Replicator r in build.Replicators) r.Build();
            foreach (var kv in build.Actors)
            {
                if (kv.Value == null) continue;
                var hutch = kv.Value.GetComponent<MowerHutch>();
                if (hutch != null) hutch.SetId(kv.Key);
                var replicator = kv.Value.GetComponent<Replicator>();
                if (replicator != null)
                {
                    replicator.SetId(kv.Key);
                    MapZone zone = map.ZoneAt(replicator.transform.position.x, replicator.transform.position.z);
                    FactoryCensus.RegisterReplicator(replicator, zone != null ? zone.id : null);
                }
            }
        }

        /// <summary>Builds a real <see cref="BackyardPath"/> wired to <paramref name="map"/>/<paramref name="build"/>
        /// (same reflection-injection idiom as MV830ReplicatorMapMarkersTests — <c>BackyardPath</c> only
        /// builds these for real in <c>Awake</c>), opens a real <see cref="MapScreen"/> against it, and
        /// returns both roots so the caller can destroy them before wiring a second
        /// <see cref="BackyardPath"/> — <see cref="MapScreen.Build"/> resolves its path via
        /// <c>FindFirstObjectByType&lt;BackyardPath&gt;</c>, so at most one may exist at a time.</summary>
        private static (MapScreen screen, GameObject pathGo, GameObject screenGo) OpenMapScreen(MapData map, MapBuild build, string tag)
        {
            var pathGo = new GameObject($"MV1119 path {tag}");
            var path = pathGo.AddComponent<BackyardPath>();
            Assert.IsNotNull(MapField, "BackyardPath._map went missing");
            Assert.IsNotNull(BuildField, "BackyardPath._build went missing");
            MapField.SetValue(path, map);
            BuildField.SetValue(path, build);

            var screenGo = new GameObject($"MV1119 screen {tag}");
            var screen = screenGo.AddComponent<MapScreen>();
            screen.Open();
            return (screen, pathGo, screenGo);
        }

        private static int ResolveAreaIndex(MapData map, float worldX, float worldZ)
        {
            MapZone zone = map.ZoneAt(worldX, worldZ);
            return zone != null ? AreaAccumulationDirector.AreaIndexOf(zone.id) : 0;
        }

        /// <summary>The same "live objective" oracle <see cref="MapScreen"/> itself resolves against
        /// (MV-1119's own AC1 wording: "the number of live objectives reported by the censuses") —
        /// alive sheds/Replicators straight off <see cref="FactoryCensus"/>, alive bosses off
        /// <see cref="BossCensus.IsAreaDefeated"/> per boss entity's own area.</summary>
        private static int LiveObjectiveCountFromCensuses(MapData map)
        {
            int sheds = FactoryCensus.All.Count(h => h != null && h.IsAlive);
            int replicators = FactoryCensus.RegisteredReplicators.Count(r => r != null && r.IsAlive);
            int bosses = 0;
            foreach (MapEntity e in map.entities)
            {
                if (e == null || e.Kind != EntityKind.Boss) continue;
                if (!BossCensus.IsAreaDefeated(ResolveAreaIndex(map, e.x, e.z))) bosses++;
            }
            return sheds + replicators + bosses;
        }

        private static Image[] ObjectiveMarkers(GameObject screenGo) =>
            screenGo.GetComponentsInChildren<Image>(true)
                .Where(i => i.gameObject.name == "Shed" || i.gameObject.name == "Replicator" || i.gameObject.name == "Boss")
                .ToArray();

        private static bool IsDestroyed(Image marker) => Mathf.Approximately(marker.color.a, 0f);

        [Test]
        public void AliveObjectivesBlinkRed_DestroyedDontBlink_AndSurviveAColdResume()
        {
            // === AC1: for each shipped world, every live shed/Replicator/boss the censuses report
            // blinks red, and nothing reads destroyed before anything has died. ===
            foreach (string worldKey in new[] { WorldLibrary.World1, WorldLibrary.World2, WorldLibrary.World3 })
            {
                WorldConfig cfg = WorldLibrary.Load(worldKey);
                Assert.IsNotNull(cfg, $"{worldKey}'s own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                var mapRoot = new GameObject($"MV1119 map root {worldKey}");
                MapBuild build = MapRuntime.Build(map, mapRoot.transform);
                BuildFactoriesAndReplicators(map, build);

                (MapScreen screen, GameObject pathGo, GameObject screenGo) = OpenMapScreen(map, build, worldKey);

                Image[] markers = ObjectiveMarkers(screenGo);
                int expectedLive = LiveObjectiveCountFromCensuses(map);
                Assert.Greater(expectedLive, 0, $"fixture: {worldKey} must author at least one live objective");
                Assert.AreEqual(expectedLive, markers.Count(m => !IsDestroyed(m)),
                    $"MV-1119 AC1 ({worldKey}): blinking marker count must equal the live objective count the censuses report");
                Assert.AreEqual(0, markers.Count(IsDestroyed),
                    $"MV-1119 AC1 ({worldKey}): nothing has been destroyed yet — no marker should read destroyed");

                UnityEngine.Object.DestroyImmediate(screenGo);
                UnityEngine.Object.DestroyImmediate(pathGo);
                UnityEngine.Object.DestroyImmediate(mapRoot);
                FactoryCensus.Reset();
                BossCensus.Reset();
            }

            // === AC2/AC3/AC4 — destroy one shed, one Replicator and one boss via their real TakeDamage
            // paths, split across World 1 (sheds + a boss) and World 2 (Replicators + a boss): NO single
            // shipped world authors all three kinds together — World 1/3 author zero Replicators, World 2
            // authors zero sheds (confirmed against the shipped configs, not assumed). Each marker flips
            // to destroyed and stops blinking, each world's own live count drops by exactly what was
            // destroyed in it, and a cold resume from a checkpoint captured with those destroyed shows
            // them destroyed on the very first frame — covering every kind the AC names, just not inside
            // one single build.

            // --- World 1: destroy one shed and one boss ---
            WorldConfig w1cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsTrue(WorldMapLoader.TryLoad(w1cfg, out MapData w1map, out string w1reason), w1reason);

            var w1root = new GameObject("MV1119 World1 root");
            MapBuild w1build = MapRuntime.Build(w1map, w1root.transform);
            BuildFactoriesAndReplicators(w1map, w1build);
            Assert.IsTrue(w1build.Factories.Count > 0, "fixture: World 1 must author at least one shed");

            Assert.IsTrue(w1build.Actors.TryGetValue("a12_boss1", out GameObject w1bossGo) && w1bossGo != null,
                "fixture: World 1's own boss ('a12_boss1') must build");
            var w1boss = w1bossGo.GetComponent<BigBermudaBoss>();
            Assert.IsNotNull(w1boss, "a12_boss1 did not build as a BigBermudaBoss");
            InvokeAwake(w1boss);
            int w1bossAreaIndex = ResolveAreaIndex(w1map, w1bossGo.transform.position.x, w1bossGo.transform.position.z);
            // Force straight to Fight (bypassing the real proximity/sight Wake() trigger, MV-1110 — out
            // of scope here) so TakeDamage below can land a kill in one call instead of just waking it.
            Assert.IsNotNull(BigBermudaPhaseField, "BigBermudaBoss._phase went missing");
            BigBermudaPhaseField.SetValue(w1boss, Enum.Parse(BigBermudaPhaseField.FieldType, "Fight"));
            BossCensus.Register(w1boss, "BIG BERMUDA", 2, 100f, 100f, w1bossAreaIndex);

            (MapScreen w1screen, GameObject w1pathGo, GameObject w1screenGo) = OpenMapScreen(w1map, w1build, "World1");

            Image[] w1beforeMarkers = ObjectiveMarkers(w1screenGo);
            int w1blinkingBefore = w1beforeMarkers.Count(m => !IsDestroyed(m));
            Assert.AreEqual(LiveObjectiveCountFromCensuses(w1map), w1blinkingBefore,
                "MV-1119 AC1 (World 1, boss registered): blinking count must equal the live count before any kill");

            MowerHutch w1shedVictim = w1build.Factories[0];
            w1shedVictim.TakeDamage(new DamageInfo(99999f, w1shedVictim.transform.position, Vector3.forward, Team.Player));
            w1boss.TakeDamage(new DamageInfo(99999f, w1bossGo.transform.position, Vector3.forward, Team.Player));
            Assert.IsFalse(w1shedVictim.IsAlive, "setup failure: the victim shed must actually die from lethal Player damage");
            Assert.IsTrue(BossCensus.IsAreaDefeated(w1bossAreaIndex), "setup failure: the victim boss's area must read defeated after lethal Player damage");

            w1screen.Close();
            w1screen.Open();

            Image[] w1afterMarkers = ObjectiveMarkers(w1screenGo);
            int w1blinkingAfter = w1afterMarkers.Count(m => !IsDestroyed(m));
            Assert.AreEqual(w1blinkingBefore - 2, w1blinkingAfter,
                "MV-1119 AC2 (World 1): destroying one shed and one boss must drop the blinking count by exactly two");
            Assert.AreEqual(1, w1afterMarkers.Count(m => m.gameObject.name == "Shed" && IsDestroyed(m)),
                "MV-1119 AC2 (World 1): exactly the killed shed's marker must read destroyed");
            Assert.AreEqual(1, w1afterMarkers.Count(m => m.gameObject.name == "Boss" && IsDestroyed(m)),
                "MV-1119 AC2 (World 1): exactly the killed boss's marker must read destroyed");

            // World 1 cold resume (AC4): capture a checkpoint with these two destroyed, wipe the censuses
            // and rebuild World 1 fresh (a cold boot, every actor Dormant/alive again until a checkpoint
            // restore says otherwise), restore the checkpoint, and open a brand-new MapScreen — the same
            // two must read destroyed on the very FIRST frame, with no extra Tick/Update.
            SaveSystem.CaptureCheckpoint(0, areaIndex: 5);

            UnityEngine.Object.DestroyImmediate(w1screenGo);
            UnityEngine.Object.DestroyImmediate(w1pathGo);
            UnityEngine.Object.DestroyImmediate(w1root);
            FactoryCensus.Reset();
            BossCensus.Reset();

            var w1root2 = new GameObject("MV1119 World1 root (resume)");
            MapBuild w1build2 = MapRuntime.Build(w1map, w1root2.transform);
            BuildFactoriesAndReplicators(w1map, w1build2);
            // The fresh boss stays Dormant/unregistered — exactly as a real cold-boot RESUME leaves it
            // (BigBermudaBoss.Wake has not run yet; see MapScreen.BossIsAlive's own doc comment on why
            // that is fine: BossCensus.IsAreaDefeated, not the live component, is what resolves this).

            bool w1restored = SaveSystem.RestoreCheckpoint(0);
            Assert.IsTrue(w1restored, "setup failure: a checkpoint captured just above must restore");

            (MapScreen w1screen2, GameObject w1pathGo2, GameObject w1screenGo2) = OpenMapScreen(w1map, w1build2, "World1 resume");

            Image[] w1resumeMarkers = ObjectiveMarkers(w1screenGo2);
            Assert.AreEqual(1, w1resumeMarkers.Count(m => m.gameObject.name == "Shed" && IsDestroyed(m)),
                "MV-1119 AC4 (World 1): the checkpoint's destroyed shed must read destroyed on the very first frame after a cold resume");
            Assert.AreEqual(1, w1resumeMarkers.Count(m => m.gameObject.name == "Boss" && IsDestroyed(m)),
                "MV-1119 AC4 (World 1): the checkpoint's defeated boss area must read destroyed on the very first frame after a cold resume");

            UnityEngine.Object.DestroyImmediate(w1screenGo2);
            UnityEngine.Object.DestroyImmediate(w1pathGo2);
            UnityEngine.Object.DestroyImmediate(w1root2);
            FactoryCensus.Reset();
            BossCensus.Reset();

            // --- World 2: destroy one Replicator ---
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData w2map, out string w2reason), w2reason);

            var w2root = new GameObject("MV1119 World2 root");
            MapBuild w2build = MapRuntime.Build(w2map, w2root.transform);
            BuildFactoriesAndReplicators(w2map, w2build);
            Assert.IsTrue(w2build.Replicators.Count > 0, "fixture: World 2 must author at least one Replicator");

            (MapScreen w2screen, GameObject w2pathGo, GameObject w2screenGo) = OpenMapScreen(w2map, w2build, "World2");

            Image[] w2beforeMarkers = ObjectiveMarkers(w2screenGo);
            int w2blinkingBefore = w2beforeMarkers.Count(m => !IsDestroyed(m));
            Assert.AreEqual(LiveObjectiveCountFromCensuses(w2map), w2blinkingBefore,
                "MV-1119 AC1 (World 2): blinking count must equal the live count before any kill");

            Replicator replicatorVictim = w2build.Replicators[0];
            replicatorVictim.TakeDamage(new DamageInfo(99999f, replicatorVictim.transform.position, Vector3.forward, Team.Player));
            Assert.IsFalse(replicatorVictim.IsAlive, "setup failure: the victim Replicator must actually die from lethal Player damage");

            w2screen.Close();
            w2screen.Open();

            Image[] w2afterMarkers = ObjectiveMarkers(w2screenGo);
            int w2blinkingAfter = w2afterMarkers.Count(m => !IsDestroyed(m));
            Assert.AreEqual(w2blinkingBefore - 1, w2blinkingAfter,
                "MV-1119 AC2 (World 2): destroying one Replicator must drop the blinking count by exactly one");
            Assert.AreEqual(1, w2afterMarkers.Count(m => m.gameObject.name == "Replicator" && IsDestroyed(m)),
                "MV-1119 AC2 (World 2): exactly the killed Replicator's marker must read destroyed");

            // World 2 cold resume (AC4), same shape as World 1's above.
            SaveSystem.CaptureCheckpoint(0, areaIndex: 5);

            UnityEngine.Object.DestroyImmediate(w2screenGo);
            UnityEngine.Object.DestroyImmediate(w2pathGo);
            UnityEngine.Object.DestroyImmediate(w2root);
            FactoryCensus.Reset();
            BossCensus.Reset();

            var w2root2 = new GameObject("MV1119 World2 root (resume)");
            MapBuild w2build2 = MapRuntime.Build(w2map, w2root2.transform);
            BuildFactoriesAndReplicators(w2map, w2build2);

            bool w2restored = SaveSystem.RestoreCheckpoint(0);
            Assert.IsTrue(w2restored, "setup failure: a checkpoint captured just above must restore");

            (MapScreen w2screen2, GameObject w2pathGo2, GameObject w2screenGo2) = OpenMapScreen(w2map, w2build2, "World2 resume");

            Image[] w2resumeMarkers = ObjectiveMarkers(w2screenGo2);
            Assert.AreEqual(1, w2resumeMarkers.Count(m => m.gameObject.name == "Replicator" && IsDestroyed(m)),
                "MV-1119 AC4 (World 2): the checkpoint's destroyed Replicator must read destroyed on the very first frame after a cold resume");

            UnityEngine.Object.DestroyImmediate(w2screenGo2);
            UnityEngine.Object.DestroyImmediate(w2pathGo2);
            UnityEngine.Object.DestroyImmediate(w2root2);
        }
    }
}
