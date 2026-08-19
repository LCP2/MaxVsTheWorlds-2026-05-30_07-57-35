using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-434: MV-428 removed the Bruiser/Heavy/Brute lunge, which exposed a pre-existing hole —
    /// nothing physical stops a robot from occupying Max's exact position
    /// (<c>EnemySpawner.LetThePlayerThrough</c>/<c>AreaAccumulationDirector.LetThePlayerThrough</c>,
    /// MV-321, both call <c>Physics.IgnoreCollision</c> on every spawn), and a melee kind with no
    /// stop distance walked straight into him and stayed there, spinning as its steering direction
    /// went numerically unstable at near-zero range.
    ///
    /// The fix is three parts, all covered here: a hard positional clamp
    /// (<see cref="EnemyBodySeparation"/>, tested on its own in <c>EnemyBodySeparationTests</c>) run
    /// after every <c>CharacterControllerMotion.SafeMove</c> in Chase/Lunge; a non-lunging kind
    /// stops applying forward movement once it's already at the clamp distance; and every re-aim
    /// turns at a capped rate instead of snapping via <c>Quaternion.LookRotation</c>.
    ///
    /// EditMode only, reflection-driven — same idiom as <c>MV428MeleeReadabilityTests</c>, which
    /// this ticket builds directly on top of (same fixture and player double).
    ///
    /// Must fail on <c>dadd9f1</c> — the commit before this ticket's fix, where
    /// <c>RobotEnemy.FaceAndMove</c> still snaps instantly via <c>Quaternion.LookRotation</c> and
    /// nothing clamps a robot's distance from Max at all.
    /// </summary>
    public sealed class MV434BodySeparationTests
    {
        private GameObject _playerGo;
        private FakePlayer _player;

        private sealed class FakePlayer : MonoBehaviour, IDamageable
        {
            public float Health = 200f;
            public int Hits;
            public bool IsAlive => Health > 0f;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info)
            {
                if (!DamageRules.Applies(info.Attacker, Team)) return;
                Hits++;
                Health -= info.Amount;
            }
        }

        [SetUp]
        public void SetUp()
        {
            DevTuning.Reset();
            RobotEnemy.ResetRegistry();
            LungeTokenPool.Reset();
            _playerGo = new GameObject("Player") { tag = "Player" };
            _player = _playerGo.AddComponent<FakePlayer>();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            LungeTokenPool.Reset();
            DevTuning.Reset();
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        // ------------------------------------------------------------------ helpers (same idiom as MV428MeleeReadabilityTests)

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewEnemy(EnemyArchetype archetype, Vector3 position)
        {
            var go = new GameObject($"Enemy {archetype.Kind}");
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            // MV-461: match EnemySpawner.CreateInstance's collider setup (no BodyScale divide needed
            // here — this fixture never scales the transform, so the archetype's metres are already
            // world metres). Without this the fixture kept CharacterController's un-configured
            // defaults (radius 0.5, height 2) instead of the archetype's real footprint (Rusher:
            // radius 0.4), an oversized capsule sweeping through geometry no production robot ever
            // has — the actual source of this test's non-determinism (see RushersLunge's own
            // comment), not anything about aim or formation bias.
            cc.height = archetype.ColliderHeight;
            cc.radius = archetype.ColliderRadius;
            cc.center = Vector3.zero;
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(archetype);
            return e;
        }

        private void GiveSight(RobotEnemy e) => e.Sight.Tick(true, _player.transform.position, 0.02f);

        private static void InvokeTickChase(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickChase", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickTelegraph(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickTelegraph", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void InvokeTickLunge(RobotEnemy e, float dt) =>
            typeof(RobotEnemy).GetMethod("TickLunge", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(e, new object[] { dt });

        private static void SetStateTimer(RobotEnemy e, float value) =>
            typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(e, value);

        private static float MinBodyDistance(EnemyArchetype a) =>
            EnemyBodySeparation.MinDistance(a.ColliderRadius, EnemyArchetype.PlayerRadius);

        // ------------------------------------------------------------------ AC1: never settles inside the clamp

        [Test]
        public void BruiserHeavyBrute_SpawnedExactlyOnMax_SettleAtBodySeparationDistance_NeverInside()
        {
            var kinds = new[] { EnemyArchetype.Bruiser, EnemyArchetype.Heavy, EnemyArchetype.Brute };
            foreach (var archetype in kinds)
            {
                var e = NewEnemy(archetype, _player.transform.position); // coincident with Max
                try
                {
                    GiveSight(e);
                    float minDist = MinBodyDistance(archetype);

                    for (int i = 0; i < 5; i++)
                    {
                        InvokeTickChase(e, 0.02f);
                        float dist = Vector3.Distance(e.transform.position, _player.transform.position);
                        Assert.GreaterOrEqual(dist, minDist - 1e-3f,
                            $"{archetype.Kind} must never end a tick inside its body-separation distance");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(e.gameObject);
                }
            }
        }

        // ------------------------------------------------------------------ AC3: stop closing, keep touching

        [Test]
        public void NonLungingKind_StopsClosingAtTheStandoff_ButKeepsTouchingAndFacing()
        {
            var bruiser = NewEnemy(EnemyArchetype.Bruiser, _player.transform.position);
            try
            {
                GiveSight(bruiser);

                // First tick: the clamp pushes it straight out to the standoff distance.
                InvokeTickChase(bruiser, 0.02f);
                float minDist = MinBodyDistance(EnemyArchetype.Bruiser);
                Assert.AreEqual(minDist,
                    Vector3.Distance(bruiser.transform.position, _player.transform.position), 1e-3f);

                Vector3 settled = bruiser.transform.position;

                // Several more ticks: it must not creep forward — Chase applies zero forward
                // translation once it's already at the standoff, not just a clamp-corrected crawl.
                for (int i = 0; i < 5; i++)
                {
                    InvokeTickChase(bruiser, 0.02f);
                    Assert.Less(Vector3.Distance(settled, bruiser.transform.position), 1e-4f,
                        "must not keep pressing forward once already at the body-separation distance");
                }

                // TickContactTouch must still be ticking underneath while it stands there — cross
                // the cooldown boundary (MV-428's own mechanism) and it still lands the hit.
                InvokeTickChase(bruiser, 1.0f);
                Assert.AreEqual(1, _player.Hits, "contact damage must still land while pressing at the standoff");
                Assert.AreEqual(200f - EnemyArchetype.Bruiser.TouchDamage, _player.Health, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(bruiser.gameObject);
            }
        }

        // ------------------------------------------------------------------ AC4: yaw rate is capped, any state

        [Test]
        public void Chase_NeverTurnsFasterThanTheCappedRate()
        {
            const float dt = 0.02f;
            float capThisTick = EnemyBodySeparation.DefaultMaxTurnDegreesPerSecond * dt;

            // Far enough out that this tick can't also reach the lunge range — isolates the turn.
            var rusher = NewEnemy(EnemyArchetype.Rusher, new Vector3(5f, 0f, 0f));
            try
            {
                GiveSight(rusher);
                Quaternion before = Quaternion.LookRotation(Vector3.back, Vector3.up); // 180 deg off Max
                rusher.transform.rotation = before;

                InvokeTickChase(rusher, dt);

                Assert.LessOrEqual(Quaternion.Angle(before, rusher.transform.rotation), capThisTick + 1e-2f,
                    "Chase must turn at a capped rate, not snap instantly — that snap is the spin this ticket fixes");
            }
            finally
            {
                Object.DestroyImmediate(rusher.gameObject);
            }
        }

        [Test]
        public void Telegraph_NeverTurnsFasterThanTheCappedRate()
        {
            const float dt = 0.02f;
            float capThisTick = EnemyBodySeparation.DefaultMaxTurnDegreesPerSecond * dt;

            var rusher = NewEnemy(EnemyArchetype.Rusher, new Vector3(1f, 0f, 0f));
            try
            {
                GiveSight(rusher);
                InvokeTickChase(rusher, dt);
                Assert.AreEqual(RobotEnemy.State.Telegraph, rusher.Current, "must have wound up first");

                Quaternion before = Quaternion.LookRotation(Vector3.back, Vector3.up);
                rusher.transform.rotation = before;

                InvokeTickTelegraph(rusher, dt);

                Assert.LessOrEqual(Quaternion.Angle(before, rusher.transform.rotation), capThisTick + 1e-2f,
                    "Telegraph's own re-aim must not snap either");
            }
            finally
            {
                Object.DestroyImmediate(rusher.gameObject);
            }
        }

        // ------------------------------------------------------------------ AC6: lungers still land their hit

        [Test]
        public void RushersLunge_StillReachesContactRange_AndStillDealsDamage_WithTheClampApplied()
        {
            var rusher = NewEnemy(EnemyArchetype.Rusher, new Vector3(1f, 0f, 0f));
            try
            {
                GiveSight(rusher);
                InvokeTickChase(rusher, 0.02f);
                Assert.AreEqual(RobotEnemy.State.Telegraph, rusher.Current);

                // MV-461: tick the wind-up the same way Update() does — one dt at a time, with
                // _stateTimer accumulating real ticks — instead of jumping _stateTimer straight to
                // telegraphTime. That old shortcut captured _lungeDir (= transform.forward) after
                // only ONE capped-rate rotation step (MV-434: 540 deg/s), aimed nowhere near the
                // player. In real play the full 0.55s wind-up gives ~27 ticks, plenty to converge —
                // this fixture now gets the same number instead of a single-tick shortcut, so the
                // dash is aimed the way a real Telegraph always leaves it.
                const float dt = 0.02f;
                float elapsed = 0f;
                while (elapsed < EnemyArchetype.Rusher.TelegraphTime)
                {
                    elapsed += dt;
                    SetStateTimer(rusher, elapsed);
                    InvokeTickTelegraph(rusher, dt);
                }
                Assert.AreEqual(RobotEnemy.State.Lunge, rusher.Current);

                InvokeTickLunge(rusher, dt); // dash, contact check, THEN the clamp

                Assert.AreEqual(1, _player.Hits, "the dash must still land its hit with the clamp now running right after it");
                Assert.AreEqual(200f - EnemyArchetype.Rusher.ContactDamage, _player.Health, 1e-3f);

                float minDist = MinBodyDistance(EnemyArchetype.Rusher);
                Assert.GreaterOrEqual(Vector3.Distance(rusher.transform.position, _player.transform.position),
                    minDist - 1e-3f, "the clamp must still have settled the lunge against Max's body, not inside it");
            }
            finally
            {
                Object.DestroyImmediate(rusher.gameObject);
            }
        }

        // ------------------------------------------------------------------ AC5: anti-pinning guarantee unregressed

        [Test]
        public void SpawnedRobot_StillIgnoresCollisionWithThePlayer_ViaEnemySpawner()
        {
            var enemyGo = new GameObject("Enemy");
            var enemyCc = enemyGo.AddComponent<CharacterController>();
            var spawnerGo = new GameObject("Spawner");
            var spawner = spawnerGo.AddComponent<MaxWorlds.Enemies.EnemySpawner>();
            try
            {
                // Not "?? AddComponent<>()" — GetComponent hands back Unity's "fake null" when
                // absent, which the CLR-level ?? operator does not treat as null (it only calls
                // Unity's overloaded == inside an explicit comparison), so it would silently keep
                // the fake-null reference instead of ever creating the real component.
                var playerCc = _playerGo.GetComponent<CharacterController>();
                if (playerCc == null) playerCc = _playerGo.AddComponent<CharacterController>();

                var method = typeof(MaxWorlds.Enemies.EnemySpawner).GetMethod(
                    "LetThePlayerThrough", BindingFlags.NonPublic | BindingFlags.Instance);
                method.Invoke(spawner, new object[] { enemyGo });

                Assert.IsTrue(Physics.GetIgnoreCollision(enemyCc, playerCc),
                    "MV-434 must not regress MV-321's anti-pinning guarantee — Max must remain able to walk out of a full encirclement");
            }
            finally
            {
                Object.DestroyImmediate(enemyGo);
                Object.DestroyImmediate(spawnerGo);
            }
        }

        [Test]
        public void SpawnedRobot_StillIgnoresCollisionWithThePlayer_ViaAreaAccumulationDirector()
        {
            var enemyGo = new GameObject("Enemy");
            var enemyCc = enemyGo.AddComponent<CharacterController>();
            var directorGo = new GameObject("Director");
            var director = directorGo.AddComponent<AreaAccumulationDirector>();
            try
            {
                var playerCc = _playerGo.GetComponent<CharacterController>();
                if (playerCc == null) playerCc = _playerGo.AddComponent<CharacterController>();

                var method = typeof(AreaAccumulationDirector).GetMethod(
                    "LetThePlayerThrough", BindingFlags.NonPublic | BindingFlags.Instance);
                method.Invoke(director, new object[] { enemyGo });

                Assert.IsTrue(Physics.GetIgnoreCollision(enemyCc, playerCc),
                    "MV-434 must not regress MV-321's anti-pinning guarantee — Max must remain able to walk out of a full encirclement");
            }
            finally
            {
                Object.DestroyImmediate(enemyGo);
                Object.DestroyImmediate(directorGo);
            }
        }
    }
}
