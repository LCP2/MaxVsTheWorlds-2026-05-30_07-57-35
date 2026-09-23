using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.Save;
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
    /// advance into), it is built at the FINAL area's own zone, and it opens on
    /// <see cref="HudSignals.RunComplete"/> — which already fires only once that exact area is fully
    /// empty (every robot dead, no boss alive) — not on any boss dying.
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
        /// two classes must never depend on one another.</summary>
        private static bool HasNextWorld()
        {
            if (SaveSystem.ActiveSlot < 0) return false;
            SaveSlotData save = SaveSystem.Load(SaveSystem.ActiveSlot);
            return WorldLibrary.Count > save.WorldIndex + 1;
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

        /// <summary>The gate's own resolved state — open once <see cref="HudSignals.RunComplete"/> has
        /// actually landed for this area, not an authored flag (MV-915 AC4).</summary>
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

        private void OnEnable() => HudSignals.RunComplete += Open;
        private void OnDisable() => HudSignals.RunComplete -= Open;

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
