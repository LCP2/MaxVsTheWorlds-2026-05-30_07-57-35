using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

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

        // Bright cyan when Max's hose is plugged in here, dim grey otherwise — so which tap he's on
        // reads at a glance (YT-130), alongside the hose line that runs to it.
        private static readonly Color ConnectedColor = new Color(0.31f, 0.86f, 0.98f);
        private static readonly Color IdleColor = new Color(0.30f, 0.32f, 0.34f);

        private Renderer _indicator;
        private MaterialPropertyBlock _mpb;

        /// <summary>Is Max's hose currently plugged into this tap? Driven by the tether (YT-130).</summary>
        public bool IsConnected { get; private set; }

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
        }

        private void OnDisable()
        {
            s_all.Remove(this);
        }

        /// <summary>Light this tap up as the connected one (or dim it back). The tether calls this on
        /// every re-plug so exactly one tap glows.</summary>
        public void SetConnected(bool connected)
        {
            IsConnected = connected;
            if (_indicator == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _indicator.GetPropertyBlock(_mpb);
            Color c = connected ? ConnectedColor : IdleColor;
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", connected ? c : Color.black);
            _indicator.SetPropertyBlock(_mpb);
        }

        /// <summary>
        /// Nearest tap to <paramref name="pos"/> within <paramref name="range"/> metres (planar), by
        /// index into <paramref name="taps"/>; -1 if none is in range. Pure, so the "walk up to a tap
        /// and it swaps" rule (YT-130) can be tested without a scene.
        /// </summary>
        public static int NearestWithin(Vector3 pos, IReadOnlyList<Vector3> taps, float range)
        {
            int best = -1;
            float bestSq = range * range;
            for (int i = 0; i < taps.Count; i++)
            {
                float dx = taps[i].x - pos.x;
                float dz = taps[i].z - pos.z;
                float sq = dx * dx + dz * dz;
                if (sq <= bestSq) { bestSq = sq; best = i; }
            }
            return best;
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

            // The connection indicator: a small bulb on top that glows cyan when Max is plugged in
            // here (YT-130). Marked KeepsOwnMaterial + given a real URP material so the surface sweep
            // leaves it alone and it's tinted per-state through a property block rather than repainted
            // as scenery. Its collider is stripped like the rest of the tap.
            var bulb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            bulb.name = "TapIndicator";
            Destroy(bulb.GetComponent<Collider>());
            bulb.AddComponent<KeepsOwnMaterial>();
            bulb.transform.SetParent(transform, worldPositionStays: false);
            bulb.transform.localScale = Vector3.one * 0.16f;
            bulb.transform.localPosition = new Vector3(0f, NozzleHeight + 0.12f, 0f);
            _indicator = bulb.GetComponent<MeshRenderer>();
            _indicator.sharedMaterial = MaterialLibrary.Surface(SurfaceKind.Metal);
            _indicator.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            SetConnected(IsConnected);   // paint the initial state (idle unless the tether plugged in first)
        }
    }
}
