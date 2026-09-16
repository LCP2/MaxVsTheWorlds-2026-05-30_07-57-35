using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-815: Lee rejected the smooth ogive MV-805 built and the capsule before it -- "the LPPE bolt
    /// is literally arched, bowed like a drawn bow, curved across its travel axis." This proves the
    /// resolved crescent mesh (never an authored constant) is genuinely bowed, one smooth arc with no
    /// crease, a cross-section that peaks at the midpoint and vanishes at both tips, and that the
    /// Sentinel's own bolt (MV-806) stayed a straight, separate mesh rather than sharing Max's crescent.
    ///
    /// Fails on base commit b0dc6ee: <c>SeekerPulse.GetBoltMesh()</c> still returns the MV-805 ogive of
    /// revolution around Y -- a straight surface. Grouping its own vertices into 16 spine rings (this
    /// test's own analysis) resolves a maximum deviation from the tip-to-tip chord of ~0.000m, not the
    /// 0.20m +/- 0.03 bow this ticket requires, so the very first numeric assertion below fails.
    /// </summary>
    public sealed class MV815CrescentBoltTests
    {
        // MV-815 spec: "16 samples along the spine" -- the crescent mesh's own known ring structure,
        // used only to group its vertices back into spine samples, not to assert an authored constant.
        private const int SpineSamples = 16;

        [Test]
        public void MaxsBoltIsABowedCrescent_SentinelsStaysStraight()
        {
            Mesh crescent = SeekerPulse.GetBoltMesh();
            Mesh sentinelMesh = SentinelBolt.GetBoltMesh();

            Assert.AreNotSame(crescent, sentinelMesh,
                "Max's bolt mesh and the Sentinel's must be different instances -- MV-815 gives the " +
                "Sentinel its own straight mesh instead of sharing Max's crescent");

            // --- spine + cross-section radius, recovered per spine sample by averaging each ring's
            // own vertices -- the cross-section's symmetric angular sampling cancels its own XZ
            // contribution to exactly zero, leaving only the true spine position in the local XZ
            // (ground) plane.
            (Vector3 point, float radius)[] spine = RingSpine(crescent, SpineSamples);

            Vector2 tip1 = new Vector2(spine[0].point.x, spine[0].point.z);
            Vector2 tip2 = new Vector2(spine[SpineSamples - 1].point.x, spine[SpineSamples - 1].point.z);
            float chordLength = Vector2.Distance(tip1, tip2);
            Vector2 chordDir = (tip2 - tip1) / chordLength;
            Vector2 chordNormal = new Vector2(-chordDir.y, chordDir.x);

            var deviation = new float[SpineSamples];
            for (int i = 0; i < SpineSamples; i++)
            {
                Vector2 p = new Vector2(spine[i].point.x, spine[i].point.z);
                deviation[i] = Vector2.Dot(p - tip1, chordNormal);
            }

            // --- bullet 1: genuinely bowed -- 0.20m +/- 0.03, peaking within 10% of the chord's own
            // midpoint (measured along the chord itself, not by sample index).
            float maxDev = 0f;
            int peakIndex = 0;
            for (int i = 0; i < SpineSamples; i++)
            {
                if (Mathf.Abs(deviation[i]) > maxDev) { maxDev = Mathf.Abs(deviation[i]); peakIndex = i; }
            }

            Assert.That(maxDev, Is.EqualTo(0.20f).Within(0.03f),
                $"the crescent's max deviation from its own tip-to-tip chord ({maxDev:0.000}m) is not " +
                "the ticket's 0.20m +/- 0.03 sagitta");

            Vector2 peakPoint = new Vector2(spine[peakIndex].point.x, spine[peakIndex].point.z);
            float alongChord = Vector2.Dot(peakPoint - tip1, chordDir);
            Assert.That(Mathf.Abs(alongChord - chordLength * 0.5f), Is.LessThanOrEqualTo(chordLength * 0.1f),
                $"the peak deviation lands {alongChord:0.000}m along a {chordLength:0.000}m chord -- " +
                "more than 10% from the chord's own midpoint");

            // --- bullet 2: one smooth arc -- rises then falls monotonically, no step exceeds 20% of
            // the peak (a creased or kinked form fails this).
            bool descended = false;
            for (int i = 1; i < SpineSamples; i++)
            {
                float prev = Mathf.Abs(deviation[i - 1]);
                float cur = Mathf.Abs(deviation[i]);
                if (cur < prev - 1e-4f) descended = true;
                else if (descended && cur > prev + 1e-4f)
                    Assert.Fail($"deviation rose again at sample {i} ({prev:0.0000} -> {cur:0.0000}) " +
                        "after it had already started falling -- not one continuous arc");

                float step = Mathf.Abs(cur - prev);
                Assert.That(step, Is.LessThanOrEqualTo(maxDev * 0.2f),
                    $"deviation step between adjacent spine samples ({step:0.0000}) exceeds 20% of the " +
                    $"peak ({maxDev * 0.2f:0.0000}) -- a creased or kinked spine, not one smooth curve");
            }

            // --- bullet 3: cross-section radius peaks at the spine's own midpoint, reaches 0 at both
            // tips.
            Assert.That(spine[0].radius, Is.LessThanOrEqualTo(0.01f),
                $"cross-section radius at the first tip must be ~0, was {spine[0].radius:0.000}m");
            Assert.That(spine[SpineSamples - 1].radius, Is.LessThanOrEqualTo(0.01f),
                $"cross-section radius at the last tip must be ~0, was {spine[SpineSamples - 1].radius:0.000}m");

            float peakRadius = 0f;
            int radiusPeakIndex = 0;
            for (int i = 0; i < SpineSamples; i++)
                if (spine[i].radius > peakRadius) { peakRadius = spine[i].radius; radiusPeakIndex = i; }
            Assert.That(Mathf.Abs(radiusPeakIndex - (SpineSamples - 1) / 2f), Is.LessThanOrEqualTo(1.5f),
                $"cross-section radius peaks at sample {radiusPeakIndex}, not near the spine's own midpoint " +
                $"(sample {(SpineSamples - 1) / 2f})");

            // --- bullet 4: the Sentinel's own bolt stays straight -- chord deviation under 0.02m.
            // Grouped by rounded Y (its lathe's own revolve axis) rather than by a fixed ring count,
            // since its own row/segment counts aren't this test's to know.
            (Vector3 point, float radius)[] sentinelSpine = SpineByHeight(sentinelMesh);
            Vector3 sTip1 = sentinelSpine[0].point;
            Vector3 sTip2 = sentinelSpine[sentinelSpine.Length - 1].point;
            Vector3 sChordDir = (sTip2 - sTip1).normalized;
            float sMaxDev = 0f;
            foreach ((Vector3 point, float _) in sentinelSpine)
            {
                Vector3 toPoint = point - sTip1;
                Vector3 alongProjection = Vector3.Dot(toPoint, sChordDir) * sChordDir;
                sMaxDev = Mathf.Max(sMaxDev, (toPoint - alongProjection).magnitude);
            }
            Assert.That(sMaxDev, Is.LessThanOrEqualTo(0.02f),
                $"the Sentinel's own bolt deviates {sMaxDev:0.000}m from a straight chord -- it must stay straight");
        }

        /// <summary>Groups a mesh's vertices into <paramref name="ringCount"/> equal, contiguous
        /// chunks -- matching <c>SeekerPulse.BuildCrescentBoltMesh</c>'s own build order, one ring of
        /// cross-section vertices per spine sample -- and averages each ring. Because the
        /// cross-section's own angular samples are symmetric around the spine, this cancels their own
        /// XZ contribution to zero, leaving the spine's own resolved position. Ring radius is the
        /// farthest any vertex in that ring sits from its own ring's centroid.</summary>
        private static (Vector3 point, float radius)[] RingSpine(Mesh mesh, int ringCount)
        {
            Vector3[] verts = mesh.vertices;
            Assert.AreEqual(0, verts.Length % ringCount,
                $"test precondition: {verts.Length} vertices don't divide evenly into {ringCount} rings");
            int ringSize = verts.Length / ringCount;

            var result = new (Vector3, float)[ringCount];
            for (int i = 0; i < ringCount; i++)
            {
                Vector3 sum = Vector3.zero;
                for (int j = 0; j < ringSize; j++) sum += verts[i * ringSize + j];
                Vector3 centroid = sum / ringSize;

                float maxR = 0f;
                for (int j = 0; j < ringSize; j++)
                    maxR = Mathf.Max(maxR, Vector3.Distance(verts[i * ringSize + j], centroid));

                result[i] = (centroid, maxR);
            }
            return result;
        }

        /// <summary>Same idea as <see cref="RingSpine"/>, but for a mesh whose own ring structure this
        /// test doesn't hardcode -- groups by rounded Y instead, since the Sentinel's own straight bolt
        /// is a lathe of revolution around Y. Same grouping idiom <c>MV805FireBoltTests</c> used to
        /// prove its own now-superseded ogive was one smooth curve.</summary>
        private static (Vector3 point, float radius)[] SpineByHeight(Mesh mesh)
        {
            var byHeight = new SortedDictionary<float, List<Vector3>>();
            foreach (Vector3 v in mesh.vertices)
            {
                float h = Mathf.Round(v.y * 2000f) / 2000f;
                if (!byHeight.TryGetValue(h, out List<Vector3> list)) byHeight[h] = list = new List<Vector3>();
                list.Add(v);
            }

            var result = new List<(Vector3, float)>();
            foreach (KeyValuePair<float, List<Vector3>> kv in byHeight)
            {
                Vector3 sum = Vector3.zero;
                foreach (Vector3 v in kv.Value) sum += v;
                Vector3 centroid = sum / kv.Value.Count;

                float maxR = 0f;
                foreach (Vector3 v in kv.Value) maxR = Mathf.Max(maxR, Vector3.Distance(v, centroid));

                result.Add((centroid, maxR));
            }
            return result.ToArray();
        }
    }
}
