using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-474 — Max stopped walking. MV-451 replaced his hand-placed leg primitives with one fused
    /// static mesh (<see cref="MaxBody"/>), and in doing so dropped the hip pivots
    /// <c>MaxRig.TickRun</c> rotates every frame: the run-cycle math was never touched and kept
    /// computing a stride, but <c>_hips[i]</c> stayed null, so the (already null-guarded) write
    /// silently did nothing. <c>MaxRig.Build</c>'s own MV-451 doc comment named the drop explicitly.
    /// </summary>
    public sealed class MV474MaxWalkTests
    {
        private Transform _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("Root").transform;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root.gameObject);
        }

        /// <summary>The measured claim: a hip pivot <c>MaxBody</c> hands back has leg geometry hanging
        /// off it, and rotating that pivot — exactly what <c>TickRun</c> does every frame — moves that
        /// geometry a visible distance. Must fail to even compile on 5c6f938 (MV-451): that commit's
        /// <c>MaxBody.Build</c> takes no <c>hipY</c> and returns a bare <c>MeshRenderer[]</c>, which is
        /// the same "nothing left to hang off" the class doc describes for the regression itself.</summary>
        [Test]
        public void HipPivot_HasFootGeometryHangingOffIt_AndRotatingItMovesTheFoot()
        {
            const float hipY = 0.74f; // MaxRig.HipY — the waist height the rig builds the torso at.
            var palette = new MaxPalette(null, null, null, null, null, null, null, null, null, null, null, null, null);

            var body = MaxBody.Build(_root, palette, hipY);

            Assert.That(body.Hips, Has.Length.EqualTo(2),
                "MaxBody must hand back exactly two hip pivots — one per foot — for TickRun to drive.");

            var hip = body.Hips[0];
            var foot = hip.GetComponentInChildren<MeshRenderer>();
            Assert.That(foot, Is.Not.Null,
                "the hip pivot has no renderer under it. A pivot with nothing hanging off it is " +
                "exactly the MV-451 regression: TickRun turns it and nothing visible moves.");

            Vector3 rest = foot.transform.position;
            hip.localRotation = Quaternion.Euler(32f, 0f, 0f);   // TickRun's own swing, at legSwing's max
            Vector3 swung = foot.transform.position;

            Assert.That(Vector3.Distance(rest, swung), Is.GreaterThan(0.05f),
                "rotating the hip pivot barely moves the foot. At MaxRig's own hip height and swing " +
                "angle the step should travel several centimetres, or the walk will read as a twitch.");
        }
    }
}
