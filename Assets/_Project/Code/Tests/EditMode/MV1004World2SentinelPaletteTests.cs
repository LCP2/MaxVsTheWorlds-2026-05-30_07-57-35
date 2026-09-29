using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1004: World 2's Sentinel wore Max's own blue (<c>Sentinel.BodyColor</c>/<c>BodyAccent</c>)
    /// and a bolt sized like every other world's (Lee, 2026-09-28: "Sentinels in World 2: make them
    /// red / dark red" / "make the Sentinels' laser in World 2 a bit thicker"). One consolidated test
    /// (testing policy MV-465 Rule 1) covers both facets of the same World-2-only palette/thickness
    /// change: (1) a Sentinel built with <see cref="BackyardPath.ResolvedWorldIndex"/> == 1 (World 2)
    /// resolves its body Warm/Accent materials to the ticket's dark-red/red and its eye to pale gold;
    /// (2) a bolt fired from that same World 2 Sentinel resolves a rendered cross-section 1.6x an
    /// unmodified (World-2-less) <see cref="SentinelBolt"/>'s, with the same 1.6x on its trail's
    /// <c>widthMultiplier</c> — the baseline every other world (including World 1's own fallback, since
    /// MV-914 restored World 1's water beam and never fires a <see cref="SentinelBolt"/> at all) still
    /// gets.
    ///
    /// Fails on base commit 8c86feb (today): <c>Sentinel.BuildBody</c> paints every world identically
    /// with <c>BodyColor</c>/<c>BodyAccent</c>/<c>EyeColor</c>, and <c>SentinelBolt.BuildVisual</c>
    /// scales every bolt's mesh and trail uniformly by <c>SizeScale</c> (0.7) with no world branch at
    /// all, so every assertion below is unsatisfiable — the World 2 sentinel reads exactly like every
    /// other world's.
    /// </summary>
    public sealed class MV1004World2SentinelPaletteTests
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
            var go = new GameObject("MV1004 Target Robot");
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
        /// — same reflect-set idiom <c>MV914SentinelWorldBeamTests</c> already uses.</summary>
        private static BackyardPath NewResolvedPath(int worldIndex)
        {
            var go = new GameObject($"MV1004 Path W{worldIndex}");
            var path = go.AddComponent<BackyardPath>();
            ResolvedWorldIndexProperty.SetValue(path, worldIndex);
            return path;
        }

        // Well clear of the origin/small coordinates other fixtures in this shared EditMode run use
        // for their own sentinel/robot/bolt fixtures — same spacing reasoning
        // MV914SentinelWorldBeamTests uses for its own pair.
        private static readonly Vector3 World2Origin = new Vector3(5200f, 0f, 5200f);

        [Test]
        public void World2SentinelIsRedWithPaleGoldEyeAndAOneSixtyPercentThickerBolt()
        {
            BackyardPath path2 = null;
            Sentinel sentinel2 = null;
            RobotEnemy target2 = null;
            SentinelBolt baselineBolt = null, bolt2 = null;
            try
            {
                // --- Baseline: an unmodified (SizeScale-only) SentinelBolt fired directly, same shape
                // every world got before this ticket and every world except World 2 still gets ---
                baselineBolt = SentinelBolt.Fire(Vector3.zero, Vector3.forward * 5f, speed: 20f);
                Transform baselineMesh = baselineBolt.transform.Find("Bolt");
                Assert.IsNotNull(baselineMesh, "test precondition: SentinelBolt must build a child named 'Bolt'");
                float crossSection1 = baselineMesh.GetComponent<MeshRenderer>().bounds.size.x;
                float trailWidth1 = baselineBolt.GetComponent<TrailRenderer>().widthMultiplier;
                Assert.That(crossSection1, Is.GreaterThan(0f), "test precondition: baseline bolt has zero cross-section");

                // Retired before firing World 2's own bolt below — otherwise still-alive, it's an
                // ambiguous second SentinelBolt in the scene and Object.FindAnyObjectByType<SentinelBolt>()
                // could return either.
                Object.DestroyImmediate(baselineBolt.gameObject);
                baselineBolt = null;

                // --- World 2 (resolved world index 1): dark-red/red body, pale-gold eye, 1.6x bolt ---
                path2 = NewResolvedPath(1);
                sentinel2 = new GameObject("MV1004 Sentinel W2").AddComponent<Sentinel>();
                sentinel2.Init(World2Origin, maxHp: 60f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);
                target2 = NewTarget(World2Origin + Vector3.forward * 2f);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)
                InvokeUpdate(sentinel2);

                var materials2 = (Material[])OwnedMaterialsField.GetValue(sentinel2);
                Color warm2 = materials2[0].GetColor("_BaseColor");
                Color accent2 = materials2[1].GetColor("_BaseColor");
                // MV-1024: brightened from (0.45, 0.07, 0.07)/(0.78, 0.18, 0.14) to #D8261C/#FF5A43 —
                // the original dark maroon sank into the floor under Stormdrain's dim lighting.
                Assert.That(warm2.r, Is.EqualTo(0.847f).Within(0.01f), $"World 2 Warm red channel: {warm2.r:0.000}");
                Assert.That(warm2.g, Is.EqualTo(0.149f).Within(0.01f), $"World 2 Warm green channel: {warm2.g:0.000}");
                Assert.That(warm2.b, Is.EqualTo(0.110f).Within(0.01f), $"World 2 Warm blue channel: {warm2.b:0.000}");
                Assert.That(accent2.r, Is.EqualTo(1.000f).Within(0.01f), $"World 2 Accent red channel: {accent2.r:0.000}");
                Assert.That(accent2.g, Is.EqualTo(0.353f).Within(0.01f), $"World 2 Accent green channel: {accent2.g:0.000}");
                Assert.That(accent2.b, Is.EqualTo(0.263f).Within(0.01f), $"World 2 Accent blue channel: {accent2.b:0.000}");

                var body2 = (RobotBodies.Body)BodyField.GetValue(sentinel2);
                var eyeMpb = new MaterialPropertyBlock();
                body2.Eyes[0].GetPropertyBlock(eyeMpb);
                Color eye2 = eyeMpb.GetColor("_BaseColor");
                Assert.That(eye2.r, Is.EqualTo(1.0f).Within(0.01f), $"World 2 eye red channel: {eye2.r:0.000}");
                Assert.That(eye2.g, Is.EqualTo(0.86f).Within(0.01f), $"World 2 eye green channel: {eye2.g:0.000}");
                Assert.That(eye2.b, Is.EqualTo(0.62f).Within(0.01f), $"World 2 eye blue channel: {eye2.b:0.000}");

                bolt2 = Object.FindAnyObjectByType<SentinelBolt>();
                Assert.IsNotNull(bolt2, "World 2 sentinel did not fire a SentinelBolt");
                Transform boltMesh2 = bolt2.transform.Find("Bolt");
                float crossSection2 = boltMesh2.GetComponent<MeshRenderer>().bounds.size.x;
                float trailWidth2 = bolt2.GetComponent<TrailRenderer>().widthMultiplier;

                Assert.That(crossSection2, Is.EqualTo(crossSection1 * 1.6f).Within(crossSection1 * 1.6f * 0.05f),
                    $"World 2 bolt cross-section ({crossSection2:0.000}m) is not 1.6x the baseline's ({crossSection1:0.000}m)");
                Assert.That(trailWidth2, Is.EqualTo(trailWidth1 * 1.6f).Within(0.001f),
                    $"World 2 trail widthMultiplier ({trailWidth2:0.000}) is not 1.6x the baseline's ({trailWidth1:0.000})");
            }
            finally
            {
                if (baselineBolt != null) Object.DestroyImmediate(baselineBolt.gameObject);
                if (bolt2 != null) Object.DestroyImmediate(bolt2.gameObject);
                if (path2 != null) Object.DestroyImmediate(path2.gameObject);
                if (sentinel2 != null) Object.DestroyImmediate(sentinel2.gameObject);
                if (target2 != null) Object.DestroyImmediate(target2.gameObject);
            }
        }
    }
}
