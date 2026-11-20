using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Factories;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1395 (the one new test, Rule 1): each City primary (RCDA / LPPE / RICOCHET) fires one shot at a
    /// Canister and one at a DestructibleCover in clear line of fire; both must lose HP (resolved values,
    /// Tier 2). Guards MV-1395: RicochetDisc only damaged/stopped on Team.Enemy, so it flew through
    /// canisters and bounced off destructible cover without hurting it.
    /// </summary>
    public sealed class MV1395PrimariesDamageNeutralTargetsTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void SetUp() { WeaponSystemState.Reset(); RobotEnemy_Reset(); }

        [TearDown]
        public void TearDown() { WeaponSystemState.Reset(); RicochetDisc.DestroyPoolForTests(); RobotEnemy_Reset(); }

        private static void RobotEnemy_Reset() => MaxWorlds.Enemies.RobotEnemy.ResetRegistry();

        [TestCase(WeaponCatalog.PrimaryKind.Rcda)]
        [TestCase(WeaponCatalog.PrimaryKind.Lppe)]
        [TestCase(WeaponCatalog.PrimaryKind.Ricochet)]
        public void EveryCityPrimaryDamagesACanisterAndADestructibleCover(WeaponCatalog.PrimaryKind primary)
        {
            LogAssert.ignoreFailingMessages = true; // one-time VfxMaterials shader log in EditMode
            var made = new System.Collections.Generic.List<GameObject>();
            try
            {
                WeaponSystemState.ApplyWorldLoadout(WorldCatalog.IndexOf(WorldIds.City));
                WeaponSystemState.ActivePrimary = primary;

                for (int kind = 0; kind < 2; kind++)
                {
                    // fresh fixture per target: a lone target 4 m dead ahead (+Z) of the muzzle
                    GameObject target = GameObject.CreatePrimitive(kind == 0 ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                    made.Add(target);
                    target.transform.position = new Vector3(0f, 0.5f, 4f);
                    target.transform.localScale = kind == 0 ? new Vector3(0.8f, 0.5f, 0.8f) : new Vector3(2f, 1f, 0.5f);
                    Canister canister = null; DestructibleCover cover = null;
                    if (kind == 0) { canister = target.AddComponent<Canister>(); canister.Build(); }
                    else { cover = target.AddComponent<DestructibleCover>(); cover.Configure(null, 1f); cover.Build(); CoverLayer.Assign(target); }
                    Physics.SyncTransforms();

                    float before = kind == 0 ? canister.HealthCurrent : 1f;
                    Fire(primary, made);

                    string what = kind == 0 ? "Canister" : "DestructibleCover";
                    if (kind == 0)
                        Assert.Less(canister.HealthCurrent, before, $"{primary}: a shot must damage the {what} (HP {canister.HealthCurrent} of {before})");
                    else
                        Assert.IsTrue(cover.IsBroken, $"{primary}: a shot must damage the {what} (1 hp piece still intact)");

                    Object.DestroyImmediate(target); made.Remove(target);
                }
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                foreach (var g in made) if (g != null) Object.DestroyImmediate(g);
            }
        }

        private static void Fire(WeaponCatalog.PrimaryKind primary, System.Collections.Generic.List<GameObject> made)
        {
            var go = new GameObject("MV1395 Weapon");
            made.Add(go);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            const float step = 1f / 60f;
            if (primary == WeaponCatalog.PrimaryKind.Rcda)
            {
                var w = go.AddComponent<WaterBlaster>();
                typeof(WaterBlaster).GetMethod("Awake", NonPublicInstance).Invoke(w, null);
                typeof(WaterBlaster).GetMethod("FireTick", NonPublicInstance).Invoke(w, null);
            }
            else if (primary == WeaponCatalog.PrimaryKind.Lppe)
            {
                var w = go.AddComponent<PulseLaser>();
                typeof(PulseLaser).GetMethod("Awake", NonPublicInstance).Invoke(w, null);
                typeof(PulseLaser).GetMethod("FireTick", NonPublicInstance).Invoke(w, null);
                SeekerPulse p = w.LastSpawnedPulseForTests;
                Assert.IsNotNull(p, "LPPE must spawn a pulse");
                for (int i = 0; i < 180 && p != null && p.gameObject.activeSelf; i++) p.Tick(step);
                if (p != null) made.Add(p.gameObject);
            }
            else
            {
                var w = go.AddComponent<Ricochet>();
                typeof(Ricochet).GetMethod("Awake", NonPublicInstance).Invoke(w, null);
                RicochetDisc d = RicochetDisc.Spawn(go.transform.position, Vector3.forward, Ricochet.DefaultSpeed,
                    w.BouncesPerDisc, 1, 0, w.EffectiveDamagePerDisc, w.BankBonusPerBounce,
                    w.AimTurnRateDegPerSec, w.AimLookaheadMetres, Ricochet.DefaultRadius, Ricochet.DefaultLifetimeSeconds);
                Assert.IsNotNull(d, "disc must spawn");
                for (int i = 0; i < 40 && d.IsActiveDisc && d.BouncesTaken == 0; i++) d.Tick(step);
            }
        }
    }
}
