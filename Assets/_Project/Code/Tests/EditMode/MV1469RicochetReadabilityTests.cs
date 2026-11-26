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
    /// MV-1469 (the one new test, Rule 1): RICOCHET POWER 8 must read as a hero disc with jagged lightning,
    /// not a flat pale bar. Asserts RESOLVED values on a built POWER 8 disc: the lightning trail bolt has at
    /// least 10 segments and every frame's largest sideways offset is at least 0.10 m; each rim crackle is
    /// 0.25-0.5 m long; the trail's head alpha is at most 0.55 and its 50% colour is fire (blue below red),
    /// not cyan; a chain arc is a white core with a #BFEFFF glow; TrailSecondsForPower(8) is at most 0.18.
    /// Reads hierarchy / LineRenderers only, so it compiles (and fails) on the base commit.
    /// </summary>
    public sealed class MV1469RicochetReadabilityTests
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
            foreach (GameObject go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
            ClearArcs();
            RicochetDisc.DestroyPoolForTests();
            RobotEnemy.ResetRegistry();
        }

        private static void ClearArcs()
        {
            foreach (ArcBoltVfx a in Object.FindObjectsByType<ArcBoltVfx>(FindObjectsInactive.Include))
                Object.DestroyImmediate(a.gameObject);
        }

        private RobotEnemy NewRobot(Vector3 offset)
        {
            EnemyArchetype a = EnemyArchetype.For(EnemyKind.Bruiser, _city);
            var go = new GameObject("MV1469-robot");
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

        private static RicochetDisc Spawn(int power)
        {
            return RicochetDisc.Spawn(Origin + new Vector3(0f, 0.5f, 0f), Vector3.forward, speed: 30f, bounces: 0,
                powerLevel: power, aimLevel: 1, damage: 40f, bankBonusPerBounce: 0f, turnRateDegPerSec: 0f,
                lookaheadMetres: 0f, radius: 0.25f, lifetime: 5f);
        }

        private static LineRenderer CoreOf(Transform bolt)
        {
            foreach (LineRenderer lr in bolt.GetComponentsInChildren<LineRenderer>(true))
                if (lr.gameObject.name == "Core") return lr;
            return null;
        }

        private static float MaxSideOffset(LineRenderer lr)
        {
            Vector3 a = lr.GetPosition(0), b = lr.GetPosition(lr.positionCount - 1);
            Vector3 axis = (b - a).normalized;
            float max = 0f;
            for (int i = 1; i < lr.positionCount - 1; i++)
            {
                Vector3 d = lr.GetPosition(i) - a;
                max = Mathf.Max(max, (d - axis * Vector3.Dot(d, axis)).magnitude);
            }
            return max;
        }

        [Test]
        public void Power8DiscIsTheHeroAndLightningIsJagged_MV1469()
        {
            Assert.LessOrEqual(RicochetDisc.TrailSecondsForPower(8), 0.18f + 1e-4f, "POWER 8 trail seconds");

            RicochetDisc d = Spawn(8);
            Transform trailBolt = d.transform.Find("LightningTrail");
            Assert.IsNotNull(trailBolt, "POWER 8 disc has a LightningTrail bolt");
            for (int i = 0; i < 30; i++) d.Tick(Dt);

            int samples = 0;
            for (int i = 0; i < 12; i++)
            {
                d.Tick(Dt);
                LineRenderer core = CoreOf(trailBolt);
                Assert.GreaterOrEqual(core.positionCount - 1, 10, "lightning trail segments");
                Assert.GreaterOrEqual(MaxSideOffset(core), 0.10f - 1e-4f, "lightning trail largest sideways offset, frame " + i);
                samples++;
            }
            Assert.AreEqual(12, samples);

            int crackles = 0;
            foreach (Transform c in d.transform)
            {
                if (!c.name.StartsWith("RimCrackle") || !c.gameObject.activeInHierarchy) continue;
                LineRenderer core = CoreOf(c);
                float len = (core.GetPosition(core.positionCount - 1) - core.GetPosition(0)).magnitude;
                Assert.GreaterOrEqual(len, 0.25f - 1e-3f, c.name + " length");
                Assert.LessOrEqual(len, 0.5f + 1e-3f, c.name + " length");
                crackles++;
            }
            Assert.AreEqual(6, crackles, "POWER 8 rim crackle count unchanged");

            TrailRenderer trail = d.GetComponent<TrailRenderer>();
            Gradient g = trail.colorGradient;
            Assert.LessOrEqual(g.Evaluate(0f).a, 0.55f + 1e-3f, "trail head alpha");
            Color mid = g.Evaluate(0.5f);
            Assert.Less(mid.b, mid.r, "trail at 50% must be fire, not cyan");

            // Chain arc colours.
            foreach (GameObject go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear(); RobotEnemy.ResetRegistry(); RicochetDisc.DestroyPoolForTests();
            NewRobot(new Vector3(0f, 0f, 3f));
            NewRobot(new Vector3(0f, 0f, 6f));
            RicochetDisc p8 = Spawn(8);
            for (int i = 0; i < 120 && p8.IsActiveDisc; i++) p8.Tick(Dt);
            ArcBoltVfx arc = Object.FindFirstObjectByType<ArcBoltVfx>();
            Assert.IsNotNull(arc, "a chain arc was drawn");
            LineRenderer arcCore = null, arcGlow = null;
            foreach (LineRenderer lr in arc.GetComponentsInChildren<LineRenderer>())
            {
                if (lr.gameObject.name == "ArcCore") arcCore = lr;
                if (lr.gameObject.name == "ArcSleeve") arcGlow = lr;
            }
            Color core8 = arcCore.startColor, glow8 = arcGlow.startColor;
            Assert.AreEqual(1f, core8.r, 0.02f); Assert.AreEqual(1f, core8.g, 0.02f); Assert.AreEqual(1f, core8.b, 0.02f);
            Assert.AreEqual(0xBF / 255f, glow8.r, 0.02f, "glow red");
            Assert.AreEqual(0xEF / 255f, glow8.g, 0.02f, "glow green");
            Assert.AreEqual(1f, glow8.b, 0.02f, "glow blue");
        }
    }
}
