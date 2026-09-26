using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Intro;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The world's own finale exit (MV-915) — the trigger for the doorway out of the world's LAST boss
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
    /// boss(es)) to be dead first.
    ///
    /// MV-964: no longer a barrier that sinks in place — opening now hands off to
    /// <see cref="WorldJoinSequence"/> to cut the REAL door <see cref="WorldTransitions"/> authors for
    /// this world in its actual exit wall and build the corridor behind it, in the same frame the Weapon
    /// Core drops. Max keeps full control until he actually walks through it; <see cref="WorldJoinSequence"/>'s
    /// own crossing check takes over from there, so this class no longer tracks Max's position at all.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("map/gate")]
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

        /// <summary>The gate's own resolved state — open once this area's own final boss has actually
        /// died, not an authored flag (MV-915 AC4; MV-956 changed the trigger from RunComplete).</summary>
        public bool IsOpen { get; private set; }

        private WorldConfig _cfg;
        private MapData _map;
        private WorldTransitionEntry _entry;
        private int _fromWorldIndex;

        private void Awake()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null || path.Cfg?.dials == null || path.Map == null) { enabled = false; return; }

            var areaDirector = FindFirstObjectByType<AreaAccumulationDirector>();
            if (areaDirector == null) { enabled = false; return; }

            _cfg = path.Cfg;
            _map = path.Map;
            _fromWorldIndex = areaDirector.ActiveWorldIndex;
            _entry = WorldTransitions.For(_fromWorldIndex);
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
        /// component's own <see cref="Awake"/>-resolved geometry: a test driving pure boss-death/seal
        /// logic has no reason to build a real scene.</summary>
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

            // Geometry-independent tests (Mv915/MV956/MV959/Mv921) build this component with no
            // BackyardPath in the scene at all -- Awake already bailed on those, leaving these null, so
            // there is nothing to hand off to and IsOpen alone is the whole observable effect, same as
            // before this ticket.
            if (_entry != null && _cfg != null && _map != null)
                WorldJoinSequence.OpenExitDoor(_cfg, _map, _entry, _fromWorldIndex);
        }

        /// <summary>True once <paramref name="pos"/> is standing past a wall's own doorway — within its
        /// half-width of the door's centre and beyond the wall's own line, outward. Pure so a test (or
        /// <see cref="WorldJoinSequence"/>'s own crossing check) can drive the geometry directly, no
        /// scene required (MV-915 AC5; generalised to any wall, MV-964).</summary>
        public static bool IsBeyondFence(Vector3 pos, Wall wall, float wallCoord, Vector2 doorCenter, float halfWidth)
        {
            switch (wall)
            {
                case Wall.N: return pos.z >= wallCoord && Mathf.Abs(pos.x - doorCenter.x) <= halfWidth;
                case Wall.S: return pos.z <= wallCoord && Mathf.Abs(pos.x - doorCenter.x) <= halfWidth;
                case Wall.E: return pos.x >= wallCoord && Mathf.Abs(pos.z - doorCenter.y) <= halfWidth;
                case Wall.W: return pos.x <= wallCoord && Mathf.Abs(pos.z - doorCenter.y) <= halfWidth;
                default: return false;
            }
        }
    }
}
