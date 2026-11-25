using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1451 (the one new test, Rule 1): RICOCHET shows POWER through crackle and chain lightning, not
    /// size. Fires a real <see cref="RicochetDisc"/> (no bounce) at robot A with robots B..E in a line 3 m
    /// apart behind it. POWER 8: A loses exactly 40, B/C/D each get a chain arc + frazzle and lose 0, E
    /// nothing. POWER 6: only B. POWER 5: no arc. A dormant robot 3 m from A is never arced or woken. The
    /// built disc is 0.40 m at POWER 1 and 0.60 m at POWER 8, with 0 and 6 active rim-crackle bolts, and its
    /// glow never exceeds 1.0 m. Written against the pre-change API only (reflection / hierarchy reads) so
    /// it compiles, and fails, on the base commit.
    /// </summary>
    public sealed class MV1451RicochetCrackleChainTests
    {
        private static readonly Vector3 Origin = new Vector3(-61113f, 0f, 54221f);
        private const float Dt = 1f / 60f;

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _made = new List<GameObject>();
        private WorldConfig _city;

        [SetUp]
        public void SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            _city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(_city, "city_config.json failed to load");
            ClearArcs();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _made.Clear();
            ClearArcs();
            RicochetDisc.DestroyPoolForTests();
            RobotEnemy.ResetRegistry();
        }

        private static void ClearArcs()
        {
            foreach (ArcBoltVfx a in UnityEngine.Object.FindObjectsByType<ArcBoltVfx>(FindObjectsInactive.Include))
                UnityEngine.Object.DestroyImmediate(a.gameObject);
        }

        private RobotEnemy NewRobot(Vector3 offset)
        {
            EnemyArchetype a = EnemyArchetype.For(EnemyKind.Bruiser, _city);
            var go = new GameObject("MV1451-robot");
            _made.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(a);
            OnEnableMethod.Invoke(e, null);
            e.transform.position = Origin + new Vector3(offset.x, a.SpawnHeight, offset.z);
            Physics.SyncTransforms();
            return e;
        }

        private static RicochetDisc Spawn(int power, int bounces)
        {
            return RicochetDisc.Spawn(Origin + new Vector3(0f, 0.5f, 0f), Vector3.forward, speed: 30f, bounces: bounces,
                powerLevel: power, aimLevel: 1, damage: 40f, bankBonusPerBounce: 0f, turnRateDegPerSec: 0f,
                lookaheadMetres: 0f, radius: 0.25f, lifetime: 5f);
        }

        private static void Fly(RicochetDisc d)
        {
            for (int i = 0; i < 120 && d.IsActiveDisc; i++) d.Tick(Dt);
        }

        /// <summary>The end point of every live ArcBolt, read off its core line renderer.</summary>
        private static List<Vector3> ArcEnds()
        {
            var ends = new List<Vector3>();
            foreach (ArcBoltVfx a in UnityEngine.Object.FindObjectsByType<ArcBoltVfx>(FindObjectsInactive.Exclude))
            {
                LineRenderer core = null;
                foreach (LineRenderer lr in a.GetComponentsInChildren<LineRenderer>())
                    if (lr.gameObject.name == "ArcCore") core = lr;
                if (core != null) ends.Add(core.GetPosition(core.positionCount - 1));
            }
            return ends;
        }

        private static bool HasArcTo(List<Vector3> ends, RobotEnemy r)
        {
            foreach (Vector3 e in ends)
            {
                Vector3 d = e - r.transform.position; d.y = 0f;
                if (d.magnitude < 0.3f) return true;
            }
            return false;
        }

        private static bool HasFrazzle(RobotEnemy r)
        {
            Type t = typeof(RobotEnemy).Assembly.GetType("MaxWorlds.VFX.RobotFrazzleVfx");
            if (t == null) return false;
            Component c = r.GetComponent(t);
            if (c == null) return false;
            return (bool)t.GetProperty("IsActive").GetValue(c);
        }

        private RobotEnemy[] Line(out RobotEnemy a)
        {
            a = NewRobot(new Vector3(0f, 0f, 3f));
            return new[]
            {
                NewRobot(new Vector3(0f, 0f, 6f)), NewRobot(new Vector3(0f, 0f, 9f)),
                NewRobot(new Vector3(0f, 0f, 12f)), NewRobot(new Vector3(0f, 0f, 15f)),
            };
        }

        [Test]
        public void CrackleAndVisualOnlyChainLightningShowPower_MV1451()
        {
            // POWER 8: A takes today's 40; B, C, D arc + frazzle with no damage; E untouched.
            RobotEnemy a;
            RobotEnemy[] line = Line(out a);
            float aBefore = a.HealthCurrent;
            float[] before = { line[0].HealthCurrent, line[1].HealthCurrent, line[2].HealthCurrent, line[3].HealthCurrent };
            Fly(Spawn(8, 0));
            List<Vector3> ends = ArcEnds();
            Assert.AreEqual(40f, aBefore - a.HealthCurrent, 1e-3f, "A must lose exactly 40 HP");
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(HasArcTo(ends, line[i]), "POWER 8: robot " + "BCD"[i] + " must show a chain arc");
                Assert.IsTrue(HasFrazzle(line[i]), "POWER 8: robot " + "BCD"[i] + " must show the hit frazzle");
                Assert.AreEqual(before[i], line[i].HealthCurrent, 1e-3f, "chain arcs deal no damage to " + "BCD"[i]);
            }
            Assert.IsFalse(HasArcTo(ends, line[3]), "POWER 8: E (4th jump) must show no chain arc");
            Assert.IsFalse(HasFrazzle(line[3]), "POWER 8: E must show no frazzle");
            Assert.AreEqual(before[3], line[3].HealthCurrent, 1e-3f);

            // POWER 6: one jump only.
            foreach (GameObject go in _made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _made.Clear(); RobotEnemy.ResetRegistry(); ClearArcs(); RicochetDisc.DestroyPoolForTests();
            line = Line(out a);
            Fly(Spawn(6, 0));
            ends = ArcEnds();
            Assert.IsTrue(HasArcTo(ends, line[0]), "POWER 6: B must show a chain arc");
            Assert.IsFalse(HasArcTo(ends, line[1]), "POWER 6: C must show none");
            Assert.AreEqual(1, ends.Count, "POWER 6: exactly one chain arc");

            // POWER 5: no chain.
            foreach (GameObject go in _made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _made.Clear(); RobotEnemy.ResetRegistry(); ClearArcs(); RicochetDisc.DestroyPoolForTests();
            line = Line(out a);
            Fly(Spawn(5, 0));
            Assert.AreEqual(0, ArcEnds().Count, "POWER 5: no chain arc");

            // POWER 8: a dormant robot beside A is never arced or woken.
            foreach (GameObject go in _made) if (go != null) UnityEngine.Object.DestroyImmediate(go);
            _made.Clear(); RobotEnemy.ResetRegistry(); ClearArcs(); RicochetDisc.DestroyPoolForTests();
            a = NewRobot(new Vector3(0f, 0f, 3f));
            RobotEnemy sleeper = NewRobot(new Vector3(-3f, 0f, 3f));
            sleeper.BeginDormant();
            Assert.IsTrue(sleeper.IsDormant, "test setup: sleeper must be dormant");
            Fly(Spawn(8, 0));
            Assert.IsFalse(HasArcTo(ArcEnds(), sleeper), "a dormant robot gets no chain arc");
            Assert.IsTrue(sleeper.IsDormant, "a dormant robot is still dormant");

            // The built disc: size nearly fixed, crackle count by POWER, glow never wider than 1.0 m.
            RicochetDisc.DestroyPoolForTests();
            RicochetDisc p1 = Spawn(1, 2);
            Assert.AreEqual(0.40f, p1.VisualDiameter, 1e-4f, "POWER 1 diameter");
            Assert.AreEqual(0, CountRimCrackles(p1), "POWER 1: no rim crackle");
            RicochetDisc.DestroyPoolForTests();
            RicochetDisc p8 = Spawn(8, 2);
            Assert.AreEqual(0.60f, p8.VisualDiameter, 1e-4f, "POWER 8 diameter");
            Assert.AreEqual(6, CountRimCrackles(p8), "POWER 8: six rim crackles");
            foreach (string halo in new[] { "BlueHalo", "RedHalo" })
            {
                Transform h = p8.transform.Find(halo);
                if (h != null && h.gameObject.activeSelf)
                    Assert.LessOrEqual(h.localScale.x, 1.0f + 1e-4f, halo + " must not exceed 1.0 m");
            }
        }

        private static int CountRimCrackles(RicochetDisc d)
        {
            int n = 0;
            foreach (Transform c in d.transform)
                if (c.name.StartsWith("RimCrackle") && c.gameObject.activeInHierarchy) n++;
            return n;
        }
    }
}
