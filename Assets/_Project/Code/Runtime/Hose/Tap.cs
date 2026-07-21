using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Hose
{
    /// <summary>
    /// A garden tap — the fixed point Max's hose plugs into (YT-129, weapon epic YT-127).
    ///
    /// A tap is a leash anchor: <see cref="HoseTether"/> keeps Max within a hard radius of the tap
    /// he's plugged into, so ranging across the garden is a decision about where the water reaches,
    /// not free movement. For YT-129 there is exactly one tap, on the patio by the back door. YT-130
    /// scatters more of them and lets Max re-plug instantly, which is what turns tap-hopping into
    /// early traversal.
    ///
    /// Taps self-register into <see cref="All"/> on enable, the same registry idiom the factories use
    /// (<c>FactoryCensus</c>), so the tether and the tap-switcher can find every tap in the level
    /// without any scene wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Tap : MonoBehaviour
    {
        private static readonly List<Tap> s_all = new List<Tap>(4);

        /// <summary>Every live tap in the level. Read-only; taps add/remove themselves.</summary>
        public static IReadOnlyList<Tap> All => s_all;

        /// <summary>Height above the tap's origin where the hose couples on — the top of the spout.</summary>
        public const float NozzleHeight = 0.9f;

        /// <summary>World point the hose visually connects to (top of the spout).</summary>
        public Vector3 NozzlePosition => transform.position + Vector3.up * NozzleHeight;

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        private void OnDisable()
        {
            s_all.Remove(this);
        }

        /// <summary>Build and place a greybox tap standing on the lawn at <paramref name="groundPosition"/>.</summary>
        public static Tap Create(string name, Vector3 groundPosition)
        {
            var go = new GameObject(name);
            go.transform.position = groundPosition;
            var tap = go.AddComponent<Tap>();
            tap.BuildVisual();
            return tap;
        }

        /// <summary>
        /// A short pipe out of the ground with a spout — greybox stand-in, no art dependency. The
        /// primitives are left UNMARKED so <c>RuntimeSurfaceDirector</c> repaints them with a real URP
        /// material (a runtime <c>CreatePrimitive</c> keeps Unity's built-in material, which draws
        /// magenta in a player build). Their colliders are stripped: a tap is a landmark, and it must
        /// not shove Max or block the leash it anchors.
        /// </summary>
        private void BuildVisual()
        {
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = "TapPost";
            Destroy(post.GetComponent<Collider>());
            post.transform.SetParent(transform, worldPositionStays: false);
            post.transform.localScale = new Vector3(0.16f, NozzleHeight * 0.5f, 0.16f);
            post.transform.localPosition = new Vector3(0f, NozzleHeight * 0.5f, 0f);

            var spout = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            spout.name = "TapSpout";
            Destroy(spout.GetComponent<Collider>());
            spout.transform.SetParent(transform, worldPositionStays: false);
            spout.transform.localScale = new Vector3(0.1f, 0.18f, 0.1f);
            spout.transform.localPosition = new Vector3(0f, NozzleHeight, 0.18f);
            spout.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }
}
