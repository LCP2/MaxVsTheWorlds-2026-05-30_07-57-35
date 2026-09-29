using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1022: World 1's a20/a23 mobile sheds pursue Max down to a standoff and hold there. Built
    /// through the real production route (<see cref="WorldMapLoader"/>/<see cref="MapRuntime.Build"/>)
    /// against the real <c>a23_shed1</c> factory and a real Max (<see cref="PlayerController"/> +
    /// <see cref="CharacterController"/>), driving <see cref="MowerHutch.TickMobility"/> and Max's own
    /// split-motion/gravity loop for 20 s of simulated time, at both an ordinary frame dt and a device
    /// hitch dt.
    ///
    /// Fails on base commit 4436916 (and on 967d1be, which carries only MV-1021's unrelated
    /// CharacterController-create guard — MowerHutch.cs is unchanged between the two) on the SECOND
    /// assertion below, by arithmetic alone: the old <c>PursuitStandoff</c> was a flat 2 m measured
    /// CENTRE to centre. A shed built at the authored 2.25 m footprint resolves to a 1.125 m world
    /// capsule radius; Max's own <see cref="EnemyArchetype.PlayerRadius"/> is 0.5 m. 2 - 1.125 - 0.5 =
    /// 0.375 m of real surface-to-surface clearance at the standoff ring, under the 0.4 m floor this
    /// test asserts. (The ticket's own leading hypothesis — an un-swept overlap push-down through the
    /// floor — was tested and disproved in an earlier pass on this ticket: a stationary
    /// CharacterController is not perturbed by even an extreme, sustained, un-swept overlap in this
    /// engine version. The surface-clearance violation above is independent of that mechanism and reproduces
    /// by measurement alone.)
    /// </summary>
    public sealed class MV1022MobileShedStandoffTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo ComputeSplitMotion =
            typeof(PlayerController).GetMethod("ComputeSplitMotion", NonPublicInstance);

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", NonPublicInstance).Invoke(component, null);

        private static float WorldCapsuleRadius(CharacterController cc, Transform t)
        {
            Vector3 scale = t.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x) &&
            !float.IsNaN(v.y) && !float.IsInfinity(v.y) &&
            !float.IsNaN(v.z) && !float.IsInfinity(v.z);

        // Guards MV-1022
        [TestCase(1f / 60f)]
        [TestCase(1f / 20f)]
        public void A23ShedPursuit_KeepsMaxAboveTheFloorAndSurfaceClearOfTheShed_ForA20SSimulatedRun(float dt)
        {
            // MowerHutch.BuildCore's collider strip logs edit-mode DestroyImmediate noise regardless of
            // caller (same precedent MV547ShedFittingTests/MV548MobileShedTests carry).
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "world1_config failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV-1022 Map Root");
            GameObject maxGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.TryGetValue("a23_shed", out GameObject shedGo),
                    "a23_shed did not build off world1_config's real route");

                var hutch = shedGo.GetComponent<MowerHutch>();
                Assert.IsNotNull(hutch, "a23's shed carries no MowerHutch");
                InvokeAwake(hutch);

                var shedCc = shedGo.GetComponent<CharacterController>();
                Assert.IsNotNull(shedCc, "a23's mobile shed carries no CharacterController");

                // The player has already found and engaged this factory (Lee's own report: "shot the
                // shed") — ShouldLiftOff gates on Discoverable.FoundOn, which a freshly built, unseen
                // factory otherwise fails.
                shedGo.GetComponent<Discoverable>()?.Reveal();

                // Real Max, 3 m from the shed, standing on the real floor this map just built.
                Vector3 shedStart = shedGo.transform.position;
                maxGo = new GameObject("MV-1022 Max", typeof(CharacterController), typeof(PlayerController));
                maxGo.transform.position = new Vector3(shedStart.x + 3f, 1f, shedStart.z);
                var player = maxGo.GetComponent<PlayerController>();
                InvokeAwake(player);
                var maxCc = maxGo.GetComponent<CharacterController>();
                Physics.SyncTransforms();

                // Deal the shed one damage tick — the exact _tookDamage -> LiftOff trigger Lee's report names.
                hutch.TakeDamage(new DamageInfo(1f, shedStart, Vector3.forward, Team.Player));

                int totalTicks = Mathf.CeilToInt(20f / dt);
                int idleTicks = totalTicks / 2;

                for (int i = 0; i < totalTicks; i++)
                {
                    // Tick the shed's own state machine first, against Max's CURRENT position.
                    hutch.TickMobility(dt, maxGo.transform.position, null);

                    // Max: idle for the first half of the run, then walking toward the shed for the rest —
                    // the ticket's own "Max (idle, then walking toward the shed)" step 1 spec, capped
                    // short of actual contact. Once the shed holds its own pursuit ring (which it reaches
                    // well inside the idle half from a 3 m start), the only way to close the remaining gap
                    // further is Max's OWN CharacterController physically shoving into the shed's — a
                    // PhysX skin-width contact distance around 0.13 m that is unrelated to PursuitStandoff
                    // and that no value of it could ever satisfy the 0.4 m floor against. That is Max
                    // choosing to walk into the shed's body, not the shed failing to keep its distance, so
                    // this stops advancing at a modest margin above the floor rather than manufacturing an
                    // unrelated collision failure.
                    Vector3 planarVel = Vector3.zero;
                    if (i >= idleTicks)
                    {
                        float shedRadiusNow = WorldCapsuleRadius(shedCc, shedGo.transform);
                        float maxRadiusNow = WorldCapsuleRadius(maxCc, maxGo.transform);
                        Vector3 toShed = shedGo.transform.position - maxGo.transform.position;
                        toShed.y = 0f;
                        float surfaceGapNow = toShed.magnitude - shedRadiusNow - maxRadiusNow;
                        if (surfaceGapNow > 0.6f && toShed.sqrMagnitude > 1e-6f)
                            planarVel = toShed.normalized * player.WalkSpeed;
                    }

                    object split = ComputeSplitMotion.Invoke(player, new object[] { planarVel, dt });
                    var splitType = split.GetType();
                    var horizontal = (Vector3)splitType.GetField("Item1").GetValue(split);
                    var vertical = (Vector3)splitType.GetField("Item2").GetValue(split);
                    CharacterControllerMotion.SafeMove(maxCc, horizontal);
                    CharacterControllerMotion.SafeMove(maxCc, vertical);

                    Physics.SyncTransforms();

                    Vector3 maxPos = maxGo.transform.position;
                    Vector3 shedPos = shedGo.transform.position;

                    Assert.IsTrue(IsFinite(maxPos), $"tick {i} (dt={dt}): Max's position went non-finite: {maxPos}");
                    Assert.IsTrue(IsFinite(shedPos), $"tick {i} (dt={dt}): the shed's position went non-finite: {shedPos}");

                    Assert.GreaterOrEqual(maxPos.y, -0.1f,
                        $"tick {i} (dt={dt}): Max fell below the floor -- y={maxPos.y:F3}");

                    float shedRadius = WorldCapsuleRadius(shedCc, shedGo.transform);
                    float maxRadius = WorldCapsuleRadius(maxCc, maxGo.transform);
                    Vector3 planarDiff = shedPos - maxPos; planarDiff.y = 0f;
                    float surfaceDistance = planarDiff.magnitude - shedRadius - maxRadius;

                    Assert.GreaterOrEqual(surfaceDistance, 0.4f,
                        $"tick {i} (dt={dt}): shed-Max capsule surface distance {surfaceDistance:F3}m is under " +
                        $"the 0.4m floor (planar centre distance {planarDiff.magnitude:F3}m, shed radius " +
                        $"{shedRadius:F3}m, Max radius {maxRadius:F3}m)");
                }

                // Every shed-spawned robot position must be finite too. No robot ever spawns in this
                // harness (ConfigureAreaComposition/Update are never driven), so this holds vacuously --
                // recorded rather than assumed, per AC1's own third bullet.
                var spawner = shedGo.GetComponent<EnemySpawner>();
                Assert.IsNotNull(spawner, "a23's shed carries no EnemySpawner");
                Assert.AreEqual(0, spawner.LiveCount, "test precondition: this harness never drives a spawn");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                Object.DestroyImmediate(root);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
