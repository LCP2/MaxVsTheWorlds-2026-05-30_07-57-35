using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-873: every sludge tile ticked itself unconditionally from its own <c>Update()</c>, so World 2
    /// spent 69 tiles x 43 moving pieces every frame regardless of where Max stood — a10 alone (38
    /// sludge rects) cost 1,634 of them every frame it was never even near. Fails to COMPILE on base
    /// commit 68d962b: <c>MaxWorlds.Rendering.SludgeFlowDirector</c> does not exist there (see the fix
    /// comment for the captured failure output).
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting MEASURED per-piece work (Rule
    /// 2/3 — not a presence check): a rig 10m from the player has its 43 pieces actually re-applied on
    /// every one of 60 director ticks (counted via <see cref="SludgeFlowRig.AppliedPieceCount"/>), a
    /// rig 300m away has none of its pieces applied across those same 60 ticks, and once the player
    /// walks to the far rig its resolved piece positions land exactly where continuous, ungated ticking
    /// across the same elapsed time would have put them — proving the gate banks elapsed time rather
    /// than freezing or dropping it.
    /// </summary>
    public sealed class MV873SludgeFlowDistanceGateTests
    {
        [Test]
        public void SludgeFlowDirector_TicksOnlyNearRigs_AndResumesGatedRigsWithNoJump()
        {
            const float width = 4f, depth = 20f;
            const int seed = 29;
            Vector3 flow = Vector3.forward;
            Vector3 nearCenter = new Vector3(10f, 0f, 0f); // 10m from the player
            Vector3 farCenter = new Vector3(300f, 0f, 0f); // 300m from the player
            const float dt = 1f / 60f;

            var nearHost = new GameObject("MV873 near host").transform;
            var farHost = new GameObject("MV873 far host").transform;
            var shadowHost = new GameObject("MV873 shadow host").transform;
            SludgeFlowRig nearRig = null, farRig = null;
            try
            {
                nearRig = StormdrainKit.DressSludgeTile(nearHost, nearCenter, width, depth, flow, seed);
                farRig = StormdrainKit.DressSludgeTile(farHost, farCenter, width, depth, flow, seed);
                Assert.IsNotNull(nearRig);
                Assert.IsNotNull(farRig);

                // Unity does not invoke OnEnable outside Play mode (the same reason RobotEnemy.Active
                // needs InvokeOnEnable in MV869PopulationReadoutTests etc.) — register both rigs with
                // the director exactly as production OnEnable would.
                InvokeOnEnable(nearRig);
                InvokeOnEnable(farRig);

                // ---- near ticks every frame, far ticks none, across 60 director ticks ----
                Vector3 playerPos = Vector3.zero;
                SludgeFlowRig.AppliedPieceCount = 0;

                for (int i = 0; i < 60; i++)
                {
                    int before = SludgeFlowRig.AppliedPieceCount;
                    SludgeFlowDirector.Tick(dt, playerPos);
                    int applied = SludgeFlowRig.AppliedPieceCount - before;
                    Assert.AreEqual(43, applied,
                        $"tick {i}: only the near rig (10m away) sits inside the 45m gate, so exactly its " +
                        "43 pieces (10 bands + 24 chevron legs + 9 foam) must be re-applied this tick - the " +
                        $"far rig (300m away) must contribute none (measured {applied} applied)");
                }

                // ---- the far rig resumes the instant the player reaches it ----
                int beforeResume = SludgeFlowRig.AppliedPieceCount;
                SludgeFlowDirector.Tick(dt, farCenter);
                int appliedOnResume = SludgeFlowRig.AppliedPieceCount - beforeResume;
                Assert.AreEqual(43, appliedOnResume,
                    $"the far rig must apply its own 43 pieces the moment it comes back into range (measured {appliedOnResume})");

                // The far rig has now lived through 60 gated ticks (banked, never applied) plus this one
                // applied tick - 61 * dt of elapsed time in total. An identically-configured rig ticked
                // directly, continuously and ungated for the same 61 frames must resolve to the SAME piece
                // positions - proving the bank resumed the scroll rather than jumping or freezing it.
                SludgeFlowRig shadowRig = StormdrainKit.DressSludgeTile(shadowHost, farCenter, width, depth, flow, seed);
                for (int i = 0; i < 61; i++) shadowRig.Tick(dt);

                AssertSamePositions(farRig.transform.Find("Bands"), shadowRig.transform.Find("Bands"), "Bands");
                AssertSamePositions(farRig.transform.Find("Chevrons"), shadowRig.transform.Find("Chevrons"), "Chevrons");
                AssertSamePositions(farRig.transform.Find("Foam"), shadowRig.transform.Find("Foam"), "Foam");
            }
            finally
            {
                // Unity does not fire OnDisable outside Play mode either (same quirk
                // MV809ReplicatorNeverShredsTests already works around) — unregister explicitly so this
                // test doesn't leave dangling entries in SludgeFlowDirector for whatever runs after it.
                if (nearRig != null) InvokeOnDisable(nearRig);
                if (farRig != null) InvokeOnDisable(farRig);
                Object.DestroyImmediate(nearHost.gameObject);
                Object.DestroyImmediate(farHost.gameObject);
                Object.DestroyImmediate(shadowHost.gameObject);
            }
        }

        private static void InvokeOnEnable(SludgeFlowRig rig) =>
            typeof(SludgeFlowRig).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rig, null);

        private static void InvokeOnDisable(SludgeFlowRig rig) =>
            typeof(SludgeFlowRig).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(rig, null);

        private static void AssertSamePositions(Transform gated, Transform ungated, string groupName)
        {
            Assert.IsNotNull(gated, $"gated rig must carry a '{groupName}' group");
            Assert.IsNotNull(ungated, $"ungated rig must carry a '{groupName}' group");
            Assert.AreEqual(ungated.childCount, gated.childCount, $"{groupName} child count must match");
            for (int i = 0; i < gated.childCount; i++)
            {
                Vector3 a = gated.GetChild(i).localPosition;
                Vector3 b = ungated.GetChild(i).localPosition;
                Assert.That(Vector3.Distance(a, b), Is.LessThan(1e-3f),
                    $"{groupName}/{i}: the resumed (gated) rig resolved {a} but continuous, ungated ticking " +
                    $"for the same elapsed time resolved {b} - the gate must not jump or freeze the scroll");
            }
        }
    }
}
