using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Pure placement maths for <see cref="WaterVfx"/>'s ground trail (MV-555): where the trail's
    /// quad sits and how it's yawed, given the owner's current position and facing. Identical to
    /// the flatten-and-look-rotation <see cref="AimReticle"/> already does in its own LateUpdate —
    /// kept here as a named, standalone function (rather than inlined) so a test can build the
    /// exact transform the trail will use without needing a frame to tick, the same reason
    /// <see cref="AimReticleMesh"/> and <see cref="WaterVfxTuning"/> are pulled out of their
    /// MonoBehaviours.
    /// </summary>
    public static class WaterGroundTrailTuning
    {
        /// <summary>Resolve the trail's world position (flattened onto the lawn at
        /// <paramref name="groundLift"/>) and yaw (facing <paramref name="ownerForward"/>, planar).
        /// A degenerate forward (e.g. straight up) falls back to identity rotation rather than
        /// producing a NaN transform.</summary>
        public static void Placement(Vector3 ownerPosition, Vector3 ownerForward, float groundLift,
            out Vector3 position, out Quaternion rotation)
        {
            position = new Vector3(ownerPosition.x, groundLift, ownerPosition.z);

            Vector3 flat = new Vector3(ownerForward.x, 0f, ownerForward.z);
            rotation = flat.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(flat.normalized, Vector3.up)
                : Quaternion.identity;
        }
    }
}
