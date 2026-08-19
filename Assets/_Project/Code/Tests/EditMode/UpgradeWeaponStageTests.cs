using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Upgrades;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The upgrade-screen hero weapon (YT-140). The look — lighting, composition — is Lee's call off a
    /// render, but the structure has to hold: it renders to a texture the screen can show, it bolts on
    /// the parts already installed plus the new one, and — this being a pile of runtime primitives — it
    /// never ships magenta and never carries a collider that would foul the world.
    ///
    /// MV-464: moved from PlayMode. <see cref="UpgradeWeaponStage.Create"/> calls its own
    /// <c>Build()</c> synchronously (Awake never runs in edit mode, so nothing here ever depended on
    /// it), and the part-prop collider strip in <c>WeaponPartArt</c> is
    /// <c>Application.isPlaying</c>-gated (MV-304) to use <c>DestroyImmediate</c> outside play mode —
    /// so the collider assertion below is exact with no frame wait.
    /// </summary>
    public sealed class UpgradeWeaponStageTests
    {
        private UpgradeWeaponStage _stage;

        [SetUp] public void SetUp() => UpgradeState.Reset();

        [TearDown]
        public void TearDown()
        {
            if (_stage != null) Object.DestroyImmediate(_stage.gameObject);
            UpgradeState.Reset();
        }

        [Test]
        public void RendersToATexture_AndAssemblesTheInstalledPartsPlusTheNewOne()
        {
            // Two already on the weapon; a third being installed now.
            UpgradeState.Install(PartKind.AugmentationHarness);
            UpgradeState.Install(PartKind.AccelerationEngine);

            _stage = UpgradeWeaponStage.Create(null);
            Assert.IsNotNull(_stage.Texture, "the stage has no render texture for the screen to show.");
            Assert.IsTrue(_stage.Texture.IsCreated(), "the render texture was never created.");

            _stage.Show(PartKind.PowerNozzle);

            // Base sprayer + 2 installed + the new one — plenty of renderers, none of them magenta.
            var renderers = _stage.GetComponentsInChildren<MeshRenderer>(true);
            Assert.Greater(renderers.Length, 8, "the assembled weapon is nearly empty.");
            foreach (var r in renderers)
            {
                Assert.IsNotNull(r.sharedMaterial, $"'{r.name}' draws nothing.");
                string shader = r.sharedMaterial.shader.name;
                Assert.That(shader,
                    Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds").Or.StartWith("Sprites"),
                    $"'{r.name}' wears '{shader}' — magenta in the build.");
            }

            Assert.IsEmpty(_stage.GetComponentsInChildren<Collider>(true),
                "the weapon stage carries a collider — it would foul the world it is hidden inside.");
        }

        [Test]
        public void TheNewPartMovesOntoItsMount_OverTheFitWindow()
        {
            _stage = UpgradeWeaponStage.Create(null);
            _stage.Show(PartKind.PowerNozzle);
            Assert.IsTrue(_stage.HasNewPart, "the new part was not staged.");

            _stage.Tick(0f, 0.45f, 0.45f);
            Vector3 start = _stage.NewPartLocalPosition;

            // Past the fit window — it should have travelled toward the weapon centre (its mount).
            _stage.Tick(1.2f, 0.45f, 0.45f);
            Vector3 seated = _stage.NewPartLocalPosition;

            Assert.Less(seated.magnitude, start.magnitude,
                "the new part never moved onto its mount — the fit animation is dead.");
        }
    }
}
