using UnityEngine;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A greybox barrier blocking the path to the next sub-zone (YT-37). Closed by default
    /// (solid collider). <see cref="Open"/> sinks it into the ground and disables its collider
    /// so the player can pass — the physical "gate opens when the factory dies" feedback. The
    /// full patio→lawn→shed→boss-gate path is YT-38; this is the gate primitive that path uses.
    ///
    /// A gate can take MORE THAN ONE KEY (YT-92). The map says which factories open it
    /// (<c>opensOn</c>), the map engine hands each of them to <see cref="AddKey"/>, and the gate
    /// counts: it opens on the death of the LAST factory named, not the first. That is what turns
    /// "break the factory" into "break the factories" — an objective sequence with a build-up rather
    /// than a single beat.
    ///
    /// A gate nobody gave a key to opens on the first <see cref="Unlock"/> it hears, which is what a
    /// hand-built test fixture (and the old one-factory slice) expects of it.
    /// </summary>
    public sealed class SubZoneGate : MonoBehaviour
    {
        [SerializeField] private float sinkDepth = 3f;
        [SerializeField] private float sinkDuration = 0.8f;

        private Collider _collider;
        private bool _opening;
        private float _t;
        private Vector3 _closedPos;
        private Vector3 _openPos;

        public bool IsOpen { get; private set; }

        /// <summary>The way is open — Max can walk through. True from the instant the gate begins to
        /// open, because that is when it stops blocking; the sink that follows is theatre.
        ///
        /// Ask the gate, rather than reading the state of its collider from outside. A caller that
        /// infers "open" from a disabled collider is a caller that breaks the day the gate opens some
        /// other way, and it cannot answer the question at all before the gate has woken up.</summary>
        public bool Unlocked => _opening || IsOpen;

        /// <summary>How many factories this gate is waiting on. 0 means it was never keyed and will
        /// open on the first <see cref="Unlock"/>.</summary>
        public int Keys { get; private set; }

        /// <summary>How many of them are still standing. This is the number the player is working
        /// down, and it is the gate's whole state — exposed so a test can read the objective without
        /// having to destroy anything.</summary>
        public int KeysRemaining { get; private set; }

        /// <summary>Register a factory that has to fall before this gate opens. Called once per
        /// factory named in the map's <c>opensOn</c>.</summary>
        public void AddKey()
        {
            Keys++;
            KeysRemaining++;
        }

        /// <summary>One of this gate's factories is gone. Opens the gate when it was the last one.
        /// Called by <see cref="MowerHutch"/> on its own death, so no factory has to know how many
        /// others there are.</summary>
        public void Unlock()
        {
            KeysRemaining = Mathf.Max(0, KeysRemaining - 1);
            if (KeysRemaining > 0) return;
            Open();
        }

        private void Awake() => _collider = GetComponent<Collider>();

        /// <summary>Begin opening the gate (idempotent). Forces it open regardless of any keys still
        /// standing — <see cref="Unlock"/> is the one that counts.</summary>
        public void Open()
        {
            if (IsOpen || _opening) return;
            _opening = true;
            _t = 0f;
            KeysRemaining = 0;

            // Read where it stands NOW, not in Awake. The map engine places the gate (YT-89) and the
            // order of two Awakes in the same scene load is nobody's to promise — a position cached
            // before the map moved it would make the gate leap back to where it used to be and sink
            // there, in a level it no longer belongs to.
            _closedPos = transform.position;
            _openPos = _closedPos + Vector3.down * sinkDepth;

            if (_collider != null) _collider.enabled = false; // passable immediately
        }

        private void Update()
        {
            if (!_opening) return;
            _t += Time.deltaTime;
            float k = sinkDuration > 0f ? Mathf.Clamp01(_t / sinkDuration) : 1f;
            transform.position = Vector3.Lerp(_closedPos, _openPos, k);
            if (k >= 1f)
            {
                _opening = false;
                IsOpen = true;
                gameObject.SetActive(false); // fully out of the way
            }
        }
    }
}
