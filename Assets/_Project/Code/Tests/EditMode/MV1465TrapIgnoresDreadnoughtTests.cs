using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1465 — the capture trap used to catch and hold a Dreadnought (immune to conversion, MV-1217),
    /// then refuse the conversion and wait on it forever: half-captured robot, trap never ends, HUD
    /// stuck at 1/1. Drops a trap over a Dreadnought plus one Salvage Crab (the Bruiser's Reef reskin)
    /// and asserts resolved state only. Guards MV-1465.
    /// </summary>
    public sealed class MV1465TrapIgnoresDreadnoughtTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo CcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private const float Dt = 1f / 60f;

        [SetUp]
        [TearDown]
        public void Clear()
        {
            RobotEnemy.ResetRegistry();
            RobotEnemy.ConversionCap = 1;
            RigBoard.UseWorld(0);
            RigState.Reset();
            WeaponSystemState.Reset();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        private static RobotEnemy NewRobot(string name, Vector3 position, in EnemyArchetype archetype)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(archetype);
            RobotOnEnableMethod.Invoke(e, null);
            go.AddComponent<MeshFilter>().sharedMesh = new Mesh();
            go.AddComponent<MeshRenderer>();
            go.AddComponent<CharacterSkin>().Bind(CharacterRole.Robot);
            return e;
        }

        [Test]
        public void TrapNeverHoldsDreadnought_CrabConverts_TrapEndsWhenDreadnoughtDies_MV1465()
        {
            RigBoard.UseWorld(WorldCatalog.IndexOf(WorldIds.Reef));
            RigState.Reset();

            GameObject maxGo = null;
            RobotEnemy dread = null, crab = null;
            try
            {
                maxGo = new GameObject("MV-1465 test Max", typeof(CharacterController)) { tag = "Player" };
                maxGo.AddComponent<PlayerHealth>().Initialize();
                var abilities = maxGo.AddComponent<PlayerAbilities>();
                maxGo.transform.position = Vector3.zero;

                Assert.IsTrue(RigState.AcquireCap("p_trp"));
                Assert.IsTrue(RigState.RaiseLevel("p_tcap"));
                WeaponSystemState.RebuildAcquiredFromRigState();

                Assert.IsTrue(abilities.TryDropTrap());
                RobotTrap trap = abilities.ActiveTrap;
                Assert.IsNotNull(trap);

                dread = NewRobot("Dreadnought", new Vector3(2f, 0f, 0f), EnemyArchetype.Dreadnought);
                crab = NewRobot("Salvage Crab", new Vector3(-2f, 0f, 0f), EnemyArchetype.Bruiser);
                Physics.SyncTransforms();
                Color dreadColorBefore = dread.GetComponent<CharacterSkin>().BodyColor;

                trap.Tick(Dt);
                Assert.IsTrue(crab.IsTrapHeld, "fixture: the Crab inside the radius is caught");
                Assert.IsFalse(dread.IsTrapHeld, "a Dreadnought must never be held by the trap");

                // Run the trap through catch -> 8s timeout -> 4.5s conversion.
                int frames = Mathf.CeilToInt(13.2f / Dt);
                for (int i = 0; i < frames && trap != null; i++) trap.Tick(Dt);

                Assert.IsFalse(dread.IsTrapHeld, "a Dreadnought must never be held by the trap");
                Assert.AreEqual(Team.Enemy, dread.Team);
                Assert.AreEqual(dreadColorBefore, dread.GetComponent<CharacterSkin>().BodyColor,
                    "the Dreadnought must keep its original body colour");
                Assert.IsTrue(crab.IsConverted, "the Crab must convert normally");
                Assert.IsNull(abilities.ActiveTrap, "the trap must end once its only catch has converted");

                dread.TakeDamage(new DamageInfo(1000000f, dread.transform.position, Vector3.up, Team.Player,
                    source: DamageSource.SecondaryWeapon));
                Assert.IsFalse(dread.IsAlive, "fixture: the Dreadnought must be dead");
                Assert.IsNull(abilities.ActiveTrap, "no trap object may remain after the Dreadnought dies");
                Assert.AreEqual(0, GameObject.FindObjectsByType<RobotTrap>(FindObjectsSortMode.None).Length,
                    "no active trap object may exist");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (dread != null) Object.DestroyImmediate(dread.gameObject);
                if (crab != null) Object.DestroyImmediate(crab.gameObject);
            }
        }
    }
}
