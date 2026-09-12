using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-785, "Stormdrain Surface Kit" (approved by Lee, 2026-09-12): World 2's sludge channel built as
    /// one flat, static, acid-green <c>Cube</c> — ten times brighter than the room and reading as a
    /// painted stripe, not a liquid. This fails to COMPILE on base commit 330b411:
    /// <c>StormdrainKit.SludgeBand</c>/<c>SludgeChevronMid</c>/<c>SludgeLipWidth</c>/<c>BuildFoamClump</c>
    /// and <c>SludgeFlowRig</c> do not exist there, and <c>DressSludgeTile</c> returns <c>void</c>.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values only (Rule 2,
    /// Tier 2): a built sludge tile actually carries 10 band / 24 chevron / 9 foam children; ticking the
    /// rig's own scroll by 1.0 s actually moves every band/foam by its own speed along the channel axis,
    /// and a band phased right at the channel's far edge actually wraps to the start rather than running
    /// past it; the lip material actually resolves at least 2.5x the channel material's luminance; two
    /// tiles at different world positions actually resolve different band phases at t=0; and the new
    /// production source actually contains no Animator, AnimationClip or tween-library type.
    /// </summary>
    public sealed class MV785SludgeFlowTests
    {
        [Test]
        public void SludgeFlow_BuildsScrollsWrapsAndLightsTheBanks_WithNoAnimatorOrTween()
        {
            BiomePalette previousPalette = MaterialLibrary.Palette;
            var host = new GameObject("MV785 host").transform;
            try
            {
                MaterialLibrary.Palette = BiomePalette.Stormdrain;
                MaterialLibrary.Clear();
                StormdrainKit.Clear();

                // ---- AC1: structural counts on one built sludge rect ----
                // A long, narrow channel (run=20, spacing well clear of the 0.35/0.12 m moved this test
                // drives) so no band/chevron/foam naturally sits within its own step of the wrap edge —
                // the wrap behaviour itself is proven separately, deterministically, below.
                const float width = 4f, depth = 20f;
                SludgeFlowRig rig = StormdrainKit.DressSludgeTile(host, Vector3.zero, width, depth, Vector3.forward, seed: 17);
                Assert.IsNotNull(rig, "DressSludgeTile must return the SludgeFlowRig driving its scroll");

                Transform root = rig.transform;
                Transform bands = root.Find("Bands");
                Transform chevrons = root.Find("Chevrons");
                Transform foam = root.Find("Foam");
                Assert.IsNotNull(bands, "'Sludge Dressing' must carry a 'Bands' group");
                Assert.IsNotNull(chevrons, "'Sludge Dressing' must carry a 'Chevrons' group");
                Assert.IsNotNull(foam, "'Sludge Dressing' must carry a 'Foam' group");
                Assert.AreEqual(10, bands.childCount, "one built sludge rect must carry 10 band children");
                Assert.AreEqual(24, chevrons.childCount, "one built sludge rect must carry 24 chevron children");
                Assert.AreEqual(9, foam.childCount, "one built sludge rect must carry 9 foam children");

                // ---- AC2 (generic case): driving the flow by 1.0s of AnimSequence time ----
                Vector3[] bandBefore = ReadLocalPositions(bands);
                Vector3[] foamBefore = ReadLocalPositions(foam);

                rig.Tick(1.0f);

                Vector3[] bandAfter = ReadLocalPositions(bands);
                Vector3[] foamAfter = ReadLocalPositions(foam);

                for (int i = 0; i < bandBefore.Length; i++)
                {
                    float moved = Vector3.Distance(bandBefore[i], bandAfter[i]);
                    Assert.AreEqual(0.35f, moved, 0.01f,
                        $"band {i} must move 0.35m +/- 0.01 along the channel axis in 1.0s (from {bandBefore[i]} to {bandAfter[i]})");
                }
                for (int i = 0; i < foamBefore.Length; i++)
                {
                    float moved = Vector3.Distance(foamBefore[i], foamAfter[i]);
                    Assert.AreEqual(0.12f, moved, 0.01f,
                        $"foam clump {i} must move 0.12m +/- 0.01 along the channel axis in 1.0s (from {foamBefore[i]} to {foamAfter[i]})");
                }

                // ---- AC3: the lip must resolve at least 2.5x the channel's own luminance ----
                // Resolved via Color.linear (Unity's own sRGB decode), the same method the ticket's own
                // Observation section uses ("luminance 0.61 against a floor of 0.062" resolves the OLD
                // MapRuntime.SludgeColor and BiomePalette.Stormdrain.GroundBase this exact way — 0.61 and
                // 0.062 match ResolvedLuminance on those two constants to two decimal places).
                //
                // The ticket's other AC3 clause ("the channel material's is between 2x and 4x the World 2
                // floor's") does not hold under this same, ticket-established method: ResolvedLuminance
                // (Sludge) = 0.0603 vs ResolvedLuminance(BiomePalette.Stormdrain.GroundBase) = 0.0643 —
                // the channel is marginally DARKER than the floor by raw luminance (ratio 0.94x), not 2-4x
                // brighter. This is a real gap in the ticket's own numbers, not a measurement artefact:
                // every other plausible "floor" reading in the shipped Stormdrain palette (GroundDry
                // 0.042, GroundAccent 0.087, Silt 0.091) also misses the 2-4x band relative to Sludge.
                // Flagged in the hand-off comment for Lee; not asserted here since it is not actually true
                // of the approved, unmodified colours, and Tier 1/3 rules forbid asserting a value known
                // to be false just because a ticket states it.
                float lipLuminance = ResolvedLuminance(StormdrainKit.SludgeBright);
                float channelLuminance = ResolvedLuminance(StormdrainKit.Sludge);
                Assert.That(lipLuminance, Is.GreaterThanOrEqualTo(channelLuminance * 2.5f),
                    $"lip luminance ({lipLuminance:F4}) must be at least 2.5x the channel's ({channelLuminance:F4})");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host.gameObject);
                MaterialLibrary.Palette = previousPalette;
                MaterialLibrary.Clear();
                StormdrainKit.Clear();
            }

            // ---- AC2 (wrap case): a band phased 0.1m from the far end must wrap to the start ----
            var wrapHost = new GameObject("MV785 wrap host").transform;
            try
            {
                var wrapBand = new GameObject("Wrap Band").transform;
                wrapBand.SetParent(wrapHost, false);
                var wrapRig = new GameObject("Wrap Rig").AddComponent<SludgeFlowRig>();
                wrapRig.transform.SetParent(wrapHost, false);

                const float run = 5f;
                float startPhase = run - 0.1f; // 0.1m short of the wrap boundary
                wrapRig.Configure(
                    Vector3.forward, run, new[] { wrapBand }, new[] { Vector3.zero }, new[] { startPhase },
                    Vector3.forward, run, Array.Empty<Transform>(), Array.Empty<Vector3>(), Array.Empty<float>());

                float startCoord = wrapBand.localPosition.z;
                Assert.AreEqual(startPhase - run * 0.5f, startCoord, 1e-4f,
                    "wrap band's own starting coordinate must resolve from its configured phase");

                // Unwrapped, 1.0s at 0.35 m/s would put it at startPhase + 0.35 = run + 0.25 — past the
                // far end. Wrapped, it must land near the start instead.
                wrapRig.Tick(1.0f);
                float coordAfter = wrapBand.localPosition.z;

                Assert.Less(coordAfter, -run * 0.5f + 0.3f,
                    $"a band 0.1m from the channel end must wrap to the start after crossing it in 1.0s, not run past it (resolved z={coordAfter:F3})");
                Assert.GreaterOrEqual(coordAfter, -run * 0.5f - 1e-3f,
                    $"a wrapped band must not land before the channel's own start (resolved z={coordAfter:F3})");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(wrapHost.gameObject);
            }

            // ---- AC4: two sludge rects at different world positions have different band phases at t=0 ----
            var rectAHost = new GameObject("MV785 rectA host").transform;
            var rectBHost = new GameObject("MV785 rectB host").transform;
            try
            {
                SludgeFlowRig rigA = StormdrainKit.DressSludgeTile(rectAHost, new Vector3(0f, 0f, 0f), 4f, 20f, Vector3.forward, seed: 17);
                SludgeFlowRig rigB = StormdrainKit.DressSludgeTile(rectBHost, new Vector3(37.3f, 0f, -12.1f), 4f, 20f, Vector3.forward, seed: 17);

                float band0AxialA = rigA.transform.Find("Bands").GetChild(0).localPosition.z;
                float band0AxialB = rigB.transform.Find("Bands").GetChild(0).localPosition.z;

                Assert.That(Mathf.Abs(band0AxialA - band0AxialB), Is.GreaterThan(1e-3f),
                    "two sludge rects at different world positions must resolve different band phases at t=0 " +
                    $"(rect A band0 z={band0AxialA:F3}, rect B band0 z={band0AxialB:F3})");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rectAHost.gameObject);
                UnityEngine.Object.DestroyImmediate(rectBHost.gameObject);
                MaterialLibrary.Clear();
                StormdrainKit.Clear();
            }

            // ---- AC5: no Animator, AnimationClip or tween-library type anywhere in the new code ----
            AssertSourceClean("Rendering", "SludgeFlowRig.cs");
            AssertSourceClean("Rendering", "StormdrainKit.cs");
        }

        private static float ResolvedLuminance(Color authoredSRgb)
        {
            Color lin = authoredSRgb.linear;
            return 0.2126f * lin.r + 0.7152f * lin.g + 0.0722f * lin.b;
        }

        private static Vector3[] ReadLocalPositions(Transform group)
        {
            var positions = new Vector3[group.childCount];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = group.GetChild(i).localPosition;
            return positions;
        }

        private static readonly string[] ForbiddenTypes =
        {
            "Animator", "AnimationClip", "DOTween", "LeanTween", "iTween", "Tweener",
        };

        private static readonly Regex CommentRegex = new Regex(
            @"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        private static void AssertSourceClean(params string[] relativeRuntimeParts)
        {
            string path = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime");
            foreach (string part in relativeRuntimeParts) path = Path.Combine(path, part);
            // Comments stripped first (the same idiom MV535RobotBodyOrderingTests already uses) — this
            // class's own doc comments explain the constraint in prose and would otherwise trip on their
            // own explanation of what the code must NOT do.
            string text = CommentRegex.Replace(File.ReadAllText(path), m => new string(' ', m.Length));

            foreach (string forbidden in ForbiddenTypes)
                Assert.IsFalse(text.Contains(forbidden),
                    $"'{path}' must not reference '{forbidden}' in code — MV-785 forbids Animator/.anim/tween-library types");
        }
    }
}
