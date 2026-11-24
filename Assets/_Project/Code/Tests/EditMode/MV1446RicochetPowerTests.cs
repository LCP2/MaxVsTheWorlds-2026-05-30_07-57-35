using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1446 (the one new test, Rule 1): RICOCHET POWER is steeper against robots. Real
    /// <see cref="RicochetDisc"/> hits, no bounce, City POWER at level 5: a Bruiser dies on the 2nd disc, a
    /// Courier on the 1st, a Heavy survives 5; a pylon and destructible cover lose exactly the old 26;
    /// POWER 1 still deals 12; and the RIG <c>p_dmg</c> line reads 100. Fails on base 6fa004e: a Bruiser
    /// (200 HP) takes 26 per disc so it is still alive after 2 discs.
    /// </summary>
    public sealed class MV1446RicochetPowerTests
    {
        private static readonly Vector3 Origin = new Vector3(-61113f, 0f, 54221f);
        private const float Dt = 1f / 60f;

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RicochetAwake =
            typeof(Ricochet).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _made) if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
            RicochetDisc.DestroyPoolForTests();
            RobotEnemy.ResetRegistry();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
        }

        private static void SetPower(int level) =>
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "p_dmg", level } }, new[] { "PRIMARY" });

        private RobotEnemy NewRobot(EnemyArchetype a)
        {
            var go = new GameObject("MV1446-" + a.Kind);
            _made.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(a);
            OnEnableMethod.Invoke(e, null);
            e.transform.position = Origin + new Vector3(0f, a.SpawnHeight, 4f);
            Physics.SyncTransforms();
            return e;
        }

        private void FireOneDisc()
        {
            var go = new GameObject("MV1446 Weapon");
            _made.Add(go);
            go.transform.position = Origin + new Vector3(0f, 0.5f, 0f);
            var w = go.AddComponent<Ricochet>();
            RicochetAwake.Invoke(w, null);
            RicochetDisc d = RicochetDisc.Spawn(go.transform.position, Vector3.forward, Ricochet.DefaultSpeed, 0,
                1, 0, w.EffectiveDamagePerDisc, w.BankBonusPerBounce, 0f, 0f,
                Ricochet.DefaultRadius, Ricochet.DefaultLifetimeSeconds, w.EffectiveRobotDamagePerDisc);
            Assert.IsNotNull(d, "disc must spawn");
            for (int i = 0; i < 120 && d.IsActiveDisc; i++) d.Tick(Dt);
        }

        [Test]
        public void PowerHitsRobotsHarderThanStructures_MV1446()
        {
            LogAssert.ignoreFailingMessages = true;
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");
            WeaponSystemState.ApplyWorldLoadout(WorldCatalog.IndexOf(WorldIds.City));
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Ricochet;
            SetPower(5);

            RobotEnemy bruiser = NewRobot(EnemyArchetype.For(EnemyKind.Bruiser, city));
            FireOneDisc();
            Assert.IsTrue(bruiser.IsAlive, "Bruiser must survive the 1st disc");
            FireOneDisc();
            Assert.IsFalse(bruiser.IsAlive, "Bruiser must die on the 2nd disc (HP " + bruiser.HealthCurrent + ")");
            Object.DestroyImmediate(bruiser.gameObject); RobotEnemy.ResetRegistry();

            RobotEnemy courier = NewRobot(EnemyArchetype.For(EnemyKind.Rusher, city));
            FireOneDisc();
            Assert.IsFalse(courier.IsAlive, "Courier must die on the 1st disc");
            Object.DestroyImmediate(courier.gameObject); RobotEnemy.ResetRegistry();

            RobotEnemy heavy = NewRobot(EnemyArchetype.For(EnemyKind.Heavy, city));
            for (int i = 0; i < 5; i++) FireOneDisc();
            Assert.IsTrue(heavy.IsAlive, "Heavy must still be alive after 5 discs");
            Object.DestroyImmediate(heavy.gameObject); RobotEnemy.ResetRegistry();

            // Structures keep the old 26 at POWER 5.
            var pylonGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _made.Add(pylonGo);
            pylonGo.transform.position = Origin + new Vector3(0f, 0.5f, 4f);
            var pylon = pylonGo.AddComponent<Pylon>();
            pylon.Build();
            Physics.SyncTransforms();
            float pylonBefore = pylon.HealthCurrent;
            FireOneDisc();
            Assert.AreEqual(26f, pylonBefore - pylon.HealthCurrent, 1e-3f, "pylon must lose exactly 26");
            Object.DestroyImmediate(pylonGo);

            // Cover exposes no HP: 26 hp breaks on one disc, 26.5 hp does not.
            foreach (var (hp, shouldBreak) in new[] { (26f, true), (26.5f, false) })
            {
                var coverGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _made.Add(coverGo);
                coverGo.transform.position = Origin + new Vector3(0f, 0.5f, 4f);
                coverGo.transform.localScale = new Vector3(2f, 1f, 0.5f);
                var cover = coverGo.AddComponent<DestructibleCover>();
                cover.Configure(null, hp);
                cover.Build();
                CoverLayer.Assign(coverGo);
                Physics.SyncTransforms();
                FireOneDisc();
                Assert.AreEqual(shouldBreak, cover.IsBroken, "cover hp " + hp + " must lose exactly 26 per disc");
                Object.DestroyImmediate(coverGo);
            }

            // POWER 1: 12 per disc.
            SetPower(1);
            RobotEnemy low = NewRobot(EnemyArchetype.For(EnemyKind.Bruiser, city));
            float before = low.HealthCurrent;
            FireOneDisc();
            Assert.AreEqual(12f, before - low.HealthCurrent, 1e-3f, "POWER 1 Bruiser must lose exactly 12");

            // RIG line at level 5.
            SetPower(5);
            Assert.IsTrue(RigEffectLines.TryValue(WeaponCatalog.PrimaryKind.Ricochet, WeaponSystemState.SecondaryKind,
                "p_dmg", 5, out float v, out _));
            Assert.AreEqual(100f, v, 1e-4f, "RIG p_dmg at level 5");
        }
    }
}
