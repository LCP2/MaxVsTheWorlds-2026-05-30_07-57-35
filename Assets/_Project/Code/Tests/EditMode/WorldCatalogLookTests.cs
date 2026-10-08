using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Audio;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1141: before this ticket, a world's palette/kit/sentinel style/music were each picked by a
    /// raw world-number comparison scattered across five files, so a fourth world inserted at index 2
    /// would silently inherit the Reef's palette, kit, sentinels and music. This proves the single
    /// replacement table (<see cref="WorldCatalog"/>) is both internally sound (AC1a) and actually wired
    /// into every real entry point those five files used to decide for themselves (AC1b), without
    /// moving a single value Worlds 1-3 already ship (AC1c), and that the test seam restores them
    /// exactly (AC1d).
    ///
    /// Fails on the commit this ticket starts from: <see cref="WorldCatalog"/> does not exist there, so
    /// this fails to COMPILE (quoted in the fix comment).
    /// </summary>
    public sealed class WorldCatalogLookTests
    {
        private static readonly MethodInfo UpdateMethod =
            typeof(Sentinel).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo RobotOnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo ApplyWorldMaterialsMethod =
            typeof(BackyardPath).GetMethod("ApplyWorldMaterials", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly PropertyInfo ResolvedWorldIndexProperty =
            typeof(BackyardPath).GetProperty(nameof(BackyardPath.ResolvedWorldIndex));

        private static readonly FieldInfo OwnedMaterialsField =
            typeof(Sentinel).GetField("_ownedMaterials", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo BodyField =
            typeof(Sentinel).GetField("_body", BindingFlags.NonPublic | BindingFlags.Instance);

        [Test]
        public void CatalogValidatesAndResolvesTheProbeWorld_WithoutMovingTheShippedThree()
        {
            // --- AC1a: the shipped rows are internally sound. ---
            Assert.IsEmpty(WorldCatalog.Validate(), "the shipped rows must carry no problems");
            Assert.AreEqual(WorldLibrary.Count, WorldCatalog.Count,
                "WorldLibrary.Count must still agree with the catalog it now reads");

            // Fetched by Id, not by literal index — the stable name is what a reorder must not move.
            WorldDefinition backyard = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Backyard));
            WorldDefinition stormdrain = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Stormdrain));
            WorldDefinition reef = WorldCatalog.Get(WorldCatalog.IndexOf(WorldIds.Reef));

            // A palette/look/style built here that differ from all three shipped rows above, so a test
            // failure that fell back to a shipped row (the pre-MV-1141 bug this ticket fixes) is
            // visibly distinguishable from one that correctly reached the probe.
            BiomePalette probePalette = BiomePalette.Reef;
            probePalette.Tint = new Color(0.1234f, 0.5678f, 0.9f);
            BackyardLook probeLook = BackyardLook.Stormdrain;
            probeLook.KeyIntensity = 12.34f;
            var probeStyle = new SentinelStyle(
                body: new Color(0.9f, 0.1f, 0.9f), accent: new Color(0.1f, 0.9f, 0.9f),
                eye: new Color(0.9f, 0.9f, 0.1f), emissionFactor: 0.5f,
                boltColor: new Color(0.1f, 0.1f, 0.9f), boltThicknessScale: 2.5f);

            var probe = new WorldDefinition
            {
                Id = "probe",
                ConfigKey = "probe_config",
                Palette = probePalette,
                Look = probeLook,
                Kit = WorldKit.Garden,
                GateSkin = WorldGateSkin.None,
                Style = probeStyle,
                FiresWaterBeam = false,
                Music = MusicWorld.Stormdrain,
                RingAlphaScale = 1f,
            };

            BiomePalette previousPalette = MaterialLibrary.Palette;
            BackyardLook previousLook = BackyardLighting.ActiveLook;
            Sentinel.ResetRegistry();

            GameObject host = null, pathGo = null, lightingGo = null, sentinelPathGo = null, sentinelGo = null, targetGo = null;
            SentinelBolt bolt = null;
            try
            {
                WorldCatalog.UseForTests(new List<WorldDefinition> { backyard, stormdrain, reef, probe });

                // --- AC1b: for world index 3, through the real code paths. ---
                host = new GameObject("MV1141 Probe Host");
                pathGo = new GameObject("MV1141 BackyardPath");
                var path = pathGo.AddComponent<BackyardPath>();
                ApplyWorldMaterialsMethod.Invoke(path, new object[] { 3, host.transform, new List<CoverPiece>() });

                Assert.IsTrue(MaterialLibrary.Palette.Equals(probePalette),
                    "BackyardPath's own material pass must apply the probe's palette for world index 3");
                Assert.AreEqual(0, host.transform.childCount,
                    "world index 3 must run neither the Reef kit nor the Stormdrain dressing — it has no kit of its own");

                // The look BackyardLighting ends up applying — through its own Awake, not by reading
                // back the field BackyardPath just set, which would only prove the assignment happened.
                lightingGo = new GameObject("MV1141 BackyardLighting");
                var lighting = lightingGo.AddComponent<BackyardLighting>();
                typeof(BackyardLighting).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(lighting, null);
                Assert.IsTrue(lighting.Look.Equals(probeLook),
                    "BackyardLighting's own Awake must end up applying the probe's look for world index 3");

                Assert.AreEqual(MusicWorld.Stormdrain, MusicDirector.ResolveWorld(3),
                    "MusicDirector must resolve the probe's own music (the Stormdrain row's), not a clamped Reef fallback");

                // Destroyed before the Sentinel check below creates its OWN BackyardPath — two
                // BackyardPath instances alive at once would leave FindFirstObjectByType<BackyardPath>
                // (which Sentinel.Init uses to resolve its world) to pick between them undefined, and
                // this one's own ResolvedWorldIndex was never set to 3 (it defaults to 0).
                Object.DestroyImmediate(pathGo);
                pathGo = null;
                Object.DestroyImmediate(lightingGo);
                lightingGo = null;

                sentinelPathGo = new GameObject("MV1141 Sentinel Path");
                var sentinelPath = sentinelPathGo.AddComponent<BackyardPath>();
                ResolvedWorldIndexProperty.SetValue(sentinelPath, 3);

                Vector3 origin = new Vector3(7100f, 0f, 7100f);
                sentinelGo = new GameObject("MV1141 Sentinel");
                var sentinel = sentinelGo.AddComponent<Sentinel>();
                sentinel.Init(origin, maxHp: 60f, range: 7f, fireInterval: 0.6f,
                    moveSpeed: 0f, standoffDistance: 2.5f, followTarget: null);

                targetGo = new GameObject("MV1141 Target Robot");
                targetGo.transform.position = origin + new Vector3(2f, 0f, 0f);
                targetGo.AddComponent<CharacterController>();
                var target = targetGo.AddComponent<RobotEnemy>();
                RobotOnEnableMethod.Invoke(target, null);

                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (see GateSolidityTests)
                UpdateMethod.Invoke(sentinel, null);

                Assert.IsNull(sentinel.GetComponentInChildren<WaterVfx>(true),
                    "the probe world must fire the bolt, never the World-1-only water beam");
                bolt = Object.FindAnyObjectByType<SentinelBolt>();
                Assert.IsNotNull(bolt, "the probe-world sentinel must fire a SentinelBolt");

                Transform boltMesh = bolt.transform.Find("Bolt");
                Assert.IsNotNull(boltMesh, "test precondition: SentinelBolt must build a child named 'Bolt'");
                Color boltTint = boltMesh.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
                Assert.That(boltTint.r, Is.EqualTo(probeStyle.BoltColor.r).Within(0.01f), "probe bolt red channel");
                Assert.That(boltTint.g, Is.EqualTo(probeStyle.BoltColor.g).Within(0.01f), "probe bolt green channel");
                Assert.That(boltTint.b, Is.EqualTo(probeStyle.BoltColor.b).Within(0.01f), "probe bolt blue channel");

                // Compared as a RATIO against an unscaled (thicknessScale: 1) baseline bolt — same idiom
                // MV1069World3SentinelPaletteTests uses — because the resolved widthMultiplier also
                // carries SentinelBolt's own SizeScale and CombatVfxTuning.LppeBolt().TrailWidth, neither
                // of which this ticket's probe should have to know about or assert as a literal.
                float trailWidth = bolt.GetComponent<TrailRenderer>().widthMultiplier;
                SentinelBolt baselineBolt = SentinelBolt.Fire(Vector3.zero, Vector3.forward * 5f, speed: 20f);
                float baselineTrailWidth = baselineBolt.GetComponent<TrailRenderer>().widthMultiplier;
                Object.DestroyImmediate(baselineBolt.gameObject);
                Assert.That(trailWidth, Is.EqualTo(baselineTrailWidth * probeStyle.BoltThicknessScale).Within(baselineTrailWidth * probeStyle.BoltThicknessScale * 0.05f),
                    $"the probe bolt's trail width ({trailWidth:0.000}) is not {probeStyle.BoltThicknessScale}x the baseline's ({baselineTrailWidth:0.000})");

                var ownedMaterials = (Material[])OwnedMaterialsField.GetValue(sentinel);
                Color warm = ownedMaterials[0].GetColor("_BaseColor");
                Assert.That(warm.r, Is.EqualTo(probeStyle.Body.r).Within(0.01f), "probe sentinel body red channel");
                Assert.That(warm.g, Is.EqualTo(probeStyle.Body.g).Within(0.01f), "probe sentinel body green channel");
                Assert.That(warm.b, Is.EqualTo(probeStyle.Body.b).Within(0.01f), "probe sentinel body blue channel");

                var body = (RobotBodies.Body)BodyField.GetValue(sentinel);
                var eyeMpb = new MaterialPropertyBlock();
                body.Eyes[0].GetPropertyBlock(eyeMpb);
                Color eye = eyeMpb.GetColor("_BaseColor");
                Assert.That(eye.r, Is.EqualTo(probeStyle.Eye.r).Within(0.01f), "probe sentinel eye red channel");
                Assert.That(eye.g, Is.EqualTo(probeStyle.Eye.g).Within(0.01f), "probe sentinel eye green channel");
                Assert.That(eye.b, Is.EqualTo(probeStyle.Eye.b).Within(0.01f), "probe sentinel eye blue channel");

                // --- AC1c: worlds 0, 1 and 2 still resolve the real shipped rows, unmoved. ---
                Assert.AreSame(backyard, WorldCatalog.Get(0), "world index 0 must still resolve the shipped Backyard row");
                Assert.AreSame(stormdrain, WorldCatalog.Get(1), "world index 1 must still resolve the shipped Stormdrain row");
                Assert.AreSame(reef, WorldCatalog.Get(2), "world index 2 must still resolve the shipped Reef row");
            }
            finally
            {
                WorldCatalog.ResetForTests();
                MaterialLibrary.Palette = previousPalette;
                BackyardLighting.ActiveLook = previousLook;
                Sentinel.ResetRegistry();

                if (bolt != null) Object.DestroyImmediate(bolt.gameObject);
                if (targetGo != null) Object.DestroyImmediate(targetGo);
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                if (sentinelPathGo != null) Object.DestroyImmediate(sentinelPathGo);
                if (lightingGo != null) Object.DestroyImmediate(lightingGo);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
                if (host != null) Object.DestroyImmediate(host);
            }

            // --- AC1d: ResetForTests (called in the finally block above) restores the shipped rows:
            // same count, same ids, in the same order, as before UseForTests. ---
            Assert.AreEqual(3, WorldCatalog.Count, "ResetForTests must restore exactly the three shipped rows");
            Assert.AreEqual(WorldIds.Backyard, WorldCatalog.Get(0).Id);
            Assert.AreEqual(WorldIds.Stormdrain, WorldCatalog.Get(1).Id);
            Assert.AreEqual(WorldIds.Reef, WorldCatalog.Get(2).Id);
        }
    }
}
