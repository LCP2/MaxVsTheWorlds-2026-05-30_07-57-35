using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-946 (Lee, TestFlight 0.9.9): "Max can teleport out of the playable area... if he keeps
    /// walking away he drops into infinity with no way back." Nothing recovered Max (or a Sentinel)
    /// once solid ground stopped existing beneath them — this is wholly new behaviour
    /// (<see cref="FallRecoveryState"/> did not exist before this ticket), so the pre-fix failure is a
    /// build error: the type is undefined. Testing policy MV-465 Tier 2: asserts the RESOLVED recovery
    /// position <see cref="FallRecoveryState.Tick"/> hands back, never an authored constant, and also
    /// asserts the grace window is actually honoured (no recovery before 0.5s out of play).
    /// </summary>
    public sealed class MV946FallRecoveryTests
    {
        private static readonly MapData Map = new MapData
        {
            zones = new[] { new MapZone { id = "floor", x = 0f, z = 0f, width = 20f, depth = 20f, level = 0 } },
        };

        [Test]
        public void FallingBelowTheFloorForOverHalfASecondRecoversToTheLastGroundedPosition()
        {
            Vector3 lastGrounded = new Vector3(3f, 0f, -2f);
            var state = new FallRecoveryState(initialSafePosition: lastGrounded);

            // One grounded, in-bounds tick banks the safe position.
            Assert.IsNull(state.Tick(lastGrounded, Map, grounded: true, dt: 0.1f));

            // Then Max drops through a floor void: ungrounded, well past BelowFloorMargin (2m) below
            // the floor plane.
            Vector3 falling = new Vector3(3f, -5f, -2f);
            Vector3? recovered = null;
            for (int i = 0; i < 4; i++) // 4 * 0.1s = 0.4s -- under the 0.5s grace window
                recovered = state.Tick(falling, Map, grounded: false, dt: 0.1f);
            Assert.IsNull(recovered, "must not recover before the 0.5s grace window elapses");

            recovered = state.Tick(falling, Map, grounded: false, dt: 0.2f); // 0.6s total -- past grace
            Assert.That(recovered, Is.EqualTo(lastGrounded));
        }
    }
}
