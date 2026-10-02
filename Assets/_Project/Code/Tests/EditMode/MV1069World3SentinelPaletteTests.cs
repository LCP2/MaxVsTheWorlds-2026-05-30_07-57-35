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
    /// MV-1069: World 3's Sentinel fell through to World 1's blue body with a red eye, and fired the
    /// thin red <see cref="SentinelBolt"/> every world but World 2 got (Lee, 2026-10-03: "give World 3's
    /// Sentinels a colour of their own — green — and make their lasers green as well... same styling
    /// and width of laser, same impressiveness" as World 2). One consolidated test (testing policy
    /// MV-465 Rule 1) covers all three worlds as facets of the same per-world style table: (1) World 3
    /// (resolved world index 2) resolves its body Warm/Accent materials to the ticket's green and its
    /// eye to pale gold, and its bolt resolves to the ticket's green at 1.6x a baseline bolt's
    /// cross-section/trail width; (2) World 2's body, eye and bolt stay byte-identical to before,
    /// proving the `_worldIndex == 1` branches collapsing into the style table didn't move World 2;
    /// (3) World 1 still fires the water beam and spawns no <see cref="SentinelBolt"/> at all.
    ///
    /// Fails on base commit cca6664 (today): <c>Sentinel.BuildBody</c> has no World 3 row at all, so
    /// World 3 resolves <c>Sentinel.BodyColor</c> (Max's own blue) and <c>EyeColor</c> (red) instead of
    /// the ticket's green/pale-gold, and the World 3 bolt fired by <c>FireBeam</c> never gets a
    /// thickness scale (<c>SentinelBolt.Fire</c>'s old `worldIndex == 1` check only widens World 2),
    /// so the World 3 assertions below are unsatisfiable.
    /// </summary>
    public sealed class MV1069World3SentinelPaletteTests
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

        private static readonly FieldInfo OwnedMaterialsField =
            typeof(Sentinel).GetField("_ownedMaterials", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo BodyField =
            typeof(Sentinel).GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance);

        private static RobotEnemy NewTarget(Vector3 position)
        {
            var go = new GameObject("MV1069 Target Robot");
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
        /// — same reflect-set idiom MV1004World2SentinelPaletteTests/MV914SentinelWorldBeamTests use.</summary>
        private static BackyardPath NewResolvedPath(int worldIndex)
        {
            var go = new GameObject($"MV1069 Path W{worldIndex}");
            var path = go.AddComponent<BackyardPath>();
            ResolvedWorldIndexProperty.SetValue(path, worldIndex);
            return path;
        }

        // Well clear of the coordinate ranges MV806/MV914/MV1004's own fixtures use in this shared
        // EditMode run — same "NearestRobotInRange reads RobotEnemy.Active globally" spacing reasoning.
        private static readonly Vector3 World1Origin = new Vector3(5600f, 0f, 5600f);
        private static readonly Vector3 World2Origin = new Vector3(5800f, 0f, 5800f);
        private static readonly Vector3 World3Origin = new Vector3(6000f, 0f, 6000f);

        [Test]
        public void World3SentinelIsGreenWithPaleGoldEyeAndThickerGreenBolt_World1And2Unaffected()
        {
            BackyardPath path1 = null, path2 = null, path3 = null;
            Sentinel sentinel1 = null, sentinel2 = null, sentinel3 = null;
            RobotEnemy target1 = null, target2 = null, target3 = null;
            SentinelBolt baselineBolt = null, bolt2 = null, bolt3 = null;
            try
            {
                // --- Baseline: an unmodified (SizeScale-only, World-1-style) bolt fired directly ---
                baselineBolt = SentinelBolt.Fire(Vector3.zero, Vector3.forward * 5f, speed: 20f);
                Transform baselineMesh = baselineBolt.transform.Find("Bolt");
                Assert.IsNotNull(baselineMesh, "test precondition: SentinelBolt must build a child named 'Bolt'");
                float crossSection1 = baselineMesh.GetComponent<MeshRenderer>().bounds.size.x;
                float trailWidth1 = baselineBolt.GetComponent<TrailRenderer>().widthMultiplier;
                Assert.That(crossSection1, Is.GreaterThan(0f), "test precondition: baseline bolt has zero cross-section");
                Object.DestroyImmediate(baselineBolt.gameObject);
                baselineBolt = null;

                // --- World 1 (resolved world index 0): water beam, never a bolt, unaffected by this ticket ---
                path1 = NewResolvedPath(0);
                sentinel1 = new GameObject("MV1069 Sentinel W1").AddComponent<Sentinel>();
                sentinel1.Init(World1Origin, maxHp: 60f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                target1 = NewTarget(World1Origin + Vector3.forward * 2f);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)
                InvokeUpdate(sentinel1);

                Assert.IsNotNull(sentinel1.GetComponentInChildren<WaterVfx>(true),
                    "World 1 (resolved world index 0) must still fire the WaterVfx beam");
                Assert.IsNull(Object.FindAnyObjectByType<SentinelBolt>(),
                    "World 1 must not spawn a SentinelBolt");

                Object.DestroyImmediate(path1.gameObject);
                path1 = null;

                // --- World 2 (resolved world index 1): byte-identical dark-red/red body, pale-gold
                // eye and 1.6x bolt — proving the style-table refactor didn't move World 2 ---
                path2 = NewResolvedPath(1);
                sentinel2 = new GameObject("MV1069 Sentinel W2").AddComponent<Sentinel>();
                sentinel2.Init(World2Origin, maxHp: 60f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                target2 = NewTarget(World2Origin + Vector3.forward * 2f);

                Physics.SyncTransforms();
                InvokeUpdate(sentinel2);

                var materials2 = (Material[])OwnedMaterialsField.GetValue(sentinel2);
                Color warm2 = materials2[0].GetColor("_BaseColor");
                Color accent2 = materials2[1].GetColor("_BaseColor");
                Assert.That(warm2.r, Is.EqualTo(0.847f).Within(0.01f), $"World 2 Warm red channel: {warm2.r:0.000}");
                Assert.That(warm2.g, Is.EqualTo(0.149f).Within(0.01f), $"World 2 Warm green channel: {warm2.g:0.000}");
                Assert.That(warm2.b, Is.EqualTo(0.110f).Within(0.01f), $"World 2 Warm blue channel: {warm2.b:0.000}");
                Assert.That(accent2.r, Is.EqualTo(1.000f).Within(0.01f), $"World 2 Accent red channel: {accent2.r:0.000}");
                Assert.That(accent2.g, Is.EqualTo(0.353f).Within(0.01f), $"World 2 Accent green channel: {accent2.g:0.000}");
                Assert.That(accent2.b, Is.EqualTo(0.263f).Within(0.01f), $"World 2 Accent blue channel: {accent2.b:0.000}");

                var body2 = (RobotBodies.Body)BodyField.GetValue(sentinel2);
                var eyeMpb2 = new MaterialPropertyBlock();
                body2.Eyes[0].GetPropertyBlock(eyeMpb2);
                Color eye2 = eyeMpb2.GetColor("_BaseColor");
                Assert.That(eye2.r, Is.EqualTo(1.0f).Within(0.01f), $"World 2 eye red channel: {eye2.r:0.000}");
                Assert.That(eye2.g, Is.EqualTo(0.86f).Within(0.01f), $"World 2 eye green channel: {eye2.g:0.000}");
                Assert.That(eye2.b, Is.EqualTo(0.62f).Within(0.01f), $"World 2 eye blue channel: {eye2.b:0.000}");

                bolt2 = Object.FindAnyObjectByType<SentinelBolt>();
                Assert.IsNotNull(bolt2, "World 2 sentinel did not fire a SentinelBolt");
                Transform boltMesh2 = bolt2.transform.Find("Bolt");
                float crossSection2 = boltMesh2.GetComponent<MeshRenderer>().bounds.size.x;
                float trailWidth2 = bolt2.GetComponent<TrailRenderer>().widthMultiplier;
                Color boltTint2 = boltMesh2.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.That(crossSection2, Is.EqualTo(crossSection1 * 1.6f).Within(crossSection1 * 1.6f * 0.05f),
                    $"World 2 bolt cross-section ({crossSection2:0.000}m) is not 1.6x the baseline's ({crossSection1:0.000}m)");
                Assert.That(trailWidth2, Is.EqualTo(trailWidth1 * 1.6f).Within(0.001f),
                    $"World 2 trail widthMultiplier ({trailWidth2:0.000}) is not 1.6x the baseline's ({trailWidth1:0.000})");
                Assert.That(boltTint2.r, Is.EqualTo(StormdrainLightKit.Red.r).Within(0.001f),
                    $"World 2 bolt colour must stay StormdrainLightKit.Red, unchanged by this ticket: {boltTint2.r:0.000}");
                Assert.That(boltTint2.g, Is.EqualTo(StormdrainLightKit.Red.g).Within(0.001f));
                Assert.That(boltTint2.b, Is.EqualTo(StormdrainLightKit.Red.b).Within(0.001f));

                Object.DestroyImmediate(bolt2.gameObject);
                bolt2 = null;
                Object.DestroyImmediate(path2.gameObject);
                path2 = null;

                // --- World 3 (resolved world index 2): new green body/accent, pale-gold eye, 1.6x green bolt ---
                path3 = NewResolvedPath(2);
                sentinel3 = new GameObject("MV1069 Sentinel W3").AddComponent<Sentinel>();
                sentinel3.Init(World3Origin, maxHp: 60f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                target3 = NewTarget(World3Origin + Vector3.forward * 2f);

                Physics.SyncTransforms();
                InvokeUpdate(sentinel3);

                var materials3 = (Material[])OwnedMaterialsField.GetValue(sentinel3);
                Color warm3 = materials3[0].GetColor("_BaseColor");
                Color accent3 = materials3[1].GetColor("_BaseColor");
                // #1FA84A / #6DFF8A
                Assert.That(warm3.r, Is.EqualTo(0.1216f).Within(0.02f), $"World 3 Warm red channel: {warm3.r:0.000}");
                Assert.That(warm3.g, Is.EqualTo(0.6588f).Within(0.02f), $"World 3 Warm green channel: {warm3.g:0.000}");
                Assert.That(warm3.b, Is.EqualTo(0.2902f).Within(0.02f), $"World 3 Warm blue channel: {warm3.b:0.000}");
                Assert.That(accent3.r, Is.EqualTo(0.4275f).Within(0.02f), $"World 3 Accent red channel: {accent3.r:0.000}");
                Assert.That(accent3.g, Is.EqualTo(1.0f).Within(0.02f), $"World 3 Accent green channel: {accent3.g:0.000}");
                Assert.That(accent3.b, Is.EqualTo(0.5412f).Within(0.02f), $"World 3 Accent blue channel: {accent3.b:0.000}");

                var body3 = (RobotBodies.Body)BodyField.GetValue(sentinel3);
                var eyeMpb3 = new MaterialPropertyBlock();
                body3.Eyes[0].GetPropertyBlock(eyeMpb3);
                Color eye3 = eyeMpb3.GetColor("_BaseColor");
                // #FFDB9E — pale gold, same reason World 2's eye is gold: a green eye vanishes on green body.
                Assert.That(eye3.r, Is.EqualTo(1.0f).Within(0.02f), $"World 3 eye red channel: {eye3.r:0.000}");
                Assert.That(eye3.g, Is.EqualTo(0.8588f).Within(0.02f), $"World 3 eye green channel: {eye3.g:0.000}");
                Assert.That(eye3.b, Is.EqualTo(0.6196f).Within(0.02f), $"World 3 eye blue channel: {eye3.b:0.000}");

                bolt3 = Object.FindAnyObjectByType<SentinelBolt>();
                Assert.IsNotNull(bolt3, "World 3 sentinel did not fire a SentinelBolt");
                Transform boltMesh3 = bolt3.transform.Find("Bolt");
                float crossSection3 = boltMesh3.GetComponent<MeshRenderer>().bounds.size.x;
                float trailWidth3 = bolt3.GetComponent<TrailRenderer>().widthMultiplier;
                Color boltTint3 = boltMesh3.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");

                Assert.That(crossSection3, Is.EqualTo(crossSection1 * 1.6f).Within(crossSection1 * 1.6f * 0.05f),
                    $"World 3 bolt cross-section ({crossSection3:0.000}m) is not 1.6x the baseline's ({crossSection1:0.000}m)");
                Assert.That(trailWidth3, Is.EqualTo(trailWidth1 * 1.6f).Within(0.001f),
                    $"World 3 trail widthMultiplier ({trailWidth3:0.000}) is not 1.6x the baseline's ({trailWidth1:0.000})");
                // #4DFF6A
                Assert.That(boltTint3.r, Is.EqualTo(0.3020f).Within(0.02f), $"World 3 bolt red channel: {boltTint3.r:0.000}");
                Assert.That(boltTint3.g, Is.EqualTo(1.0f).Within(0.02f), $"World 3 bolt green channel: {boltTint3.g:0.000}");
                Assert.That(boltTint3.b, Is.EqualTo(0.4157f).Within(0.02f), $"World 3 bolt blue channel: {boltTint3.b:0.000}");
            }
            finally
            {
                if (baselineBolt != null) Object.DestroyImmediate(baselineBolt.gameObject);
                if (bolt2 != null) Object.DestroyImmediate(bolt2.gameObject);
                if (bolt3 != null) Object.DestroyImmediate(bolt3.gameObject);
                if (path1 != null) Object.DestroyImmediate(path1.gameObject);
                if (path2 != null) Object.DestroyImmediate(path2.gameObject);
                if (path3 != null) Object.DestroyImmediate(path3.gameObject);
                if (sentinel1 != null) Object.DestroyImmediate(sentinel1.gameObject);
                if (sentinel2 != null) Object.DestroyImmediate(sentinel2.gameObject);
                if (sentinel3 != null) Object.DestroyImmediate(sentinel3.gameObject);
                if (target1 != null) Object.DestroyImmediate(target1.gameObject);
                if (target2 != null) Object.DestroyImmediate(target2.gameObject);
                if (target3 != null) Object.DestroyImmediate(target3.gameObject);
            }
        }
    }
}
