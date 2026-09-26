using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The world's own finale exit (MV-915) — a barrier across the doorway out of the world's LAST boss
    /// area (role "boss", index == <c>dials.areaCount</c>), separate from <see cref="BackyardExitGate"/>
    /// on purpose.
    ///
    /// <see cref="BackyardExitGate"/>/<c>BossVictoryPayoff</c> are a single scene-wide instance built
    /// once, positioned at whichever boss zone <see cref="MaxWorlds.Arena.Map.MapLayoutBridge"/> finds
    /// FIRST (in practice, a mid-run area like a12) and armed by the FIRST <see cref="HudSignals.BossDefeated"/>
    /// anywhere in the run — a single-boss-arena assumption from before World 1 authored bosses mid-run
    /// (a12, a20) ahead of the real finale (a30). Generalising that class to move/re-arm per area would
    /// change what a12's own boss death already does, which this ticket must not touch. This is a new,
    /// independent object instead: it only ever exists for the actual finale (a run with a next world to
    /// advance into), and it is built at the FINAL area's own zone.
    ///
    /// MV-956: opens on <see cref="HudSignals.BossDefeated"/> for its OWN final area (see
    /// <see cref="IsFinalBossAreaDefeat"/>) — the same event <c>BossVictoryPayoff</c> drops the Weapon
    /// Core on, so the orb and the open wall land in the same frame. It no longer waits on
    /// <see cref="HudSignals.RunComplete"/>, which required every robot in the area (not just its
    /// boss(es)) to be dead first — a real World 1 save could clear both Big Bermudas and never see the
    /// wall open while any other robot in a30 lingered.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldFinaleGate : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<WorldFinaleGate>() != null) return;

            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null || path.Cfg?.dials == null || path.Map == null) return;
            if (!HasNextWorld()) return;   // a final world's own last victory has no next world to gate

            new GameObject("WorldFinaleGate").AddComponent<WorldFinaleGate>();
        }

        /// <summary>Same "is there anywhere left to advance into" check <c>BossVictoryPayoff</c> makes
        /// before dropping a Weapon Core — kept local rather than shared, since it is two lines and the
        /// two classes must never depend on one another. MV-921: reads the world actually being PLAYED
        /// (<see cref="AreaAccumulationDirector.ActiveWorldIndex"/>), not a fresh <c>SaveSlotData.WorldIndex</c>
        /// read — a save's furthest-progress marker can be further along than the world this run is
        /// actually replaying, and that must not suppress the finale gate.</summary>
        private static bool HasNextWorld()
        {
            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            return areaDirector != null && WorldLibrary.Count > areaDirector.ActiveWorldIndex + 1;
        }

        private static readonly Color BarWarn = new Color(0.85f, 0.20f, 0.16f);   // shut: reads as blocked
        private const float PanelHeight = 3f;
        private const float PanelThickness = 0.3f;
        private const float SinkDuration = 0.6f;   // realtime seconds the shut panel takes to clear the doorway

        private float _fenceZ;
        private float _centerX;
        private float _halfWidth;

        private Collider _blocker;
        private Transform _panel;
        private bool _sinking;
        private float _sinkT;

        /// <summary>The gate's own resolved state — open once this area's own final boss has actually
        /// died, not an authored flag (MV-915 AC4; MV-956 changed the trigger from RunComplete).</summary>
        public bool IsOpen { get; private set; }

        private Transform _max;
        private bool _crossed;

        private void Awake()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null || path.Cfg?.dials == null || path.Map == null) { enabled = false; return; }

            int finalArea = path.Cfg.dials.areaCount;
            var zone = path.Map.Zone($"area{finalArea}");
            if (zone == null) { enabled = false; return; }

            _fenceZ = zone.ZMax - 0.3f;         // just inside the arena's own back wall
            _centerX = zone.x;
            _halfWidth = Mathf.Max(1.5f, zone.width * 0.25f);

            gameObject.AddComponent<KeepsOwnMaterial>();
            BuildBarrier();
        }

        private void BuildBarrier()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Barrier";
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(_centerX, PanelHeight * 0.5f, _fenceZ);
            go.transform.localScale = new Vector3(_halfWidth * 2f, PanelHeight, PanelThickness);

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, BarWarn);
            r.shadowCastingMode = ShadowCastingMode.Off;

            _blocker = go.GetComponent<Collider>();   // the Cube's own BoxCollider — this IS the block
            _panel = go.transform;
        }

        private void OnEnable() => HudSignals.BossDefeated += OnBossDefeated;
        private void OnDisable() => HudSignals.BossDefeated -= OnBossDefeated;

        /// <summary>MV-956: <see cref="HudSignals.BossDefeated"/> fires for the last living boss of ANY
        /// area emptying (a12, a20, a30 alike) — only open for THIS gate's own final area.</summary>
        private void OnBossDefeated()
        {
            if (!IsFinalBossAreaDefeat()) return;
            Open();
        }

        /// <summary>The exact same "was the area that just cleared the world's own final boss area"
        /// check <c>BossVictoryPayoff.IsFinalBossAreaDefeat</c> makes before dropping the Weapon Core —
        /// kept local rather than shared (same reasoning as <see cref="HasNextWorld"/>: two lines, and
        /// the two classes must never depend on one another). Deliberately independent of this
        /// component's own <see cref="Awake"/>-resolved zone geometry: that only exists to place the
        /// barrier, and requires a real <see cref="BackyardPath"/>/<see cref="MapData"/> in the scene,
        /// which a test driving pure boss-death/seal logic has no reason to build.</summary>
        private static bool IsFinalBossAreaDefeat()
        {
            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            WorldConfig cfg = areaDirector != null ? areaDirector.ActiveWorldConfig : null;
            if (cfg?.dials == null) return false;

            int areaIndex = BossCensus.LastDefeatedAreaIndex;
            WorldArea area = cfg.AreaByIndex(areaIndex);
            return area != null && area.IsBossRole && areaIndex == cfg.dials.areaCount;
        }

        private void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            if (_blocker != null) _blocker.enabled = false;   // clear the way the instant it's earned
            _sinking = true;
            _sinkT = 0f;
        }

        private void Update()
        {
            if (_sinking) TickSink();

            if (!IsOpen || _crossed) return;

            Transform max = MaxTransform();
            if (max == null) return;

            if (IsBeyondFence(max.position, _fenceZ, _centerX, _halfWidth))
            {
                _crossed = true;
                HudSignals.EmitFinaleGateCrossed();
            }
        }

        private void TickSink()
        {
            _sinkT += Time.deltaTime;
            float t = Mathf.Clamp01(_sinkT / SinkDuration);
            if (_panel != null) _panel.localPosition = new Vector3(_centerX, PanelHeight * (0.5f - t), _fenceZ);
            if (t >= 1f)
            {
                _sinking = false;
                if (_panel != null) _panel.gameObject.SetActive(false);
            }
        }

        private Transform MaxTransform()
        {
            if (_max == null)
            {
                GameObject g = GameObject.FindGameObjectWithTag("Player");
                if (g != null) _max = g.transform;
            }
            return _max;
        }

        /// <summary>True once <paramref name="pos"/> is standing past the open gateway — within its
        /// half-width of centre and beyond the fence line. Pure so a test can drive the geometry
        /// directly without a scene (MV-915 AC5).</summary>
        public static bool IsBeyondFence(Vector3 pos, float fenceZ, float centerX, float halfWidth)
        {
            return pos.z >= fenceZ && Mathf.Abs(pos.x - centerX) <= halfWidth;
        }
    }
}
