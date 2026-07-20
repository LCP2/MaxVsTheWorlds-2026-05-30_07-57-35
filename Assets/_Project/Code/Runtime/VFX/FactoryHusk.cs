using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The wreck of the factory, standing for as long as it takes to fall (YT-109).
    ///
    /// The biggest single reason the destruction did not land was not the particles — it was that the
    /// building disappeared on the frame it died. <see cref="MowerHutch"/> switches its renderer off
    /// the instant its health hits zero, and it has to: the GameObject stays alive so the robots
    /// parented to it keep fighting, so hiding the body is the only way for it to stop being there.
    /// A three-metre building blinking out of existence is not something an explosion can rescue.
    ///
    /// So the moment it dies, art stands a HUSK in its place — same size, same colour, same spot —
    /// and takes that down properly: it shudders while the failure builds, then gives way and sinks
    /// as the blast lands, then it is gone. Nothing about the real factory changes; gameplay still
    /// hides its body on frame one, and this is what the player actually watches.
    ///
    /// One per factory (YT-92), bound to its own hutch. Nothing here has a collider — the fight
    /// carries on over the wreck, and a husk you could walk into would trap robots against it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FactoryHusk : MonoBehaviour
    {
        /// <summary>Built exactly like <see cref="FactoryLife"/>'s: inactive, bound, then switched
        /// on, because Awake measures the body it is about to stand in for.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            foreach (var hutch in FindObjectsByType<MowerHutch>(FindObjectsSortMode.None))
            {
                if (hutch == null || Watches(hutch)) continue;

                var go = new GameObject($"FactoryHusk ({hutch.name})");
                go.SetActive(false);
                go.AddComponent<FactoryHusk>().Bind(hutch);
                go.SetActive(true);
            }
        }

        private static bool Watches(MowerHutch hutch)
        {
            foreach (var h in FindObjectsByType<FactoryHusk>(FindObjectsSortMode.None))
                if (h._hutch == hutch) return true;

            return false;
        }

        public void Bind(MowerHutch hutch) => _hutch = hutch;

        private MowerHutch _hutch;
        private Transform _husk;
        private Vector3 _standing;      // where the body was, while it was still there
        private Vector3 _size;
        private Vector3 _across;
        private float _elapsed = -1f;   // < 0 until it dies
        private bool _spent;

        private void Awake()
        {
            if (_hutch == null) _hutch = FindFirstObjectByType<MowerHutch>();
            if (_hutch == null) return;

            gameObject.AddComponent<KeepsOwnMaterial>();

            // Measured NOW, while the body is still visible. After death its renderer is off and its
            // bounds are a zero-sized box at the origin — the husk would be a speck at the map origin.
            var body = _hutch.GetComponent<Renderer>();
            if (body == null) return;

            _standing = body.bounds.center;
            _size = body.bounds.size;
            _across = Vector3.right;
        }

        private void Update()
        {
            if (_hutch == null || _spent) return;

            if (_elapsed < 0f)
            {
                if (_hutch.IsAlive) return;   // still standing; nothing to do
                Raise();
            }

            _elapsed += Time.deltaTime;
            Drive();
        }

        /// <summary>Stand the wreck up in the dead factory's place, on the frame it dies.</summary>
        private void Raise()
        {
            _elapsed = 0f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Husk";

            // Scenery. The husk exists to be looked at for a second — a collider on it would block
            // shots and pin robots against a building that is no longer there.
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = _standing;
            go.transform.localScale = _size;

            // The factory's own steel, not its hazard orange: this is wreckage now. Taking a material
            // from the library rather than the primitive's default is what keeps it off the magenta
            // path that has caught two other objects in this codebase.
            var r = go.GetComponent<MeshRenderer>();
            var mat = MaterialLibrary.Surface(SurfaceKind.Metal);
            if (mat != null) r.sharedMaterial = mat;

            _husk = go.transform;
        }

        /// <summary>Shudder, then sink. Driven off elapsed time rather than a coroutine so the whole
        /// pose is a function of one number and can be reasoned about at any instant.</summary>
        private void Drive()
        {
            if (_husk == null) return;

            float fall = FactoryDeathTiming.CollapseProgress(_elapsed);
            if (fall >= 1f)
            {
                // Gone, and it takes its object with it: this runs once per factory per run, and a
                // husk left parked off-screen is a renderer the frame still pays for.
                Destroy(_husk.gameObject);
                _husk = null;
                _spent = true;
                return;
            }

            // Sinks INTO the ground rather than shrinking. A building that scales down reads as a
            // menu animation; one that goes through the floor reads as collapsing.
            float sink = _size.y * fall;
            Vector3 p = _standing - Vector3.up * sink;
            p += _across * FactoryDeathTiming.ShudderOffset(_elapsed, 0.06f);
            _husk.position = p;

            // A slight lean as it goes, so it does not descend like a lift.
            _husk.rotation = Quaternion.Euler(fall * 7f, 0f, fall * 4f);
        }
    }
}
