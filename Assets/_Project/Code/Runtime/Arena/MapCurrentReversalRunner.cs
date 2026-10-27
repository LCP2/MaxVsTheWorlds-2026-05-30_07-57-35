using UnityEngine;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-1307: ticks ONE shared clock for every reversing current lane in the map — same "one shared
    /// clock, not one per lane" idiom as <see cref="MapVentDamageRunner"/>, so lanes with the same
    /// authored <c>reverseEvery</c> stay in lockstep. <see cref="MapGeometry.CurrentDriftAt"/> is the
    /// only reader: each lane resolves its own flip state from <see cref="Age"/> against its own authored
    /// <c>reverseEvery</c>, so lanes with different cadences can still share one clock. Self-installing,
    /// same idiom as <see cref="MapVentDamageRunner"/> — exists with zero scene wiring, including a bare
    /// test fixture.
    /// </summary>
    [MaxWorlds.Core.PerfSection("currents")]
    public sealed class MapCurrentReversalRunner : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<MapCurrentReversalRunner>() != null) return;
            new GameObject("MapCurrentReversalRunner").AddComponent<MapCurrentReversalRunner>();
        }

        private static MapCurrentReversalRunner s_instance;

        private float _age;

        /// <summary>Seconds since this clock started, for <see cref="MapGeometry.CurrentDriftAt"/> to
        /// resolve each lane's flip state against. 0 when no runner has ever ticked (e.g. a bare test
        /// fixture that never adds one).</summary>
        public static float Age => s_instance != null ? s_instance._age : 0f;

        private void Awake() => s_instance = this;

        private void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }

        private void Update() => TickCurrentReversal(Time.deltaTime);

        /// <summary>The runner's whole per-frame job, pulled out so a test can drive it directly at a
        /// cadence it controls (same "a caller decides the cadence" idiom as
        /// <see cref="MapVentDamageRunner.TickVentDamage"/>). Stamps <see cref="s_instance"/> itself
        /// rather than relying solely on <see cref="Awake"/> — EditMode never reliably runs Awake as an
        /// AddComponent side effect (same gotcha <c>MV1300CurrentLaneTests</c>/
        /// <c>MV1240PlasmaVentDamageTests</c> already work around for other components), so a test-built
        /// runner that only ever calls this method directly must still become the active clock.</summary>
        public void TickCurrentReversal(float dt)
        {
            s_instance = this;
            if (dt <= 0f) return;
            _age += dt;
        }
    }
}
