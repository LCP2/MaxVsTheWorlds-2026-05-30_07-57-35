using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-696 (the ticket's own AC1): a Sludgequeen at 100% HP in a synthetic 44x44 arena floods only
    /// the south half (z &lt; 22); once damaged to 49% and the 3 s phase-2 tell elapses, the whole floor
    /// floods except the map-authored dry zones (deck islands / centre block); and standing on flooded
    /// floor for 1 s costs exactly <see cref="SludgequeenTuning.FloodDamagePerSecond"/> (4) damage.
    ///
    /// Fails on the MV-705 merge commit (91e2160), the base commit MV-696 depends on: at that commit
    /// <c>SludgequeenBoss</c>/<c>SludgequeenTuning</c> do not exist, so this file fails to COMPILE
    /// (CS0246) before a single assertion runs — same "compile failure is the proof" shape
    /// <c>MV705SludgeDroneTests</c>'s own doc comment already used for its base commit.
    ///
    /// EditMode only, reflection-driven (repo convention — <c>Awake</c>/<c>Update</c> never run outside
    /// Play mode): <c>Wake</c> and <c>TickPhaseTwoTell</c> are invoked directly instead of ticking a
    /// live wake-area/Update loop, the same idiom <c>MV572BossAreaWakeTests</c> uses for
    /// <c>BigBermudaBoss</c>.
    /// </summary>
    public sealed class MV696SludgequeenFloodTests
    {
        private GameObject _playerGo;
        private GameObject _bossGo;

        private sealed class FakeReceiver : MonoBehaviour, IDamageable
        {
            public float Health = 200f;
            public float TotalDamageTaken;
            public bool IsAlive => Health > 0f;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info)
            {
                if (!DamageRules.Applies(info.Attacker, Team)) return;
                TotalDamageTaken += info.Amount;
                Health -= info.Amount;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player") { tag = "Player" };
            SludgequeenBoss.ResetRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_bossGo != null) Object.DestroyImmediate(_bossGo);
            Object.DestroyImmediate(_playerGo);
            SludgequeenBoss.ResetRegistry();
        }

        private static SludgequeenBoss NewWokenBoss(Rect arenaBounds, Rect[] dryZones)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            var boss = go.AddComponent<SludgequeenBoss>();

            // EditMode never calls Awake on a plain MonoBehaviour -- invoke it explicitly, same idiom as
            // MV572BossAreaWakeTests.NewBoss for BigBermudaBoss.
            typeof(SludgequeenBoss).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

            boss.SetArenaBounds(arenaBounds);
            boss.SetDryZones(dryZones);

            // Skip the wake-area/Update dance entirely -- Wake() itself is what this AC is about.
            typeof(SludgequeenBoss).GetMethod("Wake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);

            return boss;
        }

        private static void InvokeTickPhaseTwoTell(SludgequeenBoss boss, float dt) =>
            typeof(SludgequeenBoss).GetMethod("TickPhaseTwoTell", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, new object[] { dt });

        [Test]
        public void Flood_CoversSouthHalfAt100Percent_ThenWholeFloorExceptDryZonesAfterThePhaseTwoTell()
        {
            var arenaBounds = new Rect(0f, 0f, 44f, 44f);            // x:[0,44], z:[0,44]
            var island = new Rect(19f, 30f, 6f, 6f);                 // a deck island, well into the north half
            var islandCentre = new Vector3(22f, 0f, 33f);
            var floorPoint = new Vector3(22f, 0f, 10f);              // plain south-half floor, no dry zone

            SludgequeenBoss boss = NewWokenBoss(arenaBounds, new[] { island });
            _bossGo = boss.gameObject;

            // 100% HP: south half only (z < 22).
            Assert.AreEqual(new Rect(0f, 0f, 44f, 22f), boss.FloodRect,
                "at 100% HP the flood must cover exactly the south half of the arena");
            Assert.IsFalse(boss.IsPhaseTwo, "must not be phase 2 yet at full HP");

            // Damage to 49% HP -- crosses the 50% threshold, arms the phase-2 tell, but the flood has not
            // landed yet.
            boss.TakeDamage(new DamageInfo(SludgequeenTuning.Health * 0.51f, boss.transform.position, Vector3.forward, Team.Player));
            Assert.IsFalse(boss.IsPhaseTwo, "the phase-2 flood must not land before the tell elapses");
            Assert.AreEqual(new Rect(0f, 0f, 44f, 22f), boss.FloodRect,
                "the flood must still be the south half while the tell is counting down");

            // Elapse the 3 s tell.
            InvokeTickPhaseTwoTell(boss, 3.1f);
            Assert.IsTrue(boss.IsPhaseTwo, "the tell must have elapsed and landed the full flood");
            Assert.AreEqual(arenaBounds, boss.FloodRect, "phase 2 must flood the whole arena floor");
            Assert.IsTrue(boss.IsDry(islandCentre), "a deck island must stay dry even under phase 2's full flood");
            Assert.IsFalse(boss.IsDry(floorPoint), "plain floor must be wet under phase 2's full flood");

            // A 1 s probe on the (wet) floor must take exactly 4 flood damage.
            var receiverGo = new GameObject("FloodProbe");
            try
            {
                var receiver = receiverGo.AddComponent<FakeReceiver>();
                boss.TickFloodDamage(1f, floorPoint, receiver);
                Assert.AreEqual(SludgequeenTuning.FloodDamagePerSecond, receiver.TotalDamageTaken, 0.001f,
                    "a probe standing on flooded floor for 1 s must record exactly the flood's per-second damage");
            }
            finally
            {
                Object.DestroyImmediate(receiverGo);
            }
        }
    }
}
