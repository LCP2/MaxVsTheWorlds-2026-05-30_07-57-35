using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-803, "Stormdrain Pass 4" review, approved by Lee 2026-09-15 ("the diagonal striped outlines
    /// should be bright enough to lift the overall design"). Before this ticket <see cref="StormdrainKit.Hazard"/>
    /// was a single flat colour used once (the gate's own stripe) — there was no striped banding
    /// anywhere, and no builder to make one. Fails on base commit f89c4e7: <c>StormdrainKit</c> carries
    /// no <c>BuildHazardBanding</c> method at all, so this file fails to compile — see the fix comment
    /// for the captured failure output.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting three RESOLVED values (Rule 2,
    /// Tier 2) over a real World 2 area's dressing pass: every yellow stripe's resolved material colour
    /// and emissive strength; the resolved pitch and lean angle of every run of stripes sharing one
    /// backing plate; and that every banding instance's own resolved world position lies within 0.6 m of
    /// something the ticket's own placement rule allows — a channel drop edge, a gate jamb, a
    /// Replicator, a pump housing or a junction box. Nothing here asserts an authored constant or a
    /// rendered pixel (Rule 3): pitch and lean both come off the actual built Transform, never the
    /// numbers that produced it.
    /// </summary>
    public sealed class MV803HazardBandingTests
    {
        private const float ProximityTolerance = 0.6f;
        private const float PitchExpected = 0.42f;
        private const float PitchTolerance = 0.03f;
        private const float LeanExpectedDeg = 34f;
        private const float LeanToleranceDeg = 2f;

        [Test]
        public void HazardBanding_StripesAreBrightAndOnPitch_AndOnlyMarksWhatCanHurtYou()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            var host = new GameObject("MV803 host").transform;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);

                // Replicators: AddComponent's own Awake never runs outside Play mode (same note
                // MV807ReplicatorQueueTests already carries) — the body, and this ticket's own base-band
                // banding with it, has to be built explicitly here.
                foreach (Replicator r in build.Replicators) r.Build();

                // Gate jambs: the banding only shows while locked -- lock every gate and skin it, the
                // same two calls BackyardPath.ApplyWorldMaterials makes for a real World 2 load.
                AreaGate[] gates = host.GetComponentsInChildren<AreaGate>(true);
                foreach (AreaGate gate in gates)
                {
                    gate.Locked = true;
                    gate.ApplyStormdrainGateSkin();
                }

                // ---- gather every anchor the ticket's own placement rule allows, as a BOUNDS, not a
                // pivot -- "within 0.6 m of a Replicator" means near its actual 2x2x1.5 m body, not
                // near whatever point its transform happens to be centred on. ----
                var anchors = new List<Bounds>();

                Transform sludgeHost = host.Find("Stormdrain Dressing/Sludge");
                Assert.IsNotNull(sludgeHost, "the sludge dressing host was never built");
                foreach (Transform t in sludgeHost.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Trough Wall A" && t.name != "Trough Wall B") continue;
                    var rend = t.GetComponent<Renderer>();
                    if (rend != null) anchors.Add(rend.bounds);
                }

                // Renderer.bounds, not Collider.bounds -- a batchmode EditMode run never runs the
                // physics loop, so a collider's bounds stay at their stale (pre-transform) PhysX state
                // until something calls Physics.SyncTransforms(); a renderer's bounds have no such trap.
                foreach (AreaGate gate in gates)
                {
                    var rend = gate.GetComponent<Renderer>();
                    anchors.Add(rend != null ? rend.bounds : new Bounds(gate.transform.position, Vector3.zero));
                }

                foreach (Replicator r in build.Replicators)
                {
                    var rend = r.GetComponent<Renderer>();
                    anchors.Add(rend != null ? rend.bounds : new Bounds(r.transform.position, Vector3.zero));
                }

                foreach (CoverPiece piece in build.Cover)
                {
                    if (piece.Cover.Dressing != CoverDressing.Shed && piece.Cover.Dressing != CoverDressing.Machinery) continue;
                    // The cover block's own resolved footprint (Rule 2: a resolved value off the actual
                    // built entity, not an authored constant) -- its Renderer is switched off by Dress()
                    // (only the art is replaced, per the kit's own contract), so Bounds is built directly
                    // from Cover rather than reading a disabled/stale component.
                    anchors.Add(new Bounds(piece.Cover.Center, piece.Cover.Size));
                }

                Transform overheadHost = host.Find("Stormdrain Dressing/Overhead");
                Assert.IsNotNull(overheadHost, "the overhead structure host was never built");
                foreach (Transform t in overheadHost.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Overhead Junction") continue;
                    Renderer[] rends = t.GetComponentsInChildren<Renderer>(true);
                    if (rends.Length == 0) continue;
                    Bounds b = rends[0].bounds;
                    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                    anchors.Add(b);
                }

                Assert.Greater(anchors.Count, 0, "no anchors resolved at all -- this test proves nothing");

                // ---- gather every banding instance built anywhere under this level ----
                List<Transform> bandingHosts = host.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name == "Hazard Banding")
                    .ToList();
                Assert.Greater(bandingHosts.Count, 0, "no hazard banding was built anywhere -- this test proves nothing");

                int stripeCount = 0;
                foreach (Transform bandingHost in bandingHosts)
                {
                    // ---- AC1c: placement -- every instance's centre is near something worth marking ----
                    float nearest = anchors.Min(a => Vector3.Distance(bandingHost.position, a.ClosestPoint(bandingHost.position)));
                    Assert.LessOrEqual(nearest, ProximityTolerance,
                        $"a Hazard Banding instance under {bandingHost.parent?.name} at {bandingHost.position} sits " +
                        $"{nearest:F2} m from the nearest channel drop edge / gate jamb / Replicator / pump " +
                        "housing / junction box -- hazard marks hazard, never decoration");

                    var stripes = new List<Transform>();
                    foreach (Transform child in bandingHost)
                        if (child.name.StartsWith("Stripe")) stripes.Add(child);

                    foreach (Transform stripe in stripes)
                    {
                        stripeCount++;
                        var rend = stripe.GetComponent<Renderer>();
                        Assert.IsNotNull(rend, $"{stripe.name} carries no renderer");
                        Material mat = rend.sharedMaterial;
                        Assert.IsNotNull(mat, $"{stripe.name} carries no material");

                        // ---- AC1a: resolved _BaseColor peak channel ----
                        Color baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.color;
                        float basePeak = Mathf.Max(baseColor.r, baseColor.g, baseColor.b);
                        Assert.GreaterOrEqual(basePeak, 0.90f,
                            $"{stripe.name}'s resolved _BaseColor peak channel {basePeak:F2} must be >= 0.90");

                        // ---- AC1a: resolved emissive contribution ----
                        Assert.IsTrue(mat.HasProperty("_EmissionColor"), $"{stripe.name}'s material carries no _EmissionColor");
                        Color emission = mat.GetColor("_EmissionColor");
                        float emissivePeak = Mathf.Max(emission.r, emission.g, emission.b);
                        Assert.GreaterOrEqual(emissivePeak, 0.20f,
                            $"{stripe.name}'s resolved emissive contribution {emissivePeak:F2} must be >= 0.20");

                        // ---- AC1b: resolved lean angle, off the run's own axis ----
                        Assert.IsNotNull(stripe.GetComponent<MeshFilter>()?.sharedMesh,
                            $"{stripe.name} carries no mesh to measure");
                        // The lean is a pure single-axis rotation, either about local Z (a run along
                        // local X) or local X (a run along local Z) -- BuildHazardBanding never does
                        // both, so whichever axis actually carries the resolved tilt IS the run's own
                        // axis. Reading the mesh's own footprint instead (comparing size.x vs size.z) is
                        // unreliable: a structural plate can legitimately be deeper than the stripe is
                        // long (a gate's own full doorway depth, ~1 m, against a leanLength under 1 m),
                        // which silently picked the wrong axis here until this test caught it.
                        Vector3 euler = stripe.localRotation.eulerAngles;
                        float zTilt = Mathf.Abs(Mathf.DeltaAngle(0f, euler.z));
                        float xTilt = Mathf.Abs(Mathf.DeltaAngle(0f, euler.x));
                        Vector3 longAxis = zTilt >= xTilt ? Vector3.right : Vector3.forward;
                        Vector3 leanedAxis = stripe.localRotation * longAxis;
                        float leanAngle = Vector3.Angle(longAxis, leanedAxis);
                        Assert.AreEqual(LeanExpectedDeg, leanAngle, LeanToleranceDeg,
                            $"{stripe.name}'s resolved long axis is {leanAngle:F1} degrees off the run's axis");
                    }

                    // ---- AC1b: resolved pitch between consecutive stripes on the same run ----
                    if (stripes.Count < 2) continue;
                    // Exactly one of local X/Z varies between siblings on a given run (see
                    // BuildHazardBanding's own "along" convention) -- summing both is a safe sort key
                    // regardless of which axis this particular run happens to use.
                    stripes.Sort((a, b) => (a.localPosition.x + a.localPosition.z)
                        .CompareTo(b.localPosition.x + b.localPosition.z));
                    for (int i = 1; i < stripes.Count; i++)
                    {
                        float pitch = Vector3.Distance(stripes[i].localPosition, stripes[i - 1].localPosition);
                        Assert.AreEqual(PitchExpected, pitch, PitchTolerance,
                            $"stripes {i - 1}->{i} under {bandingHost.parent?.name}/{bandingHost.name} are " +
                            $"{pitch:F3} m apart, not {PitchExpected} +/- {PitchTolerance}");
                    }
                }

                Assert.Greater(stripeCount, 0, "no stripe renderers were built anywhere -- this test proves nothing");
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
