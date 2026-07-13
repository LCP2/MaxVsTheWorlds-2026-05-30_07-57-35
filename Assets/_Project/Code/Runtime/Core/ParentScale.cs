using UnityEngine;

namespace MaxWorlds.Core
{
    /// <summary>
    /// Cancelling an inherited transform scale.
    ///
    /// This project has now been bitten by the same trap twice, in two unrelated systems, so the
    /// concept lives in one place:
    ///
    /// * YT-71 — the factory's health bar was parented to the Mower Hutch's body, a cube scaled
    ///   (3, 2, 3). Its authored 0.02 canvas scale became (0.06, 0.04, 0.06), and a 220 px bar
    ///   rendered 13.2 METRES wide.
    /// * YT-74 — the robots were parented to that same body. Their authored body scale was
    ///   multiplied by (3, 2, 3) too, so a 1.9 m bruiser spawned as a 5.7 m cube with a collider to
    ///   match, and walled the player in.
    ///
    /// Both times every authored number was correct and the bug was invisible in the file that
    /// contained it. The lesson is the general one: <b>anything parented to a scaled body inherits
    /// that scale</b> — meshes, colliders, canvases, and local positions alike. If a thing's size is
    /// supposed to mean metres, its parent's scale has to be cancelled, not guessed around.
    /// </summary>
    public static class ParentScale
    {
        /// <summary>Local scale that cancels <paramref name="parentLossyScale"/>, so a child's units
        /// are world metres again whatever it hangs on.</summary>
        public static Vector3 Unscale(Vector3 parentLossyScale)
        {
            return new Vector3(
                1f / SafeAxis(parentLossyScale.x),
                1f / SafeAxis(parentLossyScale.y),
                1f / SafeAxis(parentLossyScale.z));
        }

        /// <summary>The local offset that lands <paramref name="worldMetres"/> along an axis whose
        /// parent is scaled by <paramref name="parentScale"/>. Local POSITIONS are scaled by the
        /// parent as well — a body scaled 2× on Y moves its children twice as far as the number says.</summary>
        public static float LocalOffset(float worldMetres, float parentScale)
            => worldMetres / SafeAxis(parentScale);

        /// <summary>
        /// Give <paramref name="child"/> a local scale that makes it a metre-space container: its own
        /// children can then be authored in plain world units. Returns the child, for chaining.
        /// </summary>
        public static Transform MakeMetreSpace(Transform child, Transform scaledParent)
        {
            child.SetParent(scaledParent, false);
            child.localPosition = Vector3.zero;
            child.localRotation = Quaternion.identity;
            child.localScale = Unscale(scaledParent.lossyScale);
            return child;
        }

        private static float SafeAxis(float v) => Mathf.Abs(v) < 1e-4f ? 1e-4f : v;
    }
}
