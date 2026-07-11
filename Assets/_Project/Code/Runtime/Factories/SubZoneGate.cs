using UnityEngine;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A greybox barrier blocking the path to the next sub-zone (YT-37). Closed by default
    /// (solid collider). <see cref="Open"/> sinks it into the ground and disables its collider
    /// so the player can pass — the physical "gate opens when the factory dies" feedback. The
    /// full patio→lawn→shed→boss-gate path is YT-38; this is the gate primitive that path uses.
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

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _closedPos = transform.position;
            _openPos = _closedPos + Vector3.down * sinkDepth;
        }

        /// <summary>Begin opening the gate (idempotent).</summary>
        public void Open()
        {
            if (IsOpen || _opening) return;
            _opening = true;
            _t = 0f;
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
