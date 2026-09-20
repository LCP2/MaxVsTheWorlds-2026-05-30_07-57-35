using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-866 — World 2's floor dressing (StormdrainKit's silt/water stains, raised to 0.020-0.026
    /// for depth-buffer precision by MV-791) drew OVER Max's aim reticle and the sentinel rings,
    /// because the gameplay ground-mark ladder (reticle/shadow/ring/telegraph) still lived at its
    /// pre-MV-791 heights of 0.006-0.030 — entirely under the dressing it now shares the lawn with.
    ///
    /// Fails on base commit 68d962b (pre-MV-866): the reticle resolves to y=0.006 and the contact
    /// shadow to y=0.012, both under the 0.026 dressing ceiling, so the first two
    /// <c>Assert.Greater</c> calls below fail.
    ///
    /// Tier 2 (resolved values): every height asserted here is read off <c>transform.position.y</c>
    /// of a real, placed GameObject — the same <see cref="AimReticle.LateUpdate"/> and
    /// <see cref="GroundRing.Show"/> call paths production code drives — never off the authored
    /// constant that feeds it.
    /// </summary>
    public sealed class MV866GroundMarkHeightsTests
    {
        // World 2's dressing ceiling (StainLift + WaterMeniscusLift, MV-791): 0.020 + 0.006. Fixed
        // here as a literal, not a reference to GroundMarkHeights, so this test proves the actual
        // resolved numbers clear the dressing rather than merely agreeing with itself.
        private const float DressingCeiling = 0.026f;

        private static readonly MethodInfo AimReticleLateUpdate =
            typeof(AimReticle).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo AimReticleQuadGo =
            typeof(AimReticle).GetField("_quadGo", BindingFlags.NonPublic | BindingFlags.Instance);

        [Test]
        public void EveryGameplayGroundMarkClearsTheDressingCeiling_InTheDocumentedOrder()
        {
            GameObject reticleOwner = null;
            GroundRing shadow = null, ring = null, telegraph = null;

            try
            {
                // --- Aim reticle: build it for real and let its own LateUpdate place the quad ---
                reticleOwner = new GameObject("MV866_ReticleOwner");
                reticleOwner.transform.position = Vector3.zero;
                reticleOwner.transform.forward = Vector3.forward;
                var reticle = reticleOwner.AddComponent<AimReticle>();
                reticle.Init(reticleOwner.transform, 6f, 35f);
                AimReticleLateUpdate.Invoke(reticle, null);
                var reticleQuad = (GameObject)AimReticleQuadGo.GetValue(reticle);
                float reticleY = reticleQuad.transform.position.y;

                // --- Contact shadow + anchor ring: the same GroundRing.Create/Lift/Show sequence
                // GroundAnchorVfx.NextShadow/NextRing drive every frame for a real actor ---
                shadow = GroundRing.Create("MV866_ContactShadow");
                shadow.Lift = GroundAnchorTuning.ShadowLift;
                shadow.Show(Vector3.zero, 0.5f, GroundAnchorTuning.ContactShadow);
                float shadowY = shadow.transform.position.y;

                ring = GroundRing.Create("MV866_AnchorRing");
                ring.Lift = GroundAnchorTuning.RingLift;
                ring.Show(Vector3.zero, 0.85f, GroundAnchorTuning.PlayerRing);
                float ringY = ring.transform.position.y;

                // --- Danger telegraph: a default GroundRing, Lift left untouched ---
                telegraph = GroundRing.Create("MV866_Telegraph");
                telegraph.Show(Vector3.zero, 1f, Color.red);
                float telegraphY = telegraph.transform.position.y;

                Assert.Greater(reticleY, DressingCeiling,
                    $"the aim reticle resolves to y={reticleY}, still under World 2's floor dressing " +
                    "— a stain would draw over it");
                Assert.Greater(shadowY, DressingCeiling,
                    $"the contact shadow resolves to y={shadowY}, still under the floor dressing");
                Assert.Greater(ringY, DressingCeiling,
                    $"the anchor ring resolves to y={ringY}, still under the floor dressing — this is " +
                    "the sentinel-ring z-fight the ticket reports");
                Assert.Greater(telegraphY, DressingCeiling,
                    $"the danger telegraph resolves to y={telegraphY}, still under the floor dressing");

                Assert.Less(reticleY, shadowY,
                    "the reticle must draw under the contact shadow — the actor standing on it wins");
                Assert.Less(shadowY, ringY,
                    "the contact shadow must draw under its own anchor ring");
                Assert.Less(ringY, telegraphY,
                    "the anchor ring must draw under the danger telegraph — an always-on decoration " +
                    "must never cover the one mark the player has to react to");
            }
            finally
            {
                if (reticleOwner != null) Object.DestroyImmediate(reticleOwner);
                if (shadow != null) Object.DestroyImmediate(shadow.gameObject);
                if (ring != null) Object.DestroyImmediate(ring.gameObject);
                if (telegraph != null) Object.DestroyImmediate(telegraph.gameObject);
            }
        }
    }
}
