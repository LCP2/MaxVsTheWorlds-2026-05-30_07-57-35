using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-914: MV-806 swapped the Sentinel's water beam for World 2's own red
    /// <see cref="SentinelBolt"/> with no world check anywhere on that path, so World 1 started
    /// firing World 2's bolt too (Lee, device, 2026-09-23). One consolidated test (testing policy
    /// MV-465 Rule 1) covers all three AC as facets of the same regression — the shot's VISUAL
    /// identity depends on the resolved world, nothing else does: (1) World 1 (resolved world index
    /// 0) fires a <see cref="WaterVfx"/> beam and spawns no <see cref="SentinelBolt"/>; (2) World 2
    /// (resolved world index 1) still fires a <see cref="SentinelBolt"/> tinted
    /// <see cref="StormdrainLightKit.Red"/>, unchanged; (3) the damage actually dealt and the fire
    /// cooldown actually resolved after the shot are identical in both worlds — only the visual
    /// differs.
    ///
    /// Fails on base commit bbe7476 (today): <c>Sentinel.FireBeam</c> calls
    /// <c>SentinelBolt.Fire</c> unconditionally, with no world branch at all, so the World 1
    /// assertions (a WaterVfx beam exists, no SentinelBolt was spawned) are unsatisfiable — World 1
    /// gets a SentinelBolt exactly like World 2 does.
    /// </summary>
    public sealed class MV914SentinelWorldBeamTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => Sentinel.ResetRegistry();

        private static void InvokeUpdate(Sentinel sentinel) =>
            typeof(Sentinel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(sentinel, null);

        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly PropertyInfo ResolvedWorldIndexProperty =
            typeof(BackyardPath).GetProperty(nameof(BackyardPath.ResolvedWorldIndex));

        private static readonly FieldInfo FireCooldownField =
            typeof(Sentinel).GetField("_fireCooldown", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewTarget(Vector3 position)
        {
            var go = new GameObject("MV914 Target Robot");
            go.transform.position = position;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            // MV-832: NearestRobotInRange reads RobotEnemy.Active, not a physics query — OnEnable
            // (not just ResetState) is what registers a robot into that list, same idiom
            // MV806SentinelBoltTests already uses.
            RobotOnEnableMethod.Invoke(e, null);
            return e;
        }

        /// <summary>A <see cref="BackyardPath"/> whose <see cref="BackyardPath.ResolvedWorldIndex"/>
        /// reads <paramref name="worldIndex"/> without running its own <see cref="BackyardPath.Awake"/>
        /// (Awake/OnEnable don't run outside Play mode — same reason
        /// MV832SentinelTargetingTests/MV755StormdrainDressingTests reflect-set BackyardPath's private
        /// state directly rather than invoking a real map load in an EditMode fixture).</summary>
        private static BackyardPath NewResolvedPath(int worldIndex)
        {
            var go = new GameObject($"MV914 Path W{worldIndex}");
            var path = go.AddComponent<BackyardPath>();
            ResolvedWorldIndexProperty.SetValue(path, worldIndex);
            return path;
        }

        // Well clear of the origin/small coordinates other fixtures in this shared EditMode run use
        // for their own sentinel/robot pairs — same reason MV806SentinelBoltTests spreads its own
        // pair 4000m out: NearestRobotInRange reads RobotEnemy.Active globally, so a stray leftover
        // from another fixture could otherwise outrank this test's own target.
        private static readonly Vector3 World1Origin = new Vector3(4600f, 0f, 4600f);
        private static readonly Vector3 World2Origin = new Vector3(4800f, 0f, 4800f);

        [Test]
        public void SentinelFiresWaterInWorld1AndBoltInWorld2_SameDamageAndCadence()
        {
            BackyardPath path1 = null, path2 = null;
            Sentinel sentinel1 = null, sentinel2 = null;
            RobotEnemy target1 = null, target2 = null;
            SentinelBolt bolt = null;
            try
            {
                // --- World 1 (resolved world index 0): water beam, never a bolt ---
                path1 = NewResolvedPath(0);
                sentinel1 = new GameObject("MV914 Sentinel W1").AddComponent<Sentinel>();
                sentinel1.Init(World1Origin, maxHp: 60f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                target1 = NewTarget(World1Origin + new Vector3(2f, 0f, 0f));
                float target1HealthBefore = target1.HealthCurrent;

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)
                InvokeUpdate(sentinel1);

                Assert.IsNotNull(sentinel1.GetComponentInChildren<WaterVfx>(true),
                    "World 1 (resolved world index 0) must fire the WaterVfx beam");
                Assert.IsNull(Object.FindAnyObjectByType<SentinelBolt>(),
                    "World 1 must not spawn a SentinelBolt");

                float damage1 = target1HealthBefore - target1.HealthCurrent;
                Assert.That(damage1, Is.GreaterThan(0f), "World 1's shot dealt no damage");
                float cooldown1 = (float)FireCooldownField.GetValue(sentinel1);

                Object.DestroyImmediate(path1.gameObject);
                path1 = null;

                // --- World 2 (resolved world index 1): SentinelBolt, unchanged colour ---
                path2 = NewResolvedPath(1);
                sentinel2 = new GameObject("MV914 Sentinel W2").AddComponent<Sentinel>();
                sentinel2.Init(World2Origin, maxHp: 60f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                target2 = NewTarget(World2Origin + new Vector3(2f, 0f, 0f));
                float target2HealthBefore = target2.HealthCurrent;

                Physics.SyncTransforms();
                InvokeUpdate(sentinel2);

                bolt = Object.FindAnyObjectByType<SentinelBolt>();
                Assert.IsNotNull(bolt, "World 2 (resolved world index 1) must still spawn a SentinelBolt");
                Assert.IsNull(sentinel2.GetComponentInChildren<WaterVfx>(true),
                    "World 2 must not build a WaterVfx beam");

                Transform boltMesh = bolt.transform.Find("Bolt");
                Assert.IsNotNull(boltMesh, "test precondition: SentinelBolt must build a child named 'Bolt'");
                Color boltTint = boltMesh.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.That(boltTint.r, Is.EqualTo(StormdrainLightKit.Red.r).Within(0.001f),
                    "World 2's bolt colour must stay StormdrainLightKit.Red, unchanged by this ticket");
                Assert.That(boltTint.g, Is.EqualTo(StormdrainLightKit.Red.g).Within(0.001f));
                Assert.That(boltTint.b, Is.EqualTo(StormdrainLightKit.Red.b).Within(0.001f));

                float damage2 = target2HealthBefore - target2.HealthCurrent;
                float cooldown2 = (float)FireCooldownField.GetValue(sentinel2);

                // --- AC3: same resolved damage and cadence in both worlds — only the visual differs ---
                Assert.That(damage2, Is.EqualTo(damage1).Within(0.001f),
                    $"World 2 dealt {damage2} damage, World 1 dealt {damage1} — only the visual should differ");
                Assert.That(cooldown2, Is.EqualTo(cooldown1).Within(0.001f),
                    $"World 2's resolved fire cooldown ({cooldown2}) differs from World 1's ({cooldown1})");
            }
            finally
            {
                if (bolt != null) Object.DestroyImmediate(bolt.gameObject);
                if (path1 != null) Object.DestroyImmediate(path1.gameObject);
                if (path2 != null) Object.DestroyImmediate(path2.gameObject);
                if (sentinel1 != null) Object.DestroyImmediate(sentinel1.gameObject);
                if (sentinel2 != null) Object.DestroyImmediate(sentinel2.gameObject);
                if (target1 != null) Object.DestroyImmediate(target1.gameObject);
                if (target2 != null) Object.DestroyImmediate(target2.gameObject);
            }
        }
    }
}
