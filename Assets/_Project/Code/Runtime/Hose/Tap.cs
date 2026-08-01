using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Hose
{
    /// <summary>
    /// A garden tap (YT-129/130, weapon epic YT-127). It used to be a leash anchor — Max's hose
    /// plugged in and he was held within a hard radius of it — but WV-233 detached the hose from taps
    /// entirely: Max now carries it freely and it's self-supplied by power cells. A tap is no longer a
    /// connection point of any kind — it's an inert landmark <c>HoseDirector</c> places along the run,
    /// dressed as pure backyard set-dressing by <c>TapArtDirector</c> (WV-239). It has no state to
    /// drive and nothing plugs into it.
    ///
    /// Taps self-register into <see cref="All"/> on enable, the same registry idiom the factories use
    /// (<c>FactoryCensus</c>), so <c>TapArtDirector</c> can find every tap in the level without any
    /// scene wiring.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Tap : MonoBehaviour
    {
        private static readonly List<Tap> s_all = new List<Tap>(4);

        /// <summary>Every live tap in the level. Read-only; taps add/remove themselves.</summary>
        public static IReadOnlyList<Tap> All => s_all;

        /// <summary>Height above the tap's origin where the greybox spout sits.</summary>
        public const float NozzleHeight = 0.9f;

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
        /// not shove Max or block him.
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
