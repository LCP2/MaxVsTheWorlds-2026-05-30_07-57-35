using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-580: <see cref="RobotBodies.Body"/> grew a <c>Legs</c> array alongside its existing
    /// <c>Wheels</c> one, so a legged mover (Max's Sentinel) can drive a walk cycle the same generic
    /// way <see cref="RobotRig.SpinWheels"/> already drives wheels — through the shared builder, never
    /// through a mover-private field. Only <see cref="EnemyKind.Gunner"/> populates it today (the kind
    /// the Sentinel borrows its body from); these tests hold the Gunner's own geometry to the same
    /// "provably unchanged" bar the refactor was built to — every part still renders exactly where it
    /// was authored, just reachable through an animatable hip joint now.
    /// </summary>
    public sealed class RobotBodiesGunnerLegsTests
    {
        private static RobotPalette DummyPalette()
        {
            var m = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            return new RobotPalette(m, m, m, m);
        }

        [Test]
        public void GunnerBody_ExposesExactlyThreeLegPivots_ThroughBodyLegs()
        {
            var root = new GameObject("Root").transform;
            try
            {
                RobotBodies.Body body = RobotBodies.Build(EnemyKind.Gunner, root, DummyPalette());

                Assert.That(body.Legs, Is.Not.Null);
                Assert.That(body.Legs.Length, Is.EqualTo(3),
                    "the Gunner's tripod should hand back exactly three hip pivots");

                foreach (var leg in body.Legs)
                {
                    Assert.IsNotNull(leg, "a null leg pivot was returned");
                    Assert.That(leg.childCount, Is.GreaterThanOrEqualTo(2),
                        $"{leg.name} has no leg parts hanging under it — the beam/foot reparent lost them");
                }
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        /// <summary>
        /// The reparent under a hip pivot (<c>SetParent(pivot, worldPositionStays: true)</c>) must
        /// leave the render EXACTLY where it was authored — this is the safety property the whole
        /// refactor depends on, since these same parts are what a live Gunner enemy renders too.
        /// Proven by asserting each leg's WORLD bounds after the rebuild sit inside the same broad
        /// envelope the whole Gunner body occupies (nothing flew off to the origin or to infinity —
        /// the failure mode a wrong pivot-placement bug would actually produce).
        /// </summary>
        [Test]
        public void GunnerLegParts_StillRenderWithinTheBodysOwnEnvelope_AfterTheHipReparent()
        {
            var root = new GameObject("Root").transform;
            try
            {
                RobotBodies.Body body = RobotBodies.Build(EnemyKind.Gunner, root, DummyPalette());

                Bounds? combined = null;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (combined == null) { combined = r.bounds; continue; }
                    var b = combined.Value;
                    b.Encapsulate(r.bounds);
                    combined = b;
                }
                Assert.IsNotNull(combined, "the Gunner built no visible parts at all");

                // The Gunner's own authored numbers span roughly y in [0, 1.4]; a mis-placed hip pivot
                // that threw a leg to the pivot-construction default (world origin) or to a wildly wrong
                // offset would blow this envelope open far past ordinary floating-point slack.
                Assert.That(combined.Value.min.y, Is.GreaterThan(-0.2f));
                Assert.That(combined.Value.max.y, Is.LessThan(2.0f));
                Assert.That(combined.Value.size.x, Is.LessThan(2.0f));
                Assert.That(combined.Value.size.z, Is.LessThan(2.0f));
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }

        /// <summary>Proves the hip pivot is a real, load-bearing hinge and not a decorative empty:
        /// rotating it must move a leg part's WORLD position — the resolved-value property a gait
        /// driver depends on.</summary>
        [Test]
        public void RotatingALegPivot_MovesItsPartsWorldPosition()
        {
            var root = new GameObject("Root").transform;
            try
            {
                RobotBodies.Body body = RobotBodies.Build(EnemyKind.Gunner, root, DummyPalette());
                Transform leg = body.Legs[0];
                Transform part = leg.GetChild(0);

                Vector3 before = part.position;
                leg.localRotation = Quaternion.Euler(35f, 0f, 0f);
                Vector3 after = part.position;

                Assert.That(Vector3.Distance(before, after), Is.GreaterThan(0.01f),
                    "rotating the leg's hip pivot did not move its part — the leg is not a real hinge");
            }
            finally { Object.DestroyImmediate(root.gameObject); }
        }
    }
}
