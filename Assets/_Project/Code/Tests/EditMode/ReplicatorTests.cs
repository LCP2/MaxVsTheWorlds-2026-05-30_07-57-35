using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-706 — the Replicator's lure/consume/double loop: a robot pulled toward the hatch, deactivated
    /// on arrival, and doubled 1.2 s later into a pair carrying <c>NoReplicate</c>, spending exactly one
    /// unit of the box's authored capacity. Fails on the MV-687 merge commit (0a80ab1) — no
    /// <c>Replicator</c> type exists there at all. Tier 2 (resolved values): asserts the resolved
    /// steering target, live counts, the <c>NoReplicate</c> tag and the resolved capacity — never an
    /// authored constant.
    /// </summary>
    public sealed class ReplicatorTests
    {
        // Same "distinctive far-off origin" idiom MV548MobileShedTests/MV618HutchPaceAndDamageTests use
        // — EditMode tests share one physics scene for the whole cc-verify run with no per-test reset.
        private static readonly Vector3 RigOrigin = new Vector3(-88123f, 0f, 41207f);

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        // EnemySpawner.EffectiveMaxLiveEnemies ramps from startingRobots (authored 0) up to
        // maxLiveEnemies as DifficultyDirector.Normalized climbs from whatever a PRIOR test in this
        // shared EditMode run left DifficultyDirector.Elapsed at — never assumed 0 here. Forcing
        // startingRobots up directly (same reflection idiom EnemySpawnerTests.Set already uses) is what
        // guarantees this box's own spawner has room for the two Rushers it doubles into, independent of
        // that shared static state.
        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
             .SetValue(o, value);

        private GameObject _playerGo;
        private GameObject _replicatorGo;
        private GameObject _rusherGo;
        private GameObject _bruteGo;

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _playerGo.transform.position = RigOrigin + new Vector3(0f, 0f, 20f); // Max 20 m away
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_replicatorGo != null) Object.DestroyImmediate(_replicatorGo);
            if (_rusherGo != null) Object.DestroyImmediate(_rusherGo);
            if (_bruteGo != null) Object.DestroyImmediate(_bruteGo);
        }

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private RobotEnemy NewRusher(Vector3 position)
        {
            _rusherGo = new GameObject("Rusher");
            var cc = _rusherGo.AddComponent<CharacterController>();
            var e = _rusherGo.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            // OnEnable never runs as a side effect of AddComponent outside Play mode (same quirk
            // MowerHutch.Build's own doc comment names) — Replicator.TickLure reads RobotEnemy.Active,
            // which OnEnable is what populates, so it's invoked directly here.
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        private RobotEnemy NewBrute(Vector3 position)
        {
            _bruteGo = new GameObject("Brute");
            var cc = _bruteGo.AddComponent<CharacterController>();
            var e = _bruteGo.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Brute));
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        /// <summary>
        /// MV-756 Cause 1 — on `main` the arrive test is a flat 1.2 m CENTRE-TO-CENTRE radius
        /// (<c>Replicator.ArriveRadius</c>) against a box whose collider half-extent is 1.0 m. A
        /// Brute's own 0.6 m <c>CharacterController</c> radius means the closest its CENTRE can ever
        /// physically get, pressed flat against the box's face, is 1.6 m — always outside that 1.2 m
        /// gate, so a robot that has walked all the way to the hatch is never consumed. Fails on
        /// current `main`: with the Brute placed at exactly that 1.6 m contact distance,
        /// `TickConsumption` never satisfies `Vector3.Distance(...) <= ArriveRadius` (1.6 > 1.2), so
        /// `spawner.LiveCountOf(EnemyKind.Brute)` stays 0, not 2. Tier 2 (resolved values): asserts
        /// the resolved live count after ticking consumption, never an authored constant.
        ///
        /// MV-775 update: the arrive gate now reads distance to the hatch FACE specifically (not any
        /// face of the box), so the Brute is placed touching the hatch rather than an arbitrary side,
        /// and the tick advances through the Intake+Cycle+stagger beats (not the old flat
        /// ConsumeSeconds) for the doubled pair to fully emerge.
        /// </summary>
        [Test]
        public void MV_RobotAtTheBoxIsConsumed()
        {
            LogAssert.ignoreFailingMessages = true; // same BuildBody collider-strip [Error], see below

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            _replicatorGo.transform.localScale = new Vector3(2f, 1.5f, 2f); // the ticket's authored 2x1.5x2 footprint
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build();
            replicator.Configure(1); // capacity 1
            Set(_replicatorGo.GetComponent<EnemySpawner>(), "startingRobots", 4); // room for the doubled pair

            RobotEnemy brute = NewBrute(RigOrigin + new Vector3(5f, 0f, 0f)); // 5 m from the box, within lure radius

            replicator.TickLure();
            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, brute.Current,
                "within the 8 m lure radius, capacity > 0, and clear of the 4 m Max-melee exclusion — " +
                "this Brute must be lured off Max");

            // Touching the hatch face, face-on: this Brute's own 0.6 m CharacterController radius is
            // the closest a real SafeMove-driven robot could ever get to the exact hatch point.
            float robotRadius = EnemyArchetype.Of(EnemyKind.Brute).ColliderRadius;
            brute.transform.position = replicator.HatchPosition + new Vector3(0f, 0f, -robotRadius);
            replicator.TickConsumption(Replicator.IntakeSeconds + Replicator.CycleSeconds + Replicator.EmitStaggerSeconds + 0.01f);

            var spawner = _replicatorGo.GetComponent<EnemySpawner>();
            Assert.AreEqual(2, spawner.LiveCountOf(EnemyKind.Brute),
                "a robot physically touching the hatch face must be consumed and doubled — the " +
                "arrive test has to read the hatch's own position, not a flat centre-to-centre " +
                "radius that a real controller radius can never satisfy");
        }

        [Test]
        public void Replicator_LuresConsumesAndDoublesARobot_ThenRefusesTheSpentPair()
        {
            // Replicator.BuildBody destroys the LED primitive's stock collider via Object.Destroy,
            // which is edit-mode-illegal and logs an [Error] regardless — same shape MowerHutch's own
            // BuildCore carries (MV-464's own doc comment on that method).
            LogAssert.ignoreFailingMessages = true;

            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build();       // AddComponent's own Awake never runs outside Play mode
            replicator.Configure(1);  // capacity 1
            Set(_replicatorGo.GetComponent<EnemySpawner>(), "startingRobots", 4); // room for the pair, see field doc above

            RobotEnemy rusher = NewRusher(RigOrigin + new Vector3(5f, 0f, 0f)); // 5 m from the box

            // --- Step the lure ---
            replicator.TickLure();

            Assert.AreEqual(RobotEnemy.State.ReplicatorSeeking, rusher.Current,
                "within the 8 m lure radius, capacity > 0, and clear of the 4 m Max-melee exclusion — " +
                "this Rusher must be lured off Max");
            Assert.AreEqual(replicator.HatchPosition, rusher.ReplicatorSeekTarget,
                "the lured robot's steering target must be the Replicator's own hatch face, not the box's centre (MV-775)");

            // --- Move it to the hatch, advance through Intake+Cycle+stagger: consumed, then the doubled pair emerges ---
            rusher.transform.position = replicator.HatchPosition;
            replicator.TickConsumption(Replicator.IntakeSeconds + Replicator.CycleSeconds + Replicator.EmitStaggerSeconds + 0.01f);

            var spawner = _replicatorGo.GetComponent<EnemySpawner>();
            Assert.AreEqual(2, spawner.LiveCountOf(EnemyKind.Rusher),
                "exactly two live Rushers must exist after one consumption at capacity 1");
            Assert.AreEqual(0, replicator.Capacity, "one doubling must spend the box's only capacity");

            // The two doubled Rushers are read back off the spawner's own live list, not
            // RobotEnemy.Active — SpawnKind's SetActive(true) never runs OnEnable outside Play mode
            // (same quirk NewRusher's own doc comment names), so Active never sees them here even
            // though LiveCountOf (populated imperatively by SpawnKind itself) already proves they exist.
            var live = (System.Collections.IList)typeof(EnemySpawner)
                .GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(spawner);
            RobotEnemy twinA = null, twinB = null;
            foreach (RobotEnemy r in live)
            {
                if (r == rusher || r.Kind != EnemyKind.Rusher) continue;
                if (twinA == null) twinA = r; else twinB = r;
            }
            Assert.IsNotNull(twinA, "the first doubled Rusher must be live and findable");
            Assert.IsNotNull(twinB, "the second doubled Rusher must be live and findable");
            Assert.IsTrue(twinA.NoReplicate, "a freshly doubled Rusher must carry NoReplicate");
            Assert.IsTrue(twinB.NoReplicate, "a freshly doubled Rusher must carry NoReplicate");

            // --- Move one of them back to the hatch, advance 5 s: spent (capacity 0) — count stays two ---
            twinA.transform.position = RigOrigin;
            replicator.TickLure();
            replicator.TickConsumption(5f);

            Assert.AreEqual(2, spawner.LiveCountOf(EnemyKind.Rusher),
                "spent at capacity 0, the Replicator lures nothing further — the count must stay two");
        }

        /// <summary>
        /// MV-693 — the primitive-cube body MV-706 shipped with is replaced by a generated mesh
        /// (see <c>FactoryBodies.BuildReplicator</c>). Fails on the MV-706 merge commit (0ee9d99):
        /// there is no "Body/Hull" child at all (the box was only ever the bare primitive cube
        /// itself), and the primitive cube's own renderer is never switched off. Tier 2 (resolved
        /// values): the hull's WORLD bounds after the metre-space container's scale-cancel — never
        /// an authored constant, since nothing in this test asserts a serialized field back at
        /// itself.
        /// </summary>
        [Test]
        public void Replicator_BuildsGeneratedHullMesh_MatchingAuthoredBoxFootprint()
        {
            _replicatorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _replicatorGo.name = "Replicator";
            _replicatorGo.transform.position = RigOrigin;
            _replicatorGo.transform.localScale = new Vector3(2f, 2f, 1.5f); // MV-706's own authored footprint
            var replicator = _replicatorGo.AddComponent<Replicator>();
            replicator.Build(); // AddComponent's own Awake never runs outside Play mode

            Transform hull = _replicatorGo.transform.Find("Body/Hull");
            Assert.IsNotNull(hull, "Build() must generate a Hull part under a metre-space Body container");

            Bounds resolvedBounds = hull.GetComponent<Renderer>().bounds;
            Assert.AreEqual(2f, resolvedBounds.size.x, 0.01f,
                "the generated hull's resolved world width must match the box's own authored footprint, " +
                "not whatever CharacterMeshes.Prism's own unit geometry happens to be before scaling");
            Assert.AreEqual(1.5f, resolvedBounds.size.z, 0.01f,
                "the generated hull's resolved world depth must match the box's own authored footprint");

            Assert.IsFalse(_replicatorGo.GetComponent<Renderer>().enabled,
                "the old primitive-cube renderer must be switched off once the generated body is built");
        }
    }
}
