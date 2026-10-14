using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1170 (the one new test, per CC_AUTONOMY's testing policy): World 2's a31 boss (Sludgequeen)
    /// is authored 20% smaller (Lee, 11 Oct 2026: "W2 boss is too big. Can't see top. Reduce by a
    /// reasonable number, maybe 20%.") so her crown stays on screen when she closes to contact.
    /// <see cref="SludgequeenRig"/> scales its whole body by
    /// <c>boss.transform.localScale.x / AuthoredBodyWidth</c> (6), so this is purely a
    /// <c>world2_config.json</c> data change -- the rig, legs, footfall rings and fitted collider all
    /// follow the new authored width automatically. Her contact reach/standoff are derived live from
    /// the resolved collider radius (<c>SludgequeenBoss.ContactReachTo</c>), not a fixed distance, so
    /// they follow too; nothing else in the fight (lob scatter, puddle/splash radii, health, timings)
    /// is authored against her body size, so nothing else needs retuning.
    ///
    /// Fails on base commit 0a2d759 (this ticket's own starting point, a31's boss still authors
    /// <c>{"w": 6, "d": 6}</c>): "resolved rig scale must be 0.8, measured 1.000 Expected: 0.8f But
    /// was: 1.0f" -- quoted in the fix comment.
    /// </summary>
    public sealed class MV1170SludgequeenSizeTests
    {
        [Test]
        public void A31Sludgequeen_ResolvesTo80PercentScale_AndIsShorterThanTheAuthoredBaseline()
        {
            // Same BuildBody collider-strip [Error] noise every full-World2-build EditMode test in this
            // suite carries (see MV1140World2TenAreasTests's own note).
            LogAssert.ignoreFailingMessages = true;

            // The baseline: a bare boss built at the rig's own reference width (6 m) -- what every
            // Sludgequeen build resolved to before this ticket, same fixture MV1124SludgequeenBodyTests
            // uses for the same reason.
            float baselineHeight = BuildAndMeasureHeight(6f);

            // The real build: World 2's shipped a31 boss, through the real loader/builder.
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            Camera[] suppressedCameras = CameraTestUtil.SuppressAmbientMainCameras();
            // Other EditMode tests in this suite build their own full World 2 (or bare Sludgequeen)
            // rigs and, unlike this test's own Cleanup, don't always sweep the unparented rig GameObject
            // back out afterward (it doesn't live under the map root they destroy) -- so a stray rig can
            // already be sitting in the scene before this test ever runs. Snapshot what's already here
            // and diff against it below, rather than assuming the one this build creates is the only one.
            var preExistingRigs = new System.Collections.Generic.HashSet<SludgequeenRig>(
                Object.FindObjectsByType<SludgequeenRig>(FindObjectsSortMode.None));
            var host = new GameObject("MV1170 Map Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, host.transform);
                Assert.AreEqual(1, built.Bosses.Count, "World 2 must build exactly one boss actor");

                SludgequeenRig rig = Object.FindObjectsByType<SludgequeenRig>(FindObjectsSortMode.None)
                    .FirstOrDefault(r => !preExistingRigs.Contains(r));
                Assert.IsNotNull(rig, "a31's boss must build a SludgequeenRig");

                Assert.AreEqual(0.8f, rig.transform.localScale.x, 0.001f,
                    $"resolved rig scale must be 0.8, measured {rig.transform.localScale.x:F3}");

                float newHeight = RenderedHeight(rig);
                float ratio = newHeight / baselineHeight;
                Assert.LessOrEqual(ratio, 0.82f,
                    $"rendered bounds height must be at most 82% of the baseline's {baselineHeight:F2} m " +
                    $"(authored at the rig's own 6 m reference width), measured {newHeight:F2} m ({ratio:P1})");
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (var stray in Object.FindObjectsByType<SludgequeenRig>(FindObjectsSortMode.None))
                    Object.DestroyImmediate(stray.gameObject);
                CameraTestUtil.RestoreAmbientMainCameras(suppressedCameras);
                SludgequeenBoss.ResetRegistry();
            }
        }

        /// <summary>Builds a bare Sludgequeen boss+rig at <paramref name="bodyWidth"/> (same
        /// <c>GameObject.CreatePrimitive(Cube)</c> fixture <c>MV1124SludgequeenBodyTests</c> uses) and
        /// returns the rig's combined rendered bounds height, tearing everything down before returning.</summary>
        private static float BuildAndMeasureHeight(float bodyWidth)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var strayCollider = go.GetComponent<BoxCollider>();
            if (strayCollider != null) Object.DestroyImmediate(strayCollider);
            go.transform.localScale = Vector3.one * bodyWidth;
            var boss = go.AddComponent<SludgequeenBoss>();

            SludgequeenRig rig = SludgequeenRig.CreateFor(boss);
            float height = RenderedHeight(rig);

            Object.DestroyImmediate(rig.gameObject);
            Object.DestroyImmediate(go);
            SludgequeenBoss.ResetRegistry();
            return height;
        }

        private static float RenderedHeight(SludgequeenRig rig)
        {
            var renderers = rig.GetComponentsInChildren<MeshRenderer>();
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.size.y;
        }
    }
}
