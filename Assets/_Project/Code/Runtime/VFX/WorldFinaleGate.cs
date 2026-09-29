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
    /// independent object instead: it only ever exists for a world's own actual finale (its final boss
    /// area), and it is built at that area's own zone.
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
    ///
    /// MV-1013: the last world (no <see cref="WorldTransitions"/> row — nowhere to build a corridor
    /// toward) still gets a real, closed exit door authored the same way (<see cref="WorldTransitions.ApplyExitDoorway"/>'s
    /// finale-only table) — <see cref="Open"/> just force-opens it directly instead of handing off to
    /// <see cref="WorldJoinSequence"/>, and THIS class tracks Max's own crossing of it (see <see cref="Update"/>),
    /// since there is no corridor sequence to do that job for a world with nowhere further to go.
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

            new GameObject("WorldFinaleGate").AddComponent<WorldFinaleGate>();
        }

        /// <summary>The gate's own resolved state — open once this area's own final boss has actually
        /// died, not an authored flag (MV-915 AC4; MV-956 changed the trigger from RunComplete).</summary>
        public bool IsOpen { get; private set; }

        private WorldConfig _cfg;
        private MapData _map;
        private WorldTransitionEntry _entry;
        private int _fromWorldIndex;
        private AreaGate _exitGate;

        /// <summary>MV-1013: this world's own exit doorway geometry (wall/coord/span), read straight off
        /// <see cref="MapData.exitDoorway"/> — set for every world now (<see cref="WorldTransitions.ApplyExitDoorway"/>'s
        /// finale-only table covers the last world, which has no <see cref="_entry"/>). Used only by
        /// <see cref="Update"/>'s own crossing check, for the corridor-less last-world case.</summary>
        private ExitDoorway? _doorway;

        private Transform _max;
        private bool _crossed;

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
            // MV-997: the real, closed map gate MapRuntime already built for this world's exit door --
            // Open() forces THIS open rather than handing WorldJoinSequence a runtime CutWallGap.
            _exitGate = path.ExitGate;
            _doorway = _map.exitDoorway;
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
        /// kept local rather than shared (two lines, and the two classes must never depend on one
        /// another). Deliberately independent of this component's own <see cref="Awake"/>-resolved
        /// geometry: a test driving pure boss-death/seal logic has no reason to build a real scene.</summary>
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
                WorldJoinSequence.OpenExitDoor(_cfg, _map, _entry, _fromWorldIndex, _exitGate);
            else if (_exitGate != null)
                // MV-1013: the last world's own finale -- a real door (see _doorway), but nowhere to
                // build a corridor toward, so there is no WorldJoinSequence to hand off to. Force it
                // open directly; Update() below watches for Max walking through it.
                _exitGate.ForceOpen();
        }

        /// <summary>MV-1013: the last world's own crossing check — every OTHER world hands this job to
        /// <see cref="WorldJoinSequence"/>'s own <c>AwaitingCrossing</c> phase (armed via <see cref="_entry"/>
        /// in <see cref="Open"/>), so this only ever runs when <see cref="_entry"/> is null and there is
        /// a real doorway (<see cref="_doorway"/>) to watch. Raises the same
        /// <see cref="HudSignals.FinaleGateCrossed"/> signal <c>WorldJoinSequence</c> raises once its
        /// corridor walk ends, so <c>RunTracker</c> cannot tell the two cases apart.</summary>
        private void Update()
        {
            if (_entry != null || _crossed || !IsOpen || !_doorway.HasValue) return;

            Transform max = MaxTransform();
            if (max == null) return;

            ExitDoorway d = _doorway.Value;
            Vector2 doorCenter = d.AlongX ? new Vector2(d.Hole.Mid, d.Coord) : new Vector2(d.Coord, d.Hole.Mid);
            if (!IsBeyondFence(max.position, d.Wall, d.Coord, doorCenter, d.Hole.Length * 0.5f)) return;

            _crossed = true;
            HudSignals.EmitFinaleGateCrossed();
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
