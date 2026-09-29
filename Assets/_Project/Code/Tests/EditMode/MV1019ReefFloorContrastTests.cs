using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1019: World 3's floor and crates read as near-black against the robots' own colouring
    /// (floor linear luminance ~0.015, crates ~0.008 — darker than the floor they sit on) because the
    /// actual rendered floor material (<see cref="WorldMaterials.M_ShipFloor"/>, built from
    /// <see cref="WorldMaterials.ReefShipFloor"/>) and the crates' swept <see cref="SurfaceKind.Prop"/>
    /// tone were both authored at the same near-black hull value. Lee (2026-09-29): the floor changes,
    /// not the robots.
    ///
    /// Asserts RESOLVED state only, through the real load path <see cref="MV745World3HullDressingTests"/>
    /// already proves out (<see cref="MapRuntime.Build"/> -> <see cref="WorldMaterials.Apply"/> ->
    /// <see cref="ReefKit.DressHull"/> -> <see cref="ReefDressing.DressCover"/>): the floor renderer's
    /// own <c>sharedMaterial.color</c>, every crate body renderer's own <c>sharedMaterial.color</c>,
    /// and a Reef-skinned <see cref="RobotRig"/>'s own <see cref="RobotRig.CurrentBodyColor"/> against a
    /// snapshot of what it wore before this ticket — never an authored constant compared to itself.
    ///
    /// Fails before this fix: floor luminance sits at ~0.015 (below the 0.022 floor this test asserts),
    /// and at least one crate body reads darker than the floor rather than >=1.6x brighter.
    /// </summary>
    public sealed class MV1019ReefFloorContrastTests
    {
        [Test]
        public void ReefFloorAndCratesReadLighterThanTheDefect_WhileRobotsAreUntouched()
        {
            WorldConfig cfg3 = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(cfg3, "World 3's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg3, out MapData map3, out string reason3), reason3);

            var root3 = new GameObject("MV1019 World3 Probe Root");
            RobotEnemy robotEnemy = null;
            try
            {
                // Exactly BackyardPath.Awake's own order (mirrors MV745World3HullDressingTests):
                // geometry, the world's palette sweep, the Reef-only hull pass, then cover dressing.
                MapBuild built3 = MapRuntime.Build(map3, root3.transform);
                var wm = new GameObject("WorldMaterials").AddComponent<WorldMaterials>();
                wm.Apply(BiomePalette.ForWorld(2));
                ReefKit.DressHull(root3.transform);
                ReefDressing.DressCover(root3.transform, built3.Cover);

                // --- AC1a: the floor's resolved luminance must clear the readability floor. ---
                Renderer floor = FindNamed(root3.transform, "Map Floor");
                Assert.IsNotNull(floor, "expected World 3's built map to include its floor slab");
                float floorLuma = ResolvedLuminance(ResolvedBaseColor(floor.sharedMaterial));
                Assert.That(floorLuma, Is.GreaterThanOrEqualTo(0.022f),
                    $"floor resolved luminance ({floorLuma:F4}) must be >= 0.022 — the near-black void MV-1019 fixes");

                // --- AC1b/AC1c: every crate body must be clearly lighter than, never darker than, the floor. ---
                int crateCount = 0;
                foreach (CoverPiece piece in built3.Cover)
                {
                    if (piece.Cover.Dressing != CoverDressing.None || piece.Body == null) continue;
                    crateCount++;

                    var crateRenderer = piece.Body.GetComponent<Renderer>();
                    Assert.IsNotNull(crateRenderer, $"crate '{piece.Body.name}' must keep its own renderer");
                    float crateLuma = ResolvedLuminance(ResolvedBaseColor(crateRenderer.sharedMaterial));

                    Assert.That(crateLuma, Is.GreaterThanOrEqualTo(floorLuma * 1.6f),
                        $"crate '{piece.Body.name}' resolved luminance ({crateLuma:F4}) must be >= 1.6x the floor's ({floorLuma:F4})");
                    Assert.That(crateLuma, Is.GreaterThanOrEqualTo(floorLuma),
                        $"crate '{piece.Body.name}' ({crateLuma:F4}) must never read darker than the floor ({floorLuma:F4})");
                }
                Assert.Greater(crateCount, 0, "precondition: World 3's own map must author at least one bare (crate) cover piece");

                // --- AC2: a Reef-skinned robot's body colour is byte-identical to before this ticket. ---
                // Rusher's reef body is WorldMaterials.ReefHazard (RobotRig.BuildMaterials) — untouched
                // by this ticket's palette/material changes; snapshotting it here as a fixed literal
                // (not compared against ReefHazard itself, which would just re-assert the same source)
                // is what makes this an actual regression guard on RobotRig's resolved output.
                var archetype = EnemyArchetype.Of(EnemyKind.Rusher).WithOverride(new WorldEnemyOverride { skin = "reef" });
                var robotGo = GameObject.CreatePrimitive(
                    archetype.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
                var cc = robotGo.AddComponent<CharacterController>();
                cc.height = 1f;
                cc.radius = 0.4f;
                robotEnemy = robotGo.AddComponent<RobotEnemy>();
                robotEnemy.Apply(archetype);
                robotGo.SetActive(true);

                var rig = robotGo.AddComponent<RobotRig>();
                InvokeEnsureBuilt(rig);

                Color expectedReefRusherBody = new Color(1f, 0.5451f, 0.1804f); // #FF8B2E, WorldMaterials.ReefHazard as of this ticket
                Assert.That(Vector4.Distance(expectedReefRusherBody, rig.CurrentBodyColor), Is.LessThan(0.01f),
                    $"a Reef-skinned robot's body colour must be unchanged by this ticket (expected {expectedReefRusherBody}, was {rig.CurrentBodyColor})");
            }
            finally
            {
                Object.DestroyImmediate(root3);
                if (robotEnemy != null)
                {
                    LogAssert.ignoreFailingMessages = true;
                    try { Object.DestroyImmediate(robotEnemy.gameObject); }
                    finally { LogAssert.ignoreFailingMessages = false; }
                }
            }
        }

        /// <summary>The colour a Reef surface material actually renders (MV713ReefKitTests's own
        /// idiom): <c>Material.color</c> only ever wraps a shader's <c>_Color</c> property, which
        /// URP/Lit does not have — every Reef material here is built against <c>_BaseColor</c>
        /// instead (<c>WorldMaterials.ReefMaterial</c>), so reading <c>.color</c> silently comes back
        /// black rather than the colour actually on screen.</summary>
        private static Color ResolvedBaseColor(Material m) =>
            m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;

        /// <summary>Rec709 luma over the engine-resolved linear channels (<see cref="Color.linear"/>),
        /// the same idiom <c>MV783StormdrainPaletteTests.ResolvedLuminance</c> uses.</summary>
        private static float ResolvedLuminance(Color authoredSRgb)
        {
            Color lin = authoredSRgb.linear;
            return 0.2126f * lin.r + 0.7152f * lin.g + 0.0722f * lin.b;
        }

        private static Renderer FindNamed(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t.GetComponent<Renderer>();
            return null;
        }

        /// <summary>Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode — same
        /// limitation <c>RobotSkinDiagnosticsTests.InvokeEnsureBuilt</c> works around.</summary>
        private static void InvokeEnsureBuilt(RobotRig rig)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }
    }
}
