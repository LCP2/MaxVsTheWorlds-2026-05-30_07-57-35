using System.Runtime.InteropServices;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-910: neither <see cref="UnityEngine.Application.targetFrameRate"/> nor
    /// <c>UnityEngine.FrameTimingManager</c> can see iOS's own thermal-mitigation governor or Low Power
    /// Mode silently capping the display/CPU below what the game requested — that needs a native
    /// bridge (<c>Assets/Plugins/iOS/MvDeviceStateBridge.mm</c>), the same "confirm the least-code
    /// route" shape the ticket calls for: two `extern "C"` functions over <c>NSProcessInfo</c>, nothing
    /// else.
    ///
    /// <see cref="HasReading"/> false off-iOS (Editor, Windows standalone, WebGL) is a legitimate,
    /// displayed state, never a false zero — same convention as
    /// <see cref="FrameTimingProbe.HasReading"/> and <see cref="FrameCost"/>'s <c>NotAvailable()</c>.
    /// </summary>
    public static class IosDeviceStateProbe
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int _MvThermalState();

        [DllImport("__Internal")]
        private static extern bool _MvIsLowPowerModeEnabled();
#endif

        // NSProcessInfoThermalState: .nominal=0, .fair=1, .serious=2, .critical=3.
        private static readonly string[] ThermalStateNames = { "nominal", "fair", "serious", "critical" };

        public static bool HasReading =>
#if UNITY_IOS && !UNITY_EDITOR
            true;
#else
            false;
#endif

        public static string ThermalStateName
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                int state = _MvThermalState();
                return state >= 0 && state < ThermalStateNames.Length ? ThermalStateNames[state] : $"unknown({state})";
#else
                return "n/a";
#endif
            }
        }

        public static bool IsLowPowerModeEnabled =>
#if UNITY_IOS && !UNITY_EDITOR
            _MvIsLowPowerModeEnabled();
#else
            false;
#endif
    }
}
