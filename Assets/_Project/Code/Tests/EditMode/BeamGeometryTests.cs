using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>The Gunner's laser (MV-293) is only fair if it can actually be dodged — these pin the
    /// two ways to do it: side-step out of the beam's width, or not be standing in its (locked)
    /// direction at all.</summary>
    public sealed class BeamGeometryTests
    {
        [Test]
        public void APointDeadAhead_IsHit()
        {
            Assert.IsTrue(BeamGeometry.Hits(Vector3.zero, Vector3.forward, range: 10f, halfWidth: 0.6f,
                point: new Vector3(0f, 0f, 5f)));
        }

        [Test]
        public void SteppingSideways_OutOfTheBeamsWidth_Dodges()
        {
            Assert.IsFalse(BeamGeometry.Hits(Vector3.zero, Vector3.forward, range: 10f, halfWidth: 0.6f,
                point: new Vector3(2f, 0f, 5f)), "2 m off the beam's centreline must not still be hit");
        }

        [Test]
        public void JustInsideTheHalfWidth_StillHits()
        {
            Assert.IsTrue(BeamGeometry.Hits(Vector3.zero, Vector3.forward, range: 10f, halfWidth: 0.6f,
                point: new Vector3(0.5f, 0f, 5f)));
        }

        [Test]
        public void BeyondRange_Misses()
        {
            Assert.IsFalse(BeamGeometry.Hits(Vector3.zero, Vector3.forward, range: 10f, halfWidth: 0.6f,
                point: new Vector3(0f, 0f, 15f)), "the beam has a fixed max range, it isn't infinite");
        }

        [Test]
        public void BehindTheShooter_Misses()
        {
            Assert.IsFalse(BeamGeometry.Hits(Vector3.zero, Vector3.forward, range: 10f, halfWidth: 0.6f,
                point: new Vector3(0f, 0f, -3f)), "a locked beam doesn't reach backward");
        }
    }
}
