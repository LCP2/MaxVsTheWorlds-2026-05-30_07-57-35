#if UNITY_IOS && !UNITY_EDITOR
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// MV-958: ticks the one live <see cref="ThermalQualityGovernor"/> from the iOS player only —
    /// compiled out of every other target entirely (Editor, Windows standalone, WebGL), so those builds
    /// carry no governor at all rather than a governor that happens to always read "n/a" and no-op.
    /// Registers itself as <see cref="ThermalQualityGovernor.Active"/> so the MV-910 overlay (a different
    /// assembly, which this one cannot see) can read the resolved tier without either side needing to
    /// know about the other beyond that one static hook.
    /// </summary>
    [MaxWorlds.Core.PerfSection("rendering")]
    internal sealed class ThermalQualityGovernorRunner : MonoBehaviour
    {
        private static ThermalQualityGovernorRunner _instance;
        private readonly ThermalQualityGovernor _governor = new ThermalQualityGovernor();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var go = new GameObject(nameof(ThermalQualityGovernorRunner));
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<ThermalQualityGovernorRunner>();
        }

        private void Awake() => ThermalQualityGovernor.Active = _governor;

        private void Update() => _governor.Tick(Time.unscaledTime, IosDeviceStateProbe.ThermalStateName);

        private void OnDestroy()
        {
            if (ThermalQualityGovernor.Active == _governor) ThermalQualityGovernor.Active = null;
            if (_instance == this) _instance = null;
        }
    }
}
#endif
