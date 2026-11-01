using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1336 (the one new test, Rule 1): Lee's iPhone screenshot showed the Mothership Hangar's
    /// hazard-yellow lines and spinning Orbitals blooming into blinding bands bright enough to wash out
    /// Max and the nearby Sentries. Proves AC2's three resolved numbers (MV-465 Tier 2) plus the Light
    /// Missile emissive cap change item 3 also asks for:
    ///
    /// 1. A hazard lane line's resolved emissive colour, computed from the LITERAL 0.45 the ticket pins
    ///    (not <see cref="ShipKit.NeonStrength"/> read back) — fails on base commit a34528b's 0.75.
    /// 2. Orbitals' resolved trail/reach-ring colours stay &lt;= 1.0 per channel, and the reach ring's
    ///    resolved alpha is 0.3 — fails on base commit (ring alpha is 0.55 there).
    /// 3. Light Missile's resolved core colour at POWER 8 stays &lt;= 1.44 per channel (the deck's own
    ///    1.2 bloom threshold + 20%) — fails on base commit (1.6).
    /// </summary>
    public sealed class MV1336ShipGlowCapTests
    {
        private const float DeckBloomThreshold = 1.2f; // BackyardLook's own Ship-deck override (MV-1332)
        private const float MaxChannelCeiling = DeckBloomThreshold * 1.2f; // change item 3's own "+20%"

        [SetUp]
        public void SetUp()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.UseWorld(WorldCatalog.IndexOf(WorldIds.Mothership)); // s_orb/s_rad/s_odm live only here
        }

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.ResetForTests();
            LightMissileBall.DestroyPoolForTests();
        }

        [Test]
        public void HazardLaneLineOrbitalsAndLightMissileAllResolveUnderTheirMV1336Caps()
        {
            AssertHazardLaneLineDimmedToPinnedNeonStrength();
            AssertOrbitalsTrailAndRingResolveUnderCap();
            AssertLightMissileCoreResolvesUnderBloomCeiling();
        }

        // ---- 1. hazard-yellow lane/edge line, literal 0.45 (not ShipKit.NeonStrength read back) ------

        private static void AssertHazardLaneLineDimmedToPinnedNeonStrength()
        {
            const float pinnedNeonStrength = 0.45f; // the ticket's own Change item 1
            var area = new Rect(0f, 0f, 16f, 11f);
            LogAssert.ignoreFailingMessages = true; // MaterialLibrary's one-time shader-resolve log line
            GameObject pad = ShipKit.BuildLaunchPadHazardStripes(null, area);
            LogAssert.ignoreFailingMessages = false;
            try
            {
                Color hazardYellow = new Color(224f / 255f, 182f / 255f, 48f / 255f);
                Color boosted = hazardYellow * 12.0f; // ShipKit's own private Hdr(..., 12.0f)
                boosted.a = 1f; // Hdr() forces alpha back to 1 after the *12 multiply touches it too
                Color expected = new Color(boosted.r * pinnedNeonStrength, boosted.g * pinnedNeonStrength,
                    boosted.b * pinnedNeonStrength, boosted.a);

                Transform neon = pad.transform.Find("Hazard N Neon");
                Assert.IsNotNull(neon, "expected a 'Hazard N Neon' child under the hazard stripes root");
                var renderer = neon.GetComponent<Renderer>();
                Assert.IsNotNull(renderer, "'Hazard N Neon' has no renderer");
                Material mat = renderer.sharedMaterial;
                Color actual = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                              : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;

                const float tol = 0.01f;
                Assert.LessOrEqual(Mathf.Abs(actual.r - expected.r), tol,
                    $"'Hazard N Neon' R: expected {expected.r:F4} (NeonStrength=0.45), resolved {actual.r:F4}");
                Assert.LessOrEqual(Mathf.Abs(actual.g - expected.g), tol,
                    $"'Hazard N Neon' G: expected {expected.g:F4} (NeonStrength=0.45), resolved {actual.g:F4}");
                Assert.LessOrEqual(Mathf.Abs(actual.b - expected.b), tol,
                    $"'Hazard N Neon' B: expected {expected.b:F4} (NeonStrength=0.45), resolved {actual.b:F4}");
            }
            finally { Object.DestroyImmediate(pad); }
        }

        // ---- 2. Orbitals: trail/reach-ring colour <= 1.0/channel, ring alpha 0.3 ----------------------

        private static void AssertOrbitalsTrailAndRingResolveUnderCap()
        {
            WeaponSystemState.SecondaryKind = SecondaryKind.Orbitals;
            RigState.RestoreSnapshot(new Dictionary<string, int> { { "s_orb", 4 }, { "s_odm", 5 } },
                new[] { "SECONDARY" });

            var maxGo = new GameObject("Max");
            var orbitals = maxGo.AddComponent<Orbitals>();
            var visuals = maxGo.AddComponent<OrbitalsVisuals>();
            try
            {
                LogAssert.ignoreFailingMessages = true; // VfxMaterials' one-time shader-resolve log line
                visuals.Tick();
                LogAssert.ignoreFailingMessages = false;

                Assert.Greater(orbitals.OrbCount, 0, "test precondition: at least one orb must be live");

                Color trail = visuals.ResolvedTrailColor(0);
                Assert.LessOrEqual(trail.r, 1.0f, $"orb trail R must be <=1.0 (no HDR boost), was {trail.r:F3}");
                Assert.LessOrEqual(trail.g, 1.0f, $"orb trail G must be <=1.0 (no HDR boost), was {trail.g:F3}");
                Assert.LessOrEqual(trail.b, 1.0f, $"orb trail B must be <=1.0 (no HDR boost), was {trail.b:F3}");

                Color ring = visuals.ResolvedReachRingColor;
                Assert.LessOrEqual(ring.r, 1.0f, $"reach ring R must be <=1.0 (no HDR boost), was {ring.r:F3}");
                Assert.LessOrEqual(ring.g, 1.0f, $"reach ring G must be <=1.0 (no HDR boost), was {ring.g:F3}");
                Assert.LessOrEqual(ring.b, 1.0f, $"reach ring B must be <=1.0 (no HDR boost), was {ring.b:F3}");
                // LineRenderer vertex colours quantize to byte precision (0.3 -> 77/255 = 0.302), so the
                // tolerance covers that 1/255 step rather than demanding exact float equality.
                Assert.AreEqual(0.3f, ring.a, 0.004f, $"reach ring alpha must resolve to 0.3, was {ring.a:F3}");
            }
            finally { Object.DestroyImmediate(maxGo); }
        }

        // ---- 3. Light Missile: resolved core colour <= deck bloom threshold + 20% ---------------------

        private static void AssertLightMissileCoreResolvesUnderBloomCeiling()
        {
            LogAssert.ignoreFailingMessages = true; // VfxMaterials' one-time shader-resolve log line
            LightMissileBall ball;
            try
            {
                ball = LightMissileBall.Spawn(Vector3.zero, Vector3.forward, speed: 9f, damage: 30f,
                    trackingBase: 170f, areaIndex: 0, powerLevel: 8);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.IsNotNull(ball, "test precondition: the pool must have room for a ball at POWER 8");

            Color core = ball.ResolvedCoreColor;
            Assert.LessOrEqual(core.r, MaxChannelCeiling,
                $"Light Missile core R must be <= {MaxChannelCeiling:F2} (deck bloom threshold +20%), was {core.r:F3}");
            Assert.LessOrEqual(core.g, MaxChannelCeiling,
                $"Light Missile core G must be <= {MaxChannelCeiling:F2} (deck bloom threshold +20%), was {core.g:F3}");
            Assert.LessOrEqual(core.b, MaxChannelCeiling,
                $"Light Missile core B must be <= {MaxChannelCeiling:F2} (deck bloom threshold +20%), was {core.b:F3}");
        }
    }
}
