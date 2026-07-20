using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// Decides when a <see cref="Discoverable"/> has come into the player's view (YT-107), and sweeps
    /// the level for ones that have.
    ///
    /// "In view" is deliberately TWO questions, because either one alone gets it wrong:
    ///
    /// <list type="bullet">
    /// <item>On screen — a real frustum test against the real camera, not a reveal radius. The camera
    /// zoom is a tunable knob (YT-82) and the frustum is narrower at phone aspect than on a monitor,
    /// so a hard-coded radius would be a number that quietly disagrees with what the player can
    /// actually see the moment anyone touches the zoom. Asking the camera can't drift.</item>
    /// <item>And in Max's line of sight — measured from Max, not from the camera. The camera looks
    /// down at ~72° and sees clean over the fences; Max does not. Reveal off the camera alone and
    /// walking up to a wall hands you everything behind it.</item>
    /// </list>
    ///
    /// Both, and only both, count as "the player has seen this".
    /// </summary>
    public static class Discovery
    {
        /// <summary>
        /// The volume to test against the frustum.
        ///
        /// Collider before renderer, because the boss's placeholder <c>MeshRenderer</c> is switched
        /// OFF by <c>BigBermudaRig</c> once it swaps in the real mech — and a disabled renderer's
        /// bounds are not maintained, so trusting it would test a stale box (or an empty one at the
        /// origin, which is never on screen: the boss would never be discovered at all). Its
        /// <c>CharacterController</c> is a collider and is always live.
        /// </summary>
        public static Bounds VolumeOf(Component landmark)
        {
            var col = landmark.GetComponent<Collider>();
            if (col != null) return col.bounds;

            var rend = landmark.GetComponent<Renderer>();
            if (rend != null && rend.enabled) return rend.bounds;

            return new Bounds(landmark.transform.position, Vector3.one);
        }

        /// <summary>True if <paramref name="landmark"/> is on screen AND Max can see it.</summary>
        public static bool InView(Camera eyeOfTheGame, Transform max, Component landmark)
        {
            if (eyeOfTheGame == null || max == null || landmark == null) return false;

            var planes = GeometryUtility.CalculateFrustumPlanes(eyeOfTheGame);
            if (!GeometryUtility.TestPlanesAABB(planes, VolumeOf(landmark))) return false;

            // The landmark is passed as the target so it does not count as its own cover. The Mower
            // Hutch is ON the cover layer (YT-83) — without this it would block the sight-line to
            // itself and could never be discovered.
            return LineOfSight.Clear(LineOfSight.EyeOf(max), landmark.transform.position,
                                     landmark.transform);
        }
    }

    /// <summary>
    /// Walks the level once a frame and reveals whatever has come into view. Self-installing, like
    /// every other director here, so a level built from map data gets its fog without a scene
    /// remembering to carry a component.
    ///
    /// Costs a frustum build and one raycast per UNDISCOVERED landmark — three of them in the slice,
    /// and zero once the yard has been explored, because the sweep retires itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DiscoveryDirector : MonoBehaviour
    {
        private Discoverable[] _landmarks = new Discoverable[0];
        private Transform _max;
        private Camera _camera;
        private int _remaining;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // AfterSceneLoad is late enough: the map builds its landmarks inside BackyardPath.Awake.
            if (FindFirstObjectByType<DiscoveryDirector>() != null) return;
            if (FindFirstObjectByType<Discoverable>() == null) return;

            new GameObject("DiscoveryDirector").AddComponent<DiscoveryDirector>();
        }

        private void Start() => Rescan();

        /// <summary>Re-read the level's landmarks. Public so a test can build a level and then say so
        /// without waiting on the installer.</summary>
        public void Rescan()
        {
            _landmarks = FindObjectsByType<Discoverable>(FindObjectsSortMode.None);
            _remaining = 0;
            foreach (Discoverable mark in _landmarks)
                if (mark != null && !mark.Found) _remaining++;
        }

        private void LateUpdate() => Sweep();

        /// <summary>One pass. Exposed so a PlayMode test can step it deterministically rather than
        /// yielding frames and hoping.</summary>
        public void Sweep()
        {
            if (_remaining <= 0) return;   // the whole yard is known; stop paying for it

            if (_max == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                _max = player != null ? player.transform : null;
                if (_max == null) return;
            }

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null) return;
            }

            foreach (Discoverable mark in _landmarks)
            {
                if (mark == null || mark.Found) continue;
                if (!Discovery.InView(_camera, _max, mark)) continue;

                mark.Reveal();
                _remaining--;
            }
        }
    }
}
