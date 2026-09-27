using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-983 — Lee's TestFlight v0.10.0 observation (World 1, 2026-09-27): some robots took no damage,
    /// Max's hose passing straight through them. Root cause: MV-966 (0d3923f) stripped every robot's
    /// stray primitive collider, leaving its <see cref="CharacterController"/> as its ONLY collider;
    /// MV-980 (d7cf08d) then disabled that controller for the whole time a robot is Dormant, to drop it
    /// out of <see cref="GroundAnchorVfx"/>'s OverlapSphere discovery. A disabled collider is invisible
    /// to EVERY physics query, not just that one — <see cref="WaterBlaster"/>'s and
    /// <see cref="PlayerRocket"/>'s splash could never find a sleeping robot either, so it could never
    /// take damage or be woken by one.
    ///
    /// Fires through the real <c>WaterBlaster.FireTick</c> and <c>PlayerRocket</c> splash paths against a
    /// real dormant garrison — World 1 a5's own concealed knot, the same sparing mechanism
    /// <see cref="MV363DormantRobotTests.FillArea_SparesAConcealedKnot_WhenTheRoomIsBigEnough"/> already
    /// proves — rather than a synthetic robot: this is a regression in what a REAL garrison-placed
    /// robot's collider is left in, not in the state machine <c>MV980DormantZeroCostTests</c> covers.
    ///
    /// Must fail on the commit before this ticket (d7cf08d, MV-980's own disable line still in
    /// <c>BeginDormant</c>): the target robot's <see cref="CharacterController"/> is disabled the moment
    /// it is placed Dormant, so <c>Physics.OverlapSphereNonAlloc</c> never returns it to either weapon's
    /// hit test — see the fix comment for the quoted pre-fix failure.
    ///
    /// Tier 2 (resolved values) throughout: robot health actually deducted, <c>Current</c> actually left
    /// Dormant, and <see cref="GroundAnchorVfx"/>'s own per-actor slot table (never a rendered pixel)
    /// proving it still skips a still-Dormant robot post-fix — now via <see cref="IDormant"/> instead of
    /// the removed disabled-collider side effect.
    /// </summary>
    public sealed class MV983DormantRobotsTakeDamageTests
    {
        private static readonly MethodInfo FireTickMethod =
            typeof(WaterBlaster).GetMethod("FireTick", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BlasterAwakeMethod =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo AnchorLateUpdateMethod =
            typeof(GroundAnchorVfx).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo AnchorSlotsField =
            typeof(GroundAnchorVfx).GetField("_slots", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _directorGo;
        private GameObject _blasterGo;
        private GameObject _anchorGo;
        private GameObject _decoyGo;

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            GameObject bodies = GameObject.Find("Area Robots");
            if (bodies != null) Object.DestroyImmediate(bodies);
            if (_directorGo != null) Object.DestroyImmediate(_directorGo);
            if (_blasterGo != null) Object.DestroyImmediate(_blasterGo);
            if (_anchorGo != null) Object.DestroyImmediate(_anchorGo);
            if (_decoyGo != null) Object.DestroyImmediate(_decoyGo);
            PlayerRocket.DestroyAllActive();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void ADormantGarrisonRobot_TakesWaterAndSplashDamage_AndWakes_WhileAnchorVfxStillSkipsIt()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World1);
            Assert.IsNotNull(cfg, "World 1's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            _directorGo = new GameObject("MV983 Area Director");
            var director = _directorGo.AddComponent<AreaAccumulationDirector>();
            director.ConfigureWorld(cfg);
            director.Configure(map, System.Array.Empty<CoverPiece>()); // seeds area 1

            for (int i = 2; i <= 5; i++) director.EnterArea(i); // walk to a5, same as MV966ParkByReachTests
            director.SetCurrentArea(5); // Max's own physical area, per the ticket's own scenario

            var dormant = new List<RobotEnemy>();
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (r.AreaIndex == 5 && r.IsDormant) dormant.Add(r);

            Assert.GreaterOrEqual(dormant.Count, 2,
                "setup failure: World 1's a5 must spare at least a 2-robot concealed knot (same mechanism " +
                "MV363DormantRobotTests.FillArea_SparesAConcealedKnot_WhenTheRoomIsBigEnough proves) for " +
                "this test to mean anything");

            RobotEnemy waterTarget = dormant[0];
            RobotEnemy splashBystander = dormant[1];

            // ---- GroundAnchorVfx must never place a ring/shadow for a still-Dormant robot -------------
            _anchorGo = new GameObject("MV983 Anchor");
            var anchor = _anchorGo.AddComponent<GroundAnchorVfx>();
            CharacterController waterTargetCc = waterTarget.GetComponent<CharacterController>();
            Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)
            AnchorLateUpdateMethod.Invoke(anchor, null);
            var slots = (IDictionary)AnchorSlotsField.GetValue(anchor);
            Assert.IsFalse(slots.Contains(waterTargetCc),
                "MV-983: GroundAnchorVfx must skip a still-Dormant robot's ring/shadow via IDormant, not " +
                "by relying on its CharacterController being disabled");

            // ---- WaterBlaster's real FireTick hit path must find and damage a Dormant robot -----------
            float waterHealthBefore = waterTarget.HealthCurrent;
            Assert.Greater(waterHealthBefore, 0f, "setup failure: the target must start alive");

            Vector3 targetPos = waterTarget.transform.position;
            Vector3 blasterPos = targetPos + new Vector3(0f, 0f, -2f);
            _blasterGo = new GameObject("MV983 Water Blaster");
            _blasterGo.transform.position = blasterPos;
            _blasterGo.transform.rotation = Quaternion.LookRotation((targetPos - blasterPos).normalized, Vector3.up);
            var blaster = _blasterGo.AddComponent<WaterBlaster>();
            BlasterAwakeMethod.Invoke(blaster, null); // AddComponent doesn't reliably run Awake outside Play mode
            Physics.SyncTransforms();

            // 0.5s of stream at the authored 0.1s tick interval (WaterBlaster.FireInterval).
            for (int i = 0; i < 5; i++) FireTickMethod.Invoke(blaster, null);

            Assert.Less(waterTarget.HealthCurrent, waterHealthBefore,
                "MV-983: Max's water blaster passed straight through a Dormant robot — its own " +
                "CharacterController (its only collider since MV-966) must stay enabled while asleep so " +
                "OverlapSphereNonAlloc can still find it");
            Assert.AreNotEqual(RobotEnemy.State.Dormant, waterTarget.Current,
                "MV-983: taking damage must wake a Dormant robot, same as it always has for an awake one");

            // ---- PlayerRocket's real splash path must catch a Dormant bystander, too ------------------
            float splashHealthBefore = splashBystander.HealthCurrent;
            Assert.Greater(splashHealthBefore, 0f, "setup failure: the bystander must start alive");
            Assert.AreEqual(RobotEnemy.State.Dormant, splashBystander.Current,
                "setup failure: nothing above must have already woken the splash bystander");

            // A decoy awake target right next to the still-Dormant bystander: RobotEnemy.IsEngageable
            // (and PlayerRocket.RetargetIfLost) never let a rocket lock ON a Dormant robot directly — a
            // Dormant robot is correctly never a legitimate auto-target, and that is not this ticket's to
            // change. This proves the ticket's own named case instead: a splash from a nearby hit
            // catching a sleeping bystander standing right next to it.
            _decoyGo = new GameObject("MV983 Rocket Decoy");
            _decoyGo.AddComponent<CharacterController>();
            RobotEnemy decoy = _decoyGo.AddComponent<RobotEnemy>();
            decoy.Apply(EnemyArchetype.Rusher);
            decoy.transform.position = splashBystander.transform.position + new Vector3(0.3f, 0f, 0f);
            Physics.SyncTransforms();

            Vector3 rocketOrigin = decoy.transform.position + new Vector3(0f, 0f, -8f);
            PlayerRocket rocket = PlayerRocket.Fire(rocketOrigin, decoy.transform, speed: 14f,
                damage: 40f, splashRadius: 3f, cluster: false, launchYawRight: true);

            bool detonated = false;
            for (int i = 0; i < 300 && !detonated; i++) // 300 * 1/60f = 5s simulated, past the 4s fuel budget
            {
                rocket.Tick(1f / 60f);
                if (rocket == null) detonated = true;
            }
            Assert.IsTrue(detonated, "setup failure: the rocket never detonated near the decoy");

            Assert.Less(splashBystander.HealthCurrent, splashHealthBefore,
                "MV-983: a rocket's splash passed straight through a Dormant bystander standing right " +
                "next to the hit — its disabled CharacterController must have been invisible to " +
                "ApplySplashDamage's OverlapSphereNonAlloc");
        }
    }
}
