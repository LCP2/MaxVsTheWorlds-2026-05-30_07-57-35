using MaxWorlds.VFX;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// YT-108. Each of these is a way the door can be wrong that a screenshot does not show you:
    /// a door on the wall facing the fence still opens and closes convincingly, a ramp that lifts a
    /// robot into the air looks fine until you notice its feet, and a shutter that lets robots
    /// through at 20% open reads as robots walking through a closed door only if you catch the frame.
    /// </summary>
    public class FactoryDoorGeometryTests
    {
        private static int IndexOf(Vector3 face)
        {
            for (int i = 0; i < FactoryDoorGeometry.Faces.Length; i++)
                if (FactoryDoorGeometry.Faces[i] == face) return i;
            return -1;
        }

        // --- which wall the door goes on ---

        [Test]
        public void DoorGoesOnTheWallWithRoomToWalkOut()
        {
            var clearances = new float[4];
            clearances[IndexOf(Vector3.left)] = 12f;   // the opening onto the lawn
            clearances[IndexOf(Vector3.right)] = 4f;
            clearances[IndexOf(Vector3.forward)] = 3f;
            clearances[IndexOf(Vector3.back)] = 3f;

            int face = FactoryDoorGeometry.ChooseFace(clearances, blocked: null);

            Assert.That(FactoryDoorGeometry.Faces[face], Is.EqualTo(Vector3.left));
        }

        [Test]
        public void ABlockedFaceIsSkippedEvenWithTheMostClearance()
        {
            // MV-1107: a raycast-only probe never sees a colliderless prop (a decorative pipe run, a
            // kerb) sitting in a face's own ramp footprint — resolved renderer bounds do, and that face
            // must lose even though its raw clearance reads best. No player involved: the face choice
            // must never depend on where the player happens to stand at build time.
            var clearances = new float[4];
            clearances[IndexOf(Vector3.left)] = 12f;
            clearances[IndexOf(Vector3.right)] = 9f;
            clearances[IndexOf(Vector3.forward)] = 3f;
            clearances[IndexOf(Vector3.back)] = 3f;

            var blocked = new bool[4];
            blocked[IndexOf(Vector3.left)] = true; // a pipe run with no collider sits right there

            int face = FactoryDoorGeometry.ChooseFace(clearances, blocked);

            Assert.That(FactoryDoorGeometry.Faces[face], Is.EqualTo(Vector3.right));
        }

        [Test]
        public void EveryFaceBlockedStillPicksTheMostOpenOne()
        {
            // A door has to go somewhere even if every candidate footprint reads occupied.
            var clearances = new float[4];
            clearances[IndexOf(Vector3.left)] = 9f;
            clearances[IndexOf(Vector3.right)] = 4f;
            clearances[IndexOf(Vector3.forward)] = 3f;
            clearances[IndexOf(Vector3.back)] = 3f;

            var blocked = new[] { true, true, true, true };

            int face = FactoryDoorGeometry.ChooseFace(clearances, blocked);

            Assert.That(FactoryDoorGeometry.Faces[face], Is.EqualTo(Vector3.left));
        }

        [Test]
        public void NoPlayerYetStillPicksTheOpenWall()
        {
            var clearances = new float[4];
            clearances[IndexOf(Vector3.forward)] = 10f;
            clearances[IndexOf(Vector3.back)] = 2f;
            clearances[IndexOf(Vector3.left)] = 2f;
            clearances[IndexOf(Vector3.right)] = 2f;

            int face = FactoryDoorGeometry.ChooseFace(clearances, blocked: null);

            Assert.That(FactoryDoorGeometry.Faces[face], Is.EqualTo(Vector3.forward));
        }

        // --- the ramp ---

        [Test]
        public void RampIsSillHighAtTheDoorAndGroundAtTheBottom()
        {
            Assert.That(FactoryDoorGeometry.RampHeightAt(0f, 0.45f, 2.8f), Is.EqualTo(0.45f).Within(1e-4f));
            Assert.That(FactoryDoorGeometry.RampHeightAt(2.8f, 0.45f, 2.8f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void RampDescendsMonotonically()
        {
            float previous = float.MaxValue;
            for (float d = 0f; d <= 2.8f; d += 0.2f)
            {
                float h = FactoryDoorGeometry.RampHeightAt(d, 0.45f, 2.8f);
                Assert.That(h, Is.LessThanOrEqualTo(previous + 1e-4f), $"rose again at {d} m");
                previous = h;
            }
        }

        [Test]
        public void PastTheBottomOfTheRampTheGroundIsFlat()
        {
            // A robot that has finished emerging must not be left standing on air.
            Assert.That(FactoryDoorGeometry.RampHeightAt(6f, 0.45f, 2.8f), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void RampLiftsOnlyWhatIsStandingOnIt()
        {
            Vector3 doorway = new Vector3(10f, 0f, 5f);
            Vector3 outward = Vector3.left;
            Vector3 across = Vector3.Cross(Vector3.up, outward);

            // On the ramp, one metre out.
            float on = FactoryDoorGeometry.RampLiftAt(
                doorway + outward * 1f, doorway, outward, across, 0.45f, 2.8f, 1.05f);
            Assert.That(on, Is.GreaterThan(0f));

            // Beside it — a robot walking past the shed is not on the ramp.
            float beside = FactoryDoorGeometry.RampLiftAt(
                doorway + outward * 1f + across * 3f, doorway, outward, across, 0.45f, 2.8f, 1.05f);
            Assert.That(beside, Is.EqualTo(0f));

            // Behind the door, inside the building.
            float behind = FactoryDoorGeometry.RampLiftAt(
                doorway - outward * 1f, doorway, outward, across, 0.45f, 2.8f, 1.05f);
            Assert.That(behind, Is.EqualTo(0f));
        }

        [Test]
        public void RampLiftIgnoresHeight_SoAJumpingRobotStillReadsAsOnIt()
        {
            Vector3 doorway = new Vector3(10f, 0f, 5f);
            Vector3 outward = Vector3.left;
            Vector3 across = Vector3.Cross(Vector3.up, outward);

            float flat = FactoryDoorGeometry.RampLiftAt(
                doorway + outward, doorway, outward, across, 0.45f, 2.8f, 1.05f);
            float raised = FactoryDoorGeometry.RampLiftAt(
                doorway + outward + Vector3.up * 2f, doorway, outward, across, 0.45f, 2.8f, 1.05f);

            Assert.That(raised, Is.EqualTo(flat).Within(1e-4f));
        }

        // --- the shutter ---

        [Test]
        public void ShutterStartsShutAndEndsFullyOpen()
        {
            Assert.That(FactoryDoorGeometry.Openness(0f, 0.38f, opening: true), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(FactoryDoorGeometry.Openness(0.38f, 0.38f, opening: true), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ClosingIsTheMirrorOfOpening()
        {
            Assert.That(FactoryDoorGeometry.Openness(0f, 0.38f, opening: false), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(FactoryDoorGeometry.Openness(0.38f, 0.38f, opening: false), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void ShutterNeverOvershootsItsTravel()
        {
            // Update clamps the timer, but the curve must not misbehave if it ever doesn't.
            float over = FactoryDoorGeometry.Openness(5f, 0.38f, opening: true);
            Assert.That(over, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void ShutterIsStillShutEarlyInItsTravel()
        {
            // The gameplay gate opens at 0.75. If the curve were ahead of itself here, robots would
            // step through a door that still looks closed.
            float early = FactoryDoorGeometry.Openness(0.38f * 0.25f, 0.38f, opening: true);
            Assert.That(early, Is.LessThan(0.75f));
        }
    }
}
