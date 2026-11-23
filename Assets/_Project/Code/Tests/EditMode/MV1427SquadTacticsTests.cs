using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1427 — City squads and posts. ONE test (MV-465 Rule 1), Tier 2 resolved values only, driven through
    /// the real spawner and each robot's public <see cref="RobotEnemy.Tick"/>. A squad of 2 rushers, 1 bruiser
    /// and 1 gunner (post): wake together, hold for the volley, bodyguard, hold the post, blink behind; and
    /// with squadTactics off the same robots behave as before. On the base commit
    /// <c>RobotEnemy.SetSquad</c> does not exist, so this fails to compile there.
    /// </summary>
    public sealed class MV1427SquadTacticsTests
    {
        private sealed class StubMax : MonoBehaviour, IDamageable
        {
            public bool IsAlive => true;
            public Team Team => Team.Player;
            public void TakeDamage(in DamageInfo info) { }
        }

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo TeleportTargetField =
            typeof(RobotEnemy).GetField("_teleportTarget", BindingFlags.NonPublic | BindingFlags.Instance);

        private const float Dt = 0.02f;
        private static readonly Vector3 Origin = new Vector3(-74123f, 0f, 56789f);

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
            CharacterControllerMotion.HasWarnedOversizedMove = true;
        }

        [TearDown]
        public void TearDown()
        {
            EnemyNavigation.Reset();
            RobotEnemy.ResetRegistry();
        }

        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        private static RobotEnemy Place(EnemySpawner sp, EnemyKind kind, Vector3 offset, string squad, bool post, bool on)
        {
            RobotEnemy r = sp.SpawnExact(kind, 1)[0];
            r.transform.position = Origin + offset + Vector3.up * EnemyArchetype.Of(kind).SpawnHeight;
            OnEnableMethod.Invoke(r, null);
            r.SetSquad(squad, post, on);
            r.BeginDormant(true);
            return r;
        }

        private static void Hit(RobotEnemy r) =>
            r.TakeDamage(new DamageInfo(1f, r.transform.position, Vector3.right, Team.Player));

        [Test]
        public void Squad_WakesTogether_HoldsForVolley_Bodyguards_HoldsPost_BlinksBehind_AndIsInertWhenOff()
        {
            GameObject groundGo = null, maxGo = null, spawnerGo = null;
            try
            {
                groundGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                groundGo.transform.position = new Vector3(Origin.x, -1f, Origin.z);
                groundGo.transform.localScale = new Vector3(200f, 2f, 200f);
                maxGo = new GameObject("MV1427 Max") { tag = "Player" };
                maxGo.AddComponent<StubMax>();
                maxGo.transform.position = Origin + new Vector3(6f, 0f, 0f);
                maxGo.transform.rotation = Quaternion.Euler(0f, 90f, 0f);   // facing +X
                spawnerGo = new GameObject("MV1427 Spawner");
                spawnerGo.transform.position = Origin;
                var sp = spawnerGo.AddComponent<EnemySpawner>();

                // ---- squadTactics ON: gunner (post) at the origin, Max 6 m east of it ----
                RobotEnemy gunner = Place(sp, EnemyKind.Gunner, Vector3.zero, "s1", true, true);
                RobotEnemy rusherA = Place(sp, EnemyKind.Rusher, new Vector3(-4f, 0f, 1.5f), "s1", false, true);
                RobotEnemy rusherB = Place(sp, EnemyKind.Rusher, new Vector3(-4f, 0f, -1.5f), "s1", false, true);
                RobotEnemy bruiser = Place(sp, EnemyKind.Bruiser, new Vector3(-3f, 0f, 3f), "s1", false, true);
                Physics.SyncTransforms();
                var all = new List<RobotEnemy> { gunner, rusherA, rusherB, bruiser };
                Vector3 postPos = gunner.transform.position;
                foreach (var r in all) Assert.AreEqual(RobotEnemy.State.Dormant, r.Current, "setup: all four asleep");

                // 1. Hitting the gunner wakes all four in the same call.
                Hit(gunner);
                foreach (var r in all)
                    Assert.AreNotEqual(RobotEnemy.State.Dormant, r.Current, $"{r.Kind} woke with the gunner");

                // 2/5. Until the gunner fires no rusher gets within 7.5 m of Max; the gunner never leaves its post.
                float minRusher = float.MaxValue, maxPostDrift = 0f;
                int fireTick = -1;
                for (int i = 0; i < 600 && fireTick < 0; i++)
                {
                    foreach (var r in all) r.Tick(Dt);
                    maxPostDrift = Mathf.Max(maxPostDrift, Flat(gunner.transform.position, postPos));
                    if (gunner.Current == RobotEnemy.State.Lunge) fireTick = i;
                    else
                    {
                        minRusher = Mathf.Min(minRusher, Flat(rusherA.transform.position, maxGo.transform.position));
                        minRusher = Mathf.Min(minRusher, Flat(rusherB.transform.position, maxGo.transform.position));
                    }
                }
                Assert.GreaterOrEqual(fireTick, 0, "the gunner fires within 12 s");
                Assert.GreaterOrEqual(minRusher, 7.5f, "no rusher closes inside 7.5 m before the volley");

                // 3. The tick after the gunner fires, both rushers are closing.
                float aBefore = Flat(rusherA.transform.position, maxGo.transform.position);
                float bBefore = Flat(rusherB.transform.position, maxGo.transform.position);
                foreach (var r in all) r.Tick(Dt);
                Assert.Less(Flat(rusherA.transform.position, maxGo.transform.position), aBefore, "rusher A closing after the volley");
                Assert.Less(Flat(rusherB.transform.position, maxGo.transform.position), bBefore, "rusher B closing after the volley");

                // 4/5. Max is 6 m from the gunner: the bruiser settles on the bodyguard point; gunner holds its post.
                for (int i = 0; i < 1500; i++)
                {
                    foreach (var r in all) r.Tick(Dt);
                    maxPostDrift = Mathf.Max(maxPostDrift, Flat(gunner.transform.position, postPos));
                }
                Vector3 guardPoint = SquadTactics.BodyguardPoint(gunner.transform.position, maxGo.transform.position);
                Assert.Less(Flat(bruiser.transform.position, guardPoint), 1f, "bruiser within 1 m of the bodyguard point");
                Assert.LessOrEqual(maxPostDrift, 1.5f, "the gunner stays within 1.5 m of its post");

                // 6. A Blinker lands behind Max (dot of his facing with the landing offset < -0.7).
                RobotEnemy blinker = Place(sp, EnemyKind.Blinker, new Vector3(-8f, 0f, 20f), "s2", false, true);
                Physics.SyncTransforms();
                Hit(blinker);
                bool teleporting = false;
                for (int i = 0; i < 2000 && !teleporting; i++)
                {
                    blinker.Tick(Dt);
                    teleporting = blinker.Current == RobotEnemy.State.Teleport;
                }
                Assert.IsTrue(teleporting, "the blinker blinks");
                Vector3 landing = (Vector3)TeleportTargetField.GetValue(blinker);
                Vector3 offset = landing - maxGo.transform.position; offset.y = 0f;
                Assert.Less(Vector3.Dot(maxGo.transform.forward, offset.normalized), -0.7f, "landing is behind Max");

                // 7. squadTactics OFF: the same squad is just robots - hitting the gunner wakes only the gunner.
                RobotEnemy.ResetRegistry();
                RobotEnemy g2 = Place(sp, EnemyKind.Gunner, new Vector3(0f, 0f, -40f), "s1", true, false);
                RobotEnemy r2 = Place(sp, EnemyKind.Rusher, new Vector3(-4f, 0f, -40f), "s1", false, false);
                Physics.SyncTransforms();
                Hit(g2);
                Assert.AreNotEqual(RobotEnemy.State.Dormant, g2.Current, "the hit gunner wakes");
                Assert.AreEqual(RobotEnemy.State.Dormant, r2.Current, "with squadTactics off a squad-mate sleeps on");
            }
            finally
            {
                if (spawnerGo != null) Object.DestroyImmediate(spawnerGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (groundGo != null) Object.DestroyImmediate(groundGo);
            }
        }
    }
}
