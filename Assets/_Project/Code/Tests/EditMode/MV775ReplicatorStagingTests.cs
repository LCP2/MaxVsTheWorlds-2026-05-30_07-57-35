using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-775 — the Replicator's Lure/Intake/Cycle/Output/Death theatre. Fails to COMPILE on the
    /// pre-MV-775 commit (89f1a75): <c>Replicator.HatchPosition</c>/<c>IntakeSeconds</c>/
    /// <c>CycleSeconds</c>/<c>EmitStaggerSeconds</c> don't exist there at all — same "no such member"
    /// base-commit failure <c>ReplicatorTests</c>' own MV-706 doc comment names for its base commit.
    /// On that commit a consumed robot was despawned the instant it touched ANY face of the box (via
    /// <c>DistanceToSurface</c>'s whole-collider <c>ClosestPoint</c>) and the doubled pair was spawned
    /// with <c>SpawnExact(kind, 2, ...)</c> in a single call — both robots in the same frame, and a
    /// robot with a real collider radius removed well outside 0.35 m of the hatch.
    ///
    /// Tier 2 (resolved values): every assertion below reads a resolved Transform position, a live
    /// spawner count, a resolved shader property block colour, or a live GameObject/component list —
    /// never an authored constant asserted back at itself.
    /// </summary>
    public sealed class MV775ReplicatorStagingTests
    {
        // Same "distinctive far-off origin" idiom every other Replicator EditMode test uses.
        private static readonly Vector3 RigOrigin = new Vector3(50211f, 0f, -67744f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, value);

        private GameObject _playerGo;
        private GameObject _replicatorGo;
        private GameObject _rusherGo;
        private GameObject _huskGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOrigin + new Vector3(0f, 0f, 20f);
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
            if (_rusherGo != null) Object.DestroyImmediate(_rusherGo);
            if (_huskGo != null) Object.DestroyImmediate(_huskGo);
        }

        private RobotEnemy NewRusher(Vector3 position)
        {
            _rusherGo = new GameObject("Rusher");
            var cc = _rusherGo.AddComponent<CharacterController>();
            var e = _rusherGo.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            // OnEnable never runs as a side effect of AddComponent outside Play mode — same quirk
            // every other Replicator/RobotEnemy EditMode test in this suite works around.
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        [Test]
        public void ReplicatorTheatre_DrawsRobotToHatch_StaggersTheEmergedPair_LitsTheGlow_StandsAHusk_NeverAnimator()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error] every Replicator test carries

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f);
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build();
            replicator.Configure(1);
            Set(_replicatorGo.GetComponent<EnemySpawner>(), "startingRobots", 4);

            // --- No Animator anywhere under this box — the ticket's own "if this seems to need one,
            // it does not" (AnimSequence/direct Transform drives everything here). ---
            Assert.AreEqual(0, _replicatorGo.GetComponentsInChildren<Animator>(true).Length,
                "a Replicator must never carry an Animator anywhere under it");

            // --- A husk watcher bound while the body is still standing (same order FactoryHusk's own
            // static Install uses at a real scene load), so it measures real bounds before death. ---
            _huskGo = new GameObject("MV775HuskWatcher");
            var husk = _huskGo.AddComponent<FactoryHusk>();
            husk.Bind(replicator);
            MethodInfo huskAwake = typeof(FactoryHusk).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            huskAwake.Invoke(husk, null);

            RobotEnemy rusher = NewRusher(RigOrigin + new Vector3(5f, 0f, 0f));
            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, rusher.Current,
                "within lure radius, capacity > 0, clear of Max's melee exclusion — this Rusher must be lured");

            // --- Lure: hatch glow active with one robot seeking and nothing pending. ---
            MethodInfo lateUpdate = typeof(Replicator).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            lateUpdate.Invoke(replicator, null);
            var hatchGlowRenderer = (Renderer)typeof(Replicator)
                .GetField("_hatchGlow", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(replicator);
            var glowMpb = new MaterialPropertyBlock();
            hatchGlowRenderer.GetPropertyBlock(glowMpb);
            Assert.Greater(glowMpb.GetColor("_BaseColor").a, 0f,
                "with one robot seeking and nothing pending, the hatch glow must be lit");

            // --- Intake: place the Rusher exactly at its arrive-gate boundary (ArriveTolerance +
            // its own 0.4 m collider radius from the hatch) — a resolved position well outside 0.35 m
            // of the hatch itself, so despawning it there (the pre-MV-775 behaviour) would fail the
            // very next assertion. Ticking past IntakeSeconds must draw it the rest of the way in. ---
            float robotRadius = EnemyArchetype.Of(EnemyKind.Rusher).ColliderRadius;
            rusher.transform.position = replicator.HatchPosition + new Vector3(Replicator.ArriveTolerance + robotRadius, 0f, 0f);
            replicator.TickConsumption(Replicator.IntakeSeconds + 0.01f);

            Assert.IsFalse(rusher.IsAlive, "the Rusher must be despawned once fully drawn into the hatch");
            Assert.LessOrEqual(Vector3.Distance(rusher.transform.position, replicator.HatchPosition), 0.35f,
                "a consumed robot's resolved position at the moment of removal must be within 0.35 m of " +
                "the hatch face — not merely wherever the (wider) arrive gate first let it through");

            var spawner = _replicatorGo.GetComponent<EnemySpawner>();
            Assert.AreEqual(0, spawner.LiveCountOf(EnemyKind.Rusher),
                "the Cycle beat (3 s) hasn't elapsed yet — nothing should have emerged");

            // --- Cycle -> Output: the doubled pair emerges staggered, never in the same tick. ---
            replicator.TickConsumption(2.5f); // total ~3.01 s: past CycleSeconds — first robot emerges
            Assert.AreEqual(1, spawner.LiveCountOf(EnemyKind.Rusher),
                "the FIRST of the doubled pair must emerge once the Cycle beat completes");

            replicator.TickConsumption(0.2f); // total ~3.21 s: short of CycleSeconds + EmitStaggerSeconds
            Assert.AreEqual(1, spawner.LiveCountOf(EnemyKind.Rusher),
                "the second robot must not emerge in the same beat as the first — the two emergences " +
                "must be measurably distinct in time, not simultaneous");

            replicator.TickConsumption(0.25f); // total ~3.46 s: past CycleSeconds + EmitStaggerSeconds (0.4 >= 0.3)
            Assert.AreEqual(2, spawner.LiveCountOf(EnemyKind.Rusher),
                "the SECOND of the doubled pair must emerge once the stagger has elapsed");
            Assert.AreEqual(0, replicator.Capacity, "one doubling must spend the box's only capacity");

            // --- Death: the drum's own renderer is disabled and a husk stands in its place. ---
            replicator.TakeDamage(new DamageInfo(99999f, replicator.transform.position, Vector3.forward, Team.Player));
            Assert.IsFalse(replicator.IsAlive, "lethal damage from the Player team must destroy the box");
            Transform body = _replicatorGo.transform.Find("Body");
            Assert.IsNotNull(body, "the generated Body container must still exist to be hidden");
            Assert.IsFalse(body.gameObject.activeSelf, "the drum's own generated body must be hidden on destruction");

            MethodInfo huskUpdate = typeof(FactoryHusk).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            huskUpdate.Invoke(husk, null);
            Assert.IsNotNull(_huskGo.transform.Find("Husk"), "a husk object must stand in the dead box's place");
        }
    }
}
