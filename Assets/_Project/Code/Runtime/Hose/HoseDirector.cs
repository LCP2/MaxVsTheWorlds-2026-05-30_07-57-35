using UnityEngine;
using MaxWorlds.Combat;

namespace MaxWorlds.Hose
{
    /// <summary>
    /// Wires the hose weapon into the level with no scene wiring (YT-129/130) — the self-installing
    /// director idiom used across this project (<c>GroundAnchorVfx</c>, <c>RuntimeSurfaceDirector</c>).
    ///
    /// On the first frame the armed Max exists it: places the whole tap NETWORK across the garden if
    /// the level has none, then gives Max a <see cref="HoseTether"/> plugged into the nearest tap. It
    /// retries each frame until Max is found, because Max is teleported to his map spawn during scene
    /// build and may not be at his final position on frame zero. Once wired it stops doing work.
    ///
    /// The taps are spaced a bit under a tether-length apart along the path (YT-130), so from each one
    /// Max can just reach the next — hopping tap to tap is the early-game traversal, until the Hydro
    /// device (YT-133) cuts the hose entirely. The instant re-plug itself lives in
    /// <see cref="HoseTether"/>; this only decides where the taps stand.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoseDirector : MonoBehaviour
    {
        /// <summary>The tap network, roughly along the patio → lawn → orchard → boss path. Each is
        /// under <see cref="HoseTether.AuthoredLength"/> from its neighbours so the leash reaches from
        /// one to the next; small x-offsets keep them clear of centre-line cover and the boss gate.</summary>
        public static readonly Vector3[] TapPositions =
        {
            new Vector3(3.5f, 0f, -13.5f),  // patio, right at Max's shed door (the start; YT-163)
            new Vector3(0f,   0f,   4f),  // the lawn
            new Vector3(-2f,  0f,  20f),  // far lawn
            new Vector3(2f,   0f,  36f),  // the orchard
            new Vector3(4f,   0f,  52f),  // just past the boss gate
            new Vector3(-4f,  0f,  62f),  // the boss clearing
        };

        /// <summary>The starting tap — the first of the network, on the patio. Kept as a named point
        /// for tests and for where Max plugs in at spawn.</summary>
        public static Vector3 StartTapPosition => TapPositions[0];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<HoseDirector>() != null) return;
            new GameObject("HoseDirector").AddComponent<HoseDirector>();
        }

        private bool _wired;

        private void Update()
        {
            if (_wired) return;

            var maxGo = GameObject.FindGameObjectWithTag("Player");
            if (maxGo == null) return; // Max not placed yet — try again next frame

            // The hose is Max's weapon re-themed, so it only belongs to the ARMED player — the one
            // carrying a WaterBlaster. This gate keeps the tether and its per-frame hose renderer out
            // of the enemy-navigation / factory / boss PlayMode tests, whose Max is a bare greybox
            // capsule with no weapon: attaching a leash and drawing a hose in those scenes was pure
            // overhead that tipped a frame-pacing-sensitive nav test over its stall threshold.
            if (maxGo.GetComponent<WaterBlaster>() == null) return;

            EnsureTaps();
            Tap tap = Nearest(maxGo.transform.position);
            if (tap == null) return;

            var tether = maxGo.GetComponent<HoseTether>();
            if (tether == null) tether = maxGo.AddComponent<HoseTether>();
            tether.SetTap(tap);

            _wired = true;
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

        private static Tap Nearest(Vector3 p)
        {
            Tap best = null;
            float bestSq = float.MaxValue;
            foreach (Tap t in Tap.All)
            {
                if (t == null) continue;
                float sq = (t.transform.position - p).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = t; }
            }
            return best;
        }
    }
}
