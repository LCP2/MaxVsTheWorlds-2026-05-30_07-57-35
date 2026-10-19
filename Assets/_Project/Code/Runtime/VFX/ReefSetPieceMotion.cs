using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1258: the moving parts of the Reef's hero set pieces — a reactor core's counter-rotating
    /// rings, a propeller shaft's coupling collar, a cargo-hold crane's swaying hook and chain. WHERE
    /// each one gets attached is <c>MaxWorlds.Arena.ReefSetPieces</c>' own concern (Arena-assembly
    /// types this VFX-assembly file has no reference to); this is only WHAT each one does once placed,
    /// same split <see cref="ReefAmbientLife"/> above already keeps for the Reef's ambient-life pass.
    /// Visual only, same "Update only ever touches a transform, never EditMode-tested" contract as
    /// <see cref="ReefDrumCorePulse"/>/<see cref="ReefLightFlicker"/> (testing policy Tier 3).
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("reef_ambient")]
    public sealed class ReefSetPieceSpin : MonoBehaviour
    {
        private float _degreesPerSecond;

        public static ReefSetPieceSpin Attach(Transform target, float degreesPerSecond)
        {
            if (target == null) return null;
            var spin = target.gameObject.AddComponent<ReefSetPieceSpin>();
            spin._degreesPerSecond = degreesPerSecond;
            return spin;
        }

        private void Update() => transform.Rotate(Vector3.up, _degreesPerSecond * Time.deltaTime, Space.Self);
    }

    /// <summary>A slow pendulum sway around local Z for a hanging piece — the cargo-hold crane's own
    /// hook and chain. Same visual-only contract as <see cref="ReefSetPieceSpin"/>.</summary>
    [DisallowMultipleComponent]
    [PerfSection("reef_ambient")]
    public sealed class ReefSetPieceSway : MonoBehaviour
    {
        private const float Period = 4f;

        private float _amplitudeDegrees;
        private Quaternion _restRotation;
        private float _phase;

        public static ReefSetPieceSway Attach(Transform target, float amplitudeDegrees, int seed)
        {
            if (target == null) return null;
            var sway = target.gameObject.AddComponent<ReefSetPieceSway>();
            sway._amplitudeDegrees = amplitudeDegrees;
            sway._restRotation = target.localRotation;
            sway._phase = ReefAmbientHash.Hash01(seed, 0) * Period;
            return sway;
        }

        private void Update()
        {
            float u = Mathf.Repeat((Time.time + _phase) / Period, 1f);
            float swing = Mathf.Sin(u * Mathf.PI * 2f) * _amplitudeDegrees;
            transform.localRotation = _restRotation * Quaternion.Euler(0f, 0f, swing);
        }
    }
}
