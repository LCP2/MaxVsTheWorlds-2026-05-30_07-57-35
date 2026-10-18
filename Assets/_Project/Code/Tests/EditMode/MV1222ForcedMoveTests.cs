using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1222: one shared push/pull rule for every feature that moves Max without his input (the
    /// Breaker's knockback and the Tractor's pull both land in later tickets). Before this ticket,
    /// <see cref="PlayerController"/> had no way to be displaced by anything but its own input.
    ///
    /// Drives <see cref="PlayerController.ApplyForcedMove"/> and its private per-tick helper directly
    /// (same reflection idiom as <c>MV752RampClimbTests</c>) rather than through a live Input System
    /// frame -- this ticket's own AC is about the shove's physical resolution against a wall, which
    /// needs no input at all.
    /// </summary>
    public sealed class MV1222ForcedMoveTests
    {
        // Awake isn't reliably invoked for AddComponent/GameObject-constructor components outside
        // Play mode -- same empirical finding MV503StuckDiagnosticTests/MV752RampClimbTests already
        // work around. Without this, PlayerController's private _cc field is never set.
        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        private const float WallThickness = 0.4f;   // MapData.wallThickness's own default
        private const float GapToWall = 1f;          // "Max 1 m from a wall" (this ticket's own AC wording)

        // Measured empirically (this ticket's own run) that building the rig near world origin shares
        // the EditMode physics scene with whatever real map geometry an earlier, unrelated test in the
        // same batch run left behind there -- the open-floor half of this test measured exactly 0 m of
        // travel at (0,0,0) in the full cc-verify run despite passing in isolation. 500 m up is well
        // outside every authored level's Y range (decks top out a few metres), so nothing shipped ever
        // shares this airspace.
        private static readonly Vector3 RigOrigin = new Vector3(0f, 500f, 0f);

        /// <summary>Runs <see cref="PlayerController.ApplyForcedMove"/>'s private per-tick helper in
        /// fixed 0.02 s slices until either the forced move clears itself (duration elapsed, or 3
        /// consecutive blocked ticks -- both are this ticket's own AC) or a generous iteration cap is
        /// hit, so this can never hang if that early-exit were ever broken.</summary>
        private static void RunForcedMoveToCompletion(PlayerController player)
        {
            MethodInfo tick = typeof(PlayerController).GetMethod("TickForcedMove",
                BindingFlags.NonPublic | BindingFlags.Instance);

            for (int i = 0; i < 100 && player.IsForcedMoveActive; i++)
            {
                tick.Invoke(player, new object[] { 0.02f });
            }
        }

        [Test]
        public void MV_ForcedMoveStopsAtAWallThenCoversTheFullDistanceInOpenFloor()
        {
            var character = new GameObject("MV-1222 Forced Move Probe",
                typeof(CharacterController), typeof(PlayerController));
            GameObject wall = null;
            try
            {
                var cc = character.GetComponent<CharacterController>();
                var player = character.GetComponent<PlayerController>();
                character.transform.position = RigOrigin;
                InvokeAwake(player);

                // The wall's near face sits GapToWall (1 m) from Max's capsule centre, matching the
                // AC's own "1 m from a wall" wording exactly.
                wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = RigOrigin + new Vector3(0f, 0f, GapToWall + WallThickness / 2f);
                wall.transform.localScale = new Vector3(6f, 3f, WallThickness);
                // autoSyncTransforms is off project-wide (DynamicsManager.asset) -- make the freshly
                // placed wall visible to the very first physics query against it.
                Physics.SyncTransforms();

                // AC: 10 m/s into the wall for 0.4 s (would cover 4 m unobstructed -- far past the wall).
                player.ApplyForcedMove(new Vector3(0f, 0f, 10f), 0.4f, ForcedMoveSource.Knockback);
                RunForcedMoveToCompletion(player);

                float wallFace = RigOrigin.z + GapToWall;
                float maxAllowedZ = wallFace - cc.radius;
                Assert.LessOrEqual(character.transform.position.z, maxAllowedZ + 0.01f,
                    $"a forced move into a wall must never pass the wall's face minus Max's radius -- " +
                    $"resolved z={character.transform.position.z:F3}, wall face={wallFace:F3}, " +
                    $"radius={cc.radius:F3} (limit {maxAllowedZ:F3})");

                // AC: a second call away from the wall moves him 4 m +-0.2 in open floor.
                Vector3 beforeSecond = character.transform.position;
                player.ApplyForcedMove(new Vector3(0f, 0f, -10f), 0.4f, ForcedMoveSource.Pull);
                RunForcedMoveToCompletion(player);

                float traveled = beforeSecond.z - character.transform.position.z;
                Assert.AreEqual(4f, traveled, 0.2f,
                    $"a forced move with nothing in its way must cover its full velocity*duration " +
                    $"distance -- travelled {traveled:F3} m, expected 4 m +-0.2");
            }
            finally
            {
                Object.DestroyImmediate(character);
                if (wall != null) Object.DestroyImmediate(wall);
            }
        }
    }
}
