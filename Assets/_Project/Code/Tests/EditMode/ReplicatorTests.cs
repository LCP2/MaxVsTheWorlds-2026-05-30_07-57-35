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
            Assert.AreEqual(RigOrigin, rusher.ReplicatorSeekTarget,
                "the lured robot's steering target must be the Replicator's own hatch position, not Max");

            // --- Move it to the hatch, advance 1.2 s: consumed, then the doubled pair emerges ---
            rusher.transform.position = RigOrigin;
            replicator.TickConsumption(1.2f);

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
    }
}
