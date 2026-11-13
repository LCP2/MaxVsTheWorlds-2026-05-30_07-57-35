using UnityEngine;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// MV-1355: a value that eases (ease-out cubic) from where it currently is to a new target over a
    /// fixed duration whenever the target changes, instead of snapping. The first <see cref="Step"/>
    /// adopts the target outright, so a freshly built component starts at its real value rather than
    /// easing in from zero. Used for the Orbitals orb diameter and orbit radius on a POWER/REACH upgrade.
    /// </summary>
    public struct EasedValue
    {
        private float _from;
        private float _to;
        private float _t;
        private bool _started;

        /// <summary>The value right now (the target itself until the first <see cref="Step"/>).</summary>
        public float Value { get; private set; }

        public bool IsStarted => _started;

        /// <summary>Ease-out cubic: fast start, gentle landing. 0 -> 0, 1 -> 1.</summary>
        public static float EaseOut(float t)
        {
            float u = 1f - Mathf.Clamp01(t);
            return 1f - u * u * u;
        }

        public void Step(float target, float dt, float durationSeconds)
        {
            if (!_started)
            {
                _started = true;
                _from = _to = Value = target;
                _t = 1f;
                return;
            }

            if (!Mathf.Approximately(target, _to))
            {
                _from = Value;
                _to = target;
                _t = 0f;
            }

            _t = durationSeconds <= 0f ? 1f : Mathf.Min(1f, _t + dt / durationSeconds);
            Value = Mathf.Lerp(_from, _to, EaseOut(_t));
        }
    }
}
