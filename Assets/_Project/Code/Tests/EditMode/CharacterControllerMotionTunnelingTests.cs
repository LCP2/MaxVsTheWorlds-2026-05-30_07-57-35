using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-386: the third report of "Max/robots walk straight through a gate/fence" -- MV-364 and
    /// MV-378 both declared victory from a STATIONARY gate's collider (Physics.OverlapBox), which
    /// can't catch a movement-tunneling bug and didn't. This ticket's own investigation notes propose
    /// a specific mechanism: a single oversized <c>CharacterController.Move()</c> call -- exactly what
    /// a WebGL stall (tab defocus, GC pause, shader/asset compile) inflates <see cref="Time.deltaTime"/>
    /// into for one frame -- tunneling through a wall as thin as the map format's own default
    /// (<c>MapData.wallThickness</c> = 0.4 m).
    ///
    /// <see cref="RawMove_DoesNotTunnelThroughAnIsolatedWallInAnyOfSeveralExtremeScenarios"/> is the
    /// result of actually testing that mechanism, per this ticket's own AC #1 ("VERIFY this is the
    /// real cause first ... rather than assuming it"): it does NOT reproduce here, in several
    /// variations (see the method's own comment). That doesn't clear the theory -- the live bug has
    /// only ever reproduced on a deployed WebGL build with the real map geometry, and this worker
    /// cannot drive that (CC_AUTONOMY.md forbids PlayMode, and there is no browser here) -- but it
    /// does mean this fix cannot be called a confirmed root-cause fix from this test alone.
    ///
    /// <see cref="SafeMove_NeverEndsUpPastTheSameWallEvenWithAStallSizedDisplacement"/> pins the
    /// hardening actually shipped (<see cref="CharacterControllerMotion.SafeMove"/>, now what
    /// <c>PlayerController</c>/<c>RobotEnemy</c>/<c>BigBermudaBoss</c> call instead of <c>cc.Move</c>
    /// directly): whatever the live mechanism turns out to be, no single physics query it makes can
    /// ever cover more than <see cref="CharacterControllerMotion.MaxSafeStep"/>.
    ///
    /// This is an EditMode test, not the PlayMode test the ticket asks for -- CC_AUTONOMY.md forbids
    /// authoring PlayMode tests (three prior stalls, one a 4h20m CI hang). <see cref="CharacterController.Move"/>
    /// needs no player loop or multi-frame simulation to probe this: it's a handful of deliberate calls
    /// with stall-sized displacements against a bare wall.
    /// </summary>
    public sealed class CharacterControllerMotionTunnelingTests
    {
        // The thinnest wall the map format is authored to allow (MapData.wallThickness's own default).
        private const float WallThickness = 0.4f;

        // A couple of seconds' worth of a robot's committed lunge (11 m/s, RobotEnemy.lungeSpeed) --
        // the size of displacement one WebGL hitch frame can produce with no clamp anywhere upstream.
        private const float StallSizedDisplacement = 6f;

        private static (GameObject character, CharacterController cc, GameObject wall) BuildRig(float wallThickness = WallThickness)
        {
            var character = new GameObject("MV-386 Tunneling Probe Character", typeof(CharacterController));
            var cc = character.GetComponent<CharacterController>();
            cc.center = Vector3.up * 1f;
            cc.height = 2f;
            cc.radius = 0.4f;
            character.transform.position = new Vector3(0f, 1f, -2f);

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "MV-386 Tunneling Probe Wall";
            wall.transform.position = new Vector3(0f, 1.5f, 0f);
            wall.transform.localScale = new Vector3(6f, 3f, wallThickness);

            // autoSyncTransforms is off project-wide (DynamicsManager.asset) -- make the freshly placed
            // wall visible to the very first physics query against it.
            Physics.SyncTransforms();
            return (character, cc, wall);
        }

        [Test]
        public void RawMove_DoesNotTunnelThroughAnIsolatedWallInAnyOfSeveralExtremeScenarios()
        {
            // (a) A single call carrying a stall-sized displacement straight at the wall.
            (GameObject character, CharacterController cc, GameObject wall) = BuildRig();
            try
            {
                cc.Move(Vector3.forward * StallSizedDisplacement);
                Assert.Less(character.transform.position.z, 0f,
                    "(a) a raw oversized single Move() ended up on the far side of the wall");
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(wall); }

            // (b) The same call, but combined with a huge downward component -- PlayerController's
            // actual velocity composition (planar + gravity) after ~2s of falling during the same stall.
            (character, cc, wall) = BuildRig();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = new Vector3(0f, -0.5f, -5f);
            floor.transform.localScale = new Vector3(20f, 1f, 20f);
            Physics.SyncTransforms();
            try
            {
                Vector3 velocity = Vector3.forward * 3.01f + Vector3.up * -42f;
                cc.Move(velocity * 2f);
                Assert.Less(character.transform.position.z, 0f,
                    "(b) a diagonal oversized Move() dominated by a stall-sized fall ended up past the wall");
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(wall); Object.DestroyImmediate(floor); }

            // (c) A much larger single displacement against a wall thinner than the map format allows.
            (character, cc, wall) = BuildRig(wallThickness: 0.05f);
            try
            {
                cc.Move(Vector3.forward * 200f);
                Assert.Less(character.transform.position.z, 0f,
                    "(c) an extreme single Move() against a very thin wall ended up past it");
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(wall); }

            // (d) Ordinary per-frame steps, but 240 of them (4s at 60fps) pressed straight into the
            // wall -- prolonged contact, not a single spike, in case repeated grinding is what matters.
            (character, cc, wall) = BuildRig();
            try
            {
                for (int i = 0; i < 240; i++) cc.Move(Vector3.forward * 3.6f * (1f / 60f));
                Assert.Less(character.transform.position.z, 0f,
                    "(d) 240 frames of ordinary steps ground straight through the wall");
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(wall); }
        }

        [Test]
        public void SafeMove_NeverEndsUpPastTheSameWallEvenWithAStallSizedDisplacement()
        {
            (GameObject character, CharacterController cc, GameObject wall) = BuildRig();
            try
            {
                CharacterControllerMotion.SafeMove(cc, Vector3.forward * StallSizedDisplacement);

                Assert.Less(character.transform.position.z, 0f,
                    "SafeMove let the character end up on the far side of the wall -- the exact MV-386 " +
                    "regression (a stall-inflated single-frame Move() tunneling through a gate/fence) " +
                    "this fix exists to prevent");
            }
            finally
            {
                Object.DestroyImmediate(character);
                Object.DestroyImmediate(wall);
            }
        }
    }
}
