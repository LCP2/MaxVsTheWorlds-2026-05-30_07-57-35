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
    /// MV-1452 (the one new test, Rule 1): Ricochet POWER damage is back to <c>PowerDamageByLevel</c>
    /// (12..40) against robots too. Fires real discs through <c>Ricochet.FireOneDisc</c> (no bounce), City
    /// POWER 5: a Bruiser loses exactly 26, a Courier (64 HP) survives 2 discs; POWER 8 Bruiser loses 40;
    /// RIG p_dmg at level 5 reads 26. Fails on main with MV-1446 merged (Bruiser loses 100).
    /// </summary>
    public sealed class MV1452RicochetDamageRevertTests
    {
        private static readonly Vector3 Origin = new Vector3(-61113f, 0f, 54221f);
        private const float Dt = 1f / 60f;

        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo RicochetAwake =
            typeof(Ricochet).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo FireOne =
            typeof(Ricochet).GetMethod("FireOneDisc", BindingFlags.NonPublic | BindingFlags.Instance);

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
            var go = new GameObject("MV1452-" + a.Kind);
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
            var go = new GameObject("MV1452 Weapon");
            _made.Add(go);
            go.transform.position = Origin + new Vector3(0f, 0.5f, 0f);
            var w = go.AddComponent<Ricochet>();
            RicochetAwake.Invoke(w, null);
            FireOne.Invoke(w, null);
            foreach (RicochetDisc d in Object.FindObjectsByType<RicochetDisc>(FindObjectsInactive.Exclude))
                for (int i = 0; i < 120 && d.IsActiveDisc; i++) d.Tick(Dt);
        }

        [Test]
        public void RicochetPowerDamageIsBackToTheOriginalTable_MV1452()
        {
            LogAssert.ignoreFailingMessages = true;
            WorldConfig city = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(city, "city_config.json failed to load");
            WeaponSystemState.ApplyWorldLoadout(WorldCatalog.IndexOf(WorldIds.City));
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Ricochet;
            SetPower(5);

            RobotEnemy bruiser = NewRobot(EnemyArchetype.For(EnemyKind.Bruiser, city));
            float before = bruiser.HealthCurrent;
            FireOneDisc();
            Assert.AreEqual(26f, before - bruiser.HealthCurrent, 1e-3f, "POWER 5 Bruiser must lose exactly 26");
            Object.DestroyImmediate(bruiser.gameObject); RobotEnemy.ResetRegistry();

            RobotEnemy courier = NewRobot(EnemyArchetype.For(EnemyKind.Rusher, city));
            FireOneDisc();
            FireOneDisc();
            Assert.IsTrue(courier.IsAlive, "Courier must survive 2 discs (HP " + courier.HealthCurrent + ")");
            Object.DestroyImmediate(courier.gameObject); RobotEnemy.ResetRegistry();

            SetPower(8);
            RobotEnemy top = NewRobot(EnemyArchetype.For(EnemyKind.Bruiser, city));
            before = top.HealthCurrent;
            FireOneDisc();
            Assert.AreEqual(40f, before - top.HealthCurrent, 1e-3f, "POWER 8 Bruiser must lose exactly 40");

            SetPower(5);
            Assert.IsTrue(RigEffectLines.TryValue(WeaponCatalog.PrimaryKind.Ricochet, WeaponSystemState.SecondaryKind,
                "p_dmg", 5, out float v, out _));
            Assert.AreEqual(26f, v, 1e-4f, "RIG p_dmg at level 5");
        }
    }
}
