using UnityEngine;
using MaxWorlds.Combat;

namespace MaxWorlds.Hose
{
    /// <summary>
    /// Wires the hose weapon into the level with no scene wiring (YT-129) — the self-installing
    /// director idiom used across this project (<c>GroundAnchorVfx</c>, <c>RuntimeSurfaceDirector</c>).
    ///
    /// On the first frame Max exists it: places the starting <see cref="Tap"/> on the patio if the
    /// level has none, then gives Max a <see cref="HoseTether"/> plugged into the nearest tap. It
    /// retries each frame until Max is found, because Max is teleported to his map spawn during scene
    /// build and may not be at his final position on frame zero. Once wired it stops doing work.
    ///
    /// One tap for YT-129. YT-130 will add more taps and the instant-replug switcher; this director is
    /// where that discovery/binding logic will grow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HoseDirector : MonoBehaviour
    {
        /// <summary>The starting tap: on the patio by the back door, a couple of metres off Max's
        /// spawn at (0, -10). Close enough that the opening leash still reaches the lawn.</summary>
        public static readonly Vector3 StartTapPosition = new Vector3(3.5f, 0f, -12f);

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

            EnsureStartTap();
            Tap tap = Nearest(maxGo.transform.position);
            if (tap == null) return;

            var tether = maxGo.GetComponent<HoseTether>();
            if (tether == null) tether = maxGo.AddComponent<HoseTether>();
            tether.SetTap(tap);

            _wired = true;
        }

        private static void EnsureStartTap()
        {
            if (Tap.All.Count > 0) return;
            Tap.Create("Garden Tap (Start)", StartTapPosition);
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
