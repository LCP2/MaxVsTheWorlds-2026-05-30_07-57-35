using UnityEngine;

namespace MaxWorlds.Hose
{
    /// <summary>
    /// Places the garden tap network with no scene wiring (YT-129/130). The taps are no longer a
    /// functional leash anchor (WV-233 reverses YT-129/130: Max carries the hose freely and it's
    /// self-supplied by power cells, not gated by a tap) — they now stand as inert set-dressing
    /// landmarks, which <c>TapArtDirector</c> may dress or an art pass may remove (WV-239).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoseDirector : MonoBehaviour
    {
        /// <summary>The tap network, roughly along the patio → lawn → orchard → boss path. Kept as
        /// set-dressing landmarks along the run even though nothing plugs into them any more.</summary>
        public static readonly Vector3[] TapPositions =
        {
            new Vector3(3.5f, 0f, -13.5f),  // patio, right at Max's shed door (the start; YT-163)
            new Vector3(0f,   0f,   4f),  // the lawn
            new Vector3(-2f,  0f,  20f),  // far lawn
            new Vector3(2f,   0f,  36f),  // the orchard
            new Vector3(4f,   0f,  52f),  // just past the boss gate
            new Vector3(-4f,  0f,  62f),  // the boss clearing
        };

        /// <summary>The first tap in the network — kept as a named point for tests.</summary>
        public static Vector3 StartTapPosition => TapPositions[0];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<HoseDirector>() != null) return;
            new GameObject("HoseDirector").AddComponent<HoseDirector>();
        }

        private void Awake()
        {
            EnsureTaps();
        }

        private static void EnsureTaps()
        {
            if (Tap.All.Count > 0) return;   // already placed (idempotent under the installer re-run)
            for (int i = 0; i < TapPositions.Length; i++)
            {
                string name = i == 0 ? "Garden Tap (Start)" : $"Garden Tap {i}";
                Tap.Create(name, TapPositions[i]);
            }
        }
    }
}
