using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1000 (Lee, TestFlight, 2026-09-28, World 2): a robot's orange/yellow attack-warning
    /// telegraph draws on the ground-level floor beneath a raised deck, not under the robot standing
    /// on the deck. <see cref="TelegraphVfx.Ground"/> flattened every telegraph straight to a fixed
    /// <c>y=0</c> — the identical fault MV-898 already fixed for <see cref="GroundAnchorVfx.Ground"/>,
    /// but MV-898 only ever touched that one call site. Both now delegate to
    /// <see cref="GroundMarkHeights.SurfaceAt"/> so they cannot drift apart again.
    ///
    /// One consolidated EditMode test (testing policy MV-465, Rule 1): builds World 2 through the real
    /// <see cref="WorldLibrary"/> config and probes the same authored deck
    /// <c>MV898GroundRingSurfaceHeightTests</c> already proves (so this exercises the actual shipped
    /// map, not a hand-built fixture), drives a real Rusher <see cref="RobotEnemy"/> into
    /// <see cref="RobotEnemy.State.Telegraph"/> via the same reflection-driven <c>TickChase</c> idiom
    /// <c>MV428MeleeReadabilityTests</c> already uses (EditMode never runs <c>Update()</c>), and drives
    /// <see cref="TelegraphVfx.LateUpdate"/> itself via reflection — the same proven-in-EditMode path
    /// <c>MV898GroundRingSurfaceHeightTests</c> already uses for <see cref="GroundAnchorVfx"/>.
    ///
    /// Tier 2 (resolved values): reads the world position off the actual pooled <see cref="GroundRing"/>
    /// <c>LateUpdate</c> placed for the telegraphing robot, never a constant.
    ///
    /// Fails on base commit a081bc1: with <c>Ground</c> still flattening to a fixed <c>y=0</c>, the ring
    /// for the deck-standing robot resolves to <c>y=GroundRing.GroundLift</c> (~0.06) instead of
    /// <c>y=deckHeight+GroundLift</c> (~2.56) — see the fix comment for the exact quoted failure.
    /// </summary>
    public sealed class MV1000TelegraphSurfaceHeightTests
    {
        private sealed class FakePlayer : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) { }
        }

        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo StateTimerField =
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo TickChaseMethod =
            typeof(RobotEnemy).GetMethod("TickChase", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo TelegraphLateUpdateMethod =
            typeof(TelegraphVfx).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo RobotOnDisableMethod =
            typeof(RobotEnemy).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            RobotEnemy.ResetRegistry();
            LungeTokenPool.Reset();
            DevTuning.Reset();
            EnemyNavigation.Reset();
        }

        /// <summary>Spawns a Rusher and drives it into <see cref="RobotEnemy.State.Telegraph"/> in one
        /// tick — the same distance-1, GiveSight-then-TickChase setup
        /// <c>MV428MeleeReadabilityTests.BruiserHeavyBrute_NeverEnterLunge_RusherAndBlinkerStillDo</c>
        /// already proves reaches Telegraph for a Rusher. <paramref name="at"/> is the robot's own
        /// spawn point — kept exactly where the caller wants it probed, never offset — while
        /// <paramref name="playerTransform"/> supplies the distance-1 target that makes the wind-up
        /// commit.</summary>
        private static RobotEnemy SpawnTelegraphingRusher(Vector3 at, Transform playerTransform)
        {
            var go = new GameObject("MV1000 Rusher");
            go.transform.position = at;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // EditMode never runs Awake/OnEnable's usual wiring (same note as MV428's NewEnemy), so
            // _cc — normally seeded there — has to be stamped by hand before TickChase's movement can
            // call CharacterControllerMotion.SafeMove on it.
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Rusher); // stamps stats and re-runs ResetState, which finds the tagged Player

            // Outside Play mode OnEnable never fires on its own, so RobotEnemy.Active — the registry
            // TelegraphVfx.DrawEnemyWindups reads — stays empty without this: the same reflection idiom
            // MV832SentinelTargetingTests/MV531DissolveSnapshotTests/etc. already use everywhere else in
            // this suite that needs a robot TelegraphVfx/Sentinel/Replicator can actually find.
            RobotOnEnableMethod.Invoke(e, null);

            e.Sight.Tick(true, playerTransform.position, 0.02f);
            TickChaseMethod.Invoke(e, new object[] { 0.02f });
            Assert.AreEqual(RobotEnemy.State.Telegraph, e.Current,
                "test setup: a Rusher one step from a tagged Player must commit to Telegraph (MV-428)");

            // TelegraphProgress is 0 the instant Telegraph is entered (_stateTimer resets to 0), and
            // DrawEnemyWindups skips anything at progress 0 — force the clock forward without another
            // Tick (which could carry it on into Lunge) so the ring this test reads actually gets drawn.
            StateTimerField.SetValue(e, 0.1f);
            return e;
        }

        [Test]
        public void TelegraphRing_ForARobotOnADeck_ResolvesToDeckHeight_NotTheFloorBelowIt()
        {
            Assert.IsNotNull(TelegraphLateUpdateMethod, "TelegraphVfx.LateUpdate went missing");
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            Assert.IsNotNull(TickChaseMethod, "RobotEnemy.TickChase went missing");

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            MapZone zone = map.Zone("area11");
            Assert.IsNotNull(zone, "MV-1000: world2_config.json must still author area 'a11' (zone id 'area11')");
            MapEntity deck = map.Entity("a11_deck1");
            Assert.IsNotNull(deck, "MV-1000: world2_config.json must still author 'a11_deck1'");

            // A floor probe point: inside a11's own zone, but clear of the deck's rect — pick whichever
            // end of the zone (in Z) sits furthest from the deck, then confirm both facts hold rather
            // than assuming them (same setup as MV898GroundRingSurfaceHeightTests).
            float farZ = Mathf.Abs(zone.ZMin - deck.z) > Mathf.Abs(zone.ZMax - deck.z) ? zone.ZMin + 1f : zone.ZMax - 1f;
            var floorPos = new Vector3(zone.x, 0f, farZ);
            Assert.Greater(Mathf.Abs(floorPos.z - deck.z), deck.depth * 0.5f + 0.5f,
                "test setup: the floor probe point must sit outside the deck's own rect");
            Assert.IsTrue(zone.Contains(floorPos.x, floorPos.z),
                "test setup: the floor probe point must sit inside a11's own zone");

            var deckPos = new Vector3(deck.x, deck.height, deck.z);

            GameObject pathGo = null, telegraphGo = null, playerGo = null, robotGo = null;
            try
            {
                pathGo = new GameObject("MV1000-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                telegraphGo = new GameObject("MV1000 TelegraphDirector");
                var telegraph = telegraphGo.AddComponent<TelegraphVfx>();

                playerGo = new GameObject("MV1000 Player") { tag = "Player" };
                playerGo.AddComponent<FakePlayer>();

                // --- Phase 1: the robot is standing on a11_deck1 --------------------------------------
                // The player only supplies the distance-1 target that makes the wind-up commit — offset
                // from the robot's own spawn point rather than the other way round, so the robot itself
                // stays exactly on the deck's authored position for Ground() to resolve.
                playerGo.transform.position = deckPos + new Vector3(1f, 0f, 0f);
                RobotEnemy robot = SpawnTelegraphingRusher(deckPos, playerGo.transform);
                robotGo = robot.gameObject;
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)

                TelegraphLateUpdateMethod.Invoke(telegraph, null);

                var deckRings = telegraph.GetComponentsInChildren<GroundRing>(includeInactive: true)
                    .Where(r => r.gameObject.activeSelf && r.name == "TelegraphRing").ToArray();
                Assert.AreEqual(1, deckRings.Length,
                    "exactly one telegraph ring should be placed for the deck-standing robot");
                Assert.AreEqual(deck.height, deckRings[0].transform.position.y - GroundRing.GroundLift, 0.01f,
                    $"telegraph ring at {deckRings[0].transform.position} is not within 0.01m of " +
                    $"a11_deck1's own top ({deck.height})");

                // MV-809's same note: SetActive/Destroy doesn't synchronously fire OnDisable outside
                // Play mode, so without this the destroyed phase-1 robot stays a phantom entry in
                // RobotEnemy.Active and phase 2's LateUpdate throws MissingReferenceException on it.
                RobotOnDisableMethod.Invoke(robot, null);
                Object.DestroyImmediate(robotGo);
                robotGo = null;

                // --- Phase 2: the robot is standing on the area floor ----------------------------------
                playerGo.transform.position = floorPos + new Vector3(1f, 0f, 0f);
                robot = SpawnTelegraphingRusher(floorPos, playerGo.transform);
                robotGo = robot.gameObject;
                Physics.SyncTransforms();

                TelegraphLateUpdateMethod.Invoke(telegraph, null);

                var floorRings = telegraph.GetComponentsInChildren<GroundRing>(includeInactive: true)
                    .Where(r => r.gameObject.activeSelf && r.name == "TelegraphRing").ToArray();
                Assert.AreEqual(1, floorRings.Length,
                    "exactly one telegraph ring should be placed for the floor-standing robot");
                Assert.AreEqual(0f, floorRings[0].transform.position.y - GroundRing.GroundLift, 0.01f,
                    $"telegraph ring at {floorRings[0].transform.position} is not within 0.01m of the area floor");
            }
            finally
            {
                if (telegraphGo != null) Object.DestroyImmediate(telegraphGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                if (playerGo != null) Object.DestroyImmediate(playerGo);
                if (robotGo != null) Object.DestroyImmediate(robotGo);
            }
        }
    }
}
