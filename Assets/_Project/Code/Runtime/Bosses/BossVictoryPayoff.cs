using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Upgrades;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// The boss-death payoff (YT-152). Beating Big Bermuda used to cut straight to the results card.
    /// Now the kill opens a beat of reward before the run resolves:
    ///
    ///  1. the blow-up plays — <c>BossSpectacle</c> + <c>BossDebris</c> already fire off the same
    ///     <see cref="HudSignals.BossDefeated"/> signal, so that theatre is not this director's job;
    ///  2. <b>four-plus fresh weapon parts fling out of the blast</b> on little ballistic arcs and land
    ///     around the arena as collectibles — the spoils that set Max up for the next fight;
    ///  3. the <c>BackyardExitGate</c> in the arena's back wall swings open (again, off the same signal);
    ///  4. and only when Max walks into that gateway — or after a grace timeout — does this director
    ///     raise <see cref="HudSignals.BossPayoffFinished"/>, the cue <c>RunTracker</c> waits on to show
    ///     the results. It should read as a reward, not an abrupt cut.
    ///
    /// The world is NOT frozen during this beat: the freeze lives in <c>ResultScreen</c>, which only
    /// builds once results are shown, so Max keeps moving at <c>timeScale</c> 1 and can actually walk to
    /// the gate and sweep up the parts. This director therefore runs on scaled time like gameplay,
    /// unlike the unscaled art directors that assumed the old instant freeze.
    ///
    /// Self-installs only where a boss can die (guarded on <see cref="BigBermudaBoss"/>), so it never
    /// leaks its per-frame work into the shared PlayMode scenes that have no boss.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossVictoryPayoff : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BossVictoryPayoff>() != null) return;
            if (FindFirstObjectByType<BigBermudaBoss>() == null) return;   // no boss, no death beat
            new GameObject("BossVictoryPayoff").AddComponent<BossVictoryPayoff>();
        }

        // ---- tunables (public so a test can shorten the wait, and so the beat is easy to retune) ----

        /// <summary>How many parts fling out. The ticket asks for at least four; five is one of each
        /// weapon part, so the spread reads as "all the spoils" rather than a random handful.</summary>
        public int partCount = 5;

        /// <summary>Grace period, seconds: if Max never walks to the gate, show the results anyway this
        /// long after the kill so the run can't stall on a won-but-idle arena.</summary>
        public float resultsTimeout = 12f;

        /// <summary>Hold off the walk-out check for this long after the kill, so the blow-up and the
        /// parts flinging out always get their moment before results can trigger.</summary>
        public float doorArmDelay = 1.2f;

        // ---- fixed feel ----

        private const float FlightTime = 0.75f;   // seconds a part spends in the air before it lands
        private const float HopHeight = 2.6f;     // arc height of the fling
        private const float ScatterMin = 4.5f;    // nearest a part lands from the blast
        private const float ScatterMax = 9.0f;    // farthest
        private const float LandingY = 0.6f;      // where a landed part hovers (matches Pickup.FloatHeight)
        private const float DoorDepth = 2.5f;     // how deep into the gateway counts as "through the door"
        private const float ArenaMargin = 2.0f;   // keep parts this far off the side walls
        private const float DoorClearZ = 3.0f;    // …and this far short of the door, so collecting ≠ exiting

        private struct PartFlight
        {
            public Pickup Pickup;
            public Vector3 From;
            public Vector3 To;
            public float T;
            public bool Landed;
        }

        private readonly List<PartFlight> _flights = new List<PartFlight>(8);
        private BigBermudaBoss _boss;
        private BackyardPathLayout _layout;
        private Transform _max;

        private bool _running;
        private bool _finished;
        private float _elapsed;

        private void Awake()
        {
            _boss = FindFirstObjectByType<BigBermudaBoss>();
        }

        private void OnEnable() => HudSignals.BossDefeated += OnDefeated;
        private void OnDisable() => HudSignals.BossDefeated -= OnDefeated;

        private void OnDestroy()
        {
            foreach (PartFlight f in _flights)
                if (f.Pickup != null) Destroy(f.Pickup.gameObject);
            _flights.Clear();
        }

        private void OnDefeated()
        {
            if (_running) return;
            _running = true;
            _elapsed = 0f;
            _layout = ResolveLayout();
            FlingParts(BossPos() + Vector3.up * 1.4f);
        }

        private void Update()
        {
            if (!_running || _finished) return;

            _elapsed += Time.deltaTime;
            CollectLanded();

            // Show results when Max steps into the open gateway, or when the grace period runs out.
            if (_elapsed >= doorArmDelay && MaxTransform() != null
                && IsAtDoor(_max.position, _layout, DoorDepth))
            {
                Finish();
                return;
            }

            if (_elapsed >= resultsTimeout) Finish();
        }

        /// <summary>Drive the arcs AFTER <see cref="Pickup.Update"/> (which every frame pins the pickup
        /// to its hover height + bob): running the flight in LateUpdate means our arc position wins for
        /// the frame, so a part reads as thrown rather than bobbing in place mid-air. Once it lands we
        /// stop touching it and hand it back to the pickup's own gentle bob.</summary>
        private void LateUpdate()
        {
            if (_flights.Count == 0) return;

            float dt = Time.deltaTime;
            for (int i = 0; i < _flights.Count; i++)
            {
                PartFlight f = _flights[i];
                if (f.Pickup == null || f.Landed) continue;

                f.T += dt / Mathf.Max(FlightTime, 0.0001f);
                if (f.T >= 1f)
                {
                    f.T = 1f;
                    f.Landed = true;
                    f.Pickup.transform.position = new Vector3(f.To.x, LandingY, f.To.z);
                }
                else
                {
                    f.Pickup.transform.position = Arc(f.From, f.To, f.T, HopHeight);
                }
                _flights[i] = f;
            }
        }

        private void FlingParts(Vector3 origin)
        {
            int n = Mathf.Max(4, partCount);
            for (int i = 0; i < n; i++)
            {
                PartKind kind = UpgradeCatalog.AllKinds[i % UpgradeCatalog.AllKinds.Length];
                Pickup p = Pickup.Create(PickupKind.Part);
                p.Part = kind;
                p.transform.SetParent(transform, worldPositionStays: false);
                p.transform.position = origin;
                p.gameObject.SetActive(true);   // active so PickupArtDirector dresses it with the part's art + glow

                _flights.Add(new PartFlight
                {
                    Pickup = p,
                    From = origin,
                    To = ScatterTarget(i, n, origin, _layout),
                    T = 0f,
                    Landed = false,
                });
            }
        }

        private void CollectLanded()
        {
            if (MaxTransform() == null) return;

            Vector3 m = _max.position;
            float r2 = PickupDirector.CollectRadius * PickupDirector.CollectRadius;

            for (int i = 0; i < _flights.Count; i++)
            {
                PartFlight f = _flights[i];
                if (f.Pickup == null || !f.Landed) continue;

                float dx = f.Pickup.transform.position.x - m.x;
                float dz = f.Pickup.transform.position.z - m.z;
                if (dx * dx + dz * dz > r2) continue;

                PickupWallet.AddPart(f.Pickup.Part);
                UpgradePart part = UpgradeCatalog.For(f.Pickup.Part);
                HudSignals.EmitPickup(f.Pickup.transform.position, part.Name, part.Accent);

                Destroy(f.Pickup.gameObject);
                f.Pickup = null;
                _flights[i] = f;
            }
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            HudSignals.EmitBossPayoffFinished();
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

        private Vector3 BossPos()
        {
            if (_boss == null) _boss = FindFirstObjectByType<BigBermudaBoss>();
            return _boss != null ? _boss.transform.position : _layout.ArenaCenter;
        }

        private BackyardPathLayout ResolveLayout()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            return path != null ? path.Layout : BackyardPathLayout.Default;
        }

        // ---- pure helpers (unit-testable without a scene) ----

        /// <summary>Where the <paramref name="index"/>th of <paramref name="count"/> parts lands: a
        /// golden-angle ring around the blast, deterministic (no Random the tests would have to seed),
        /// clamped inside the arena and kept clear of the side walls and the exit door so a part is
        /// always reachable and collecting one is never mistaken for walking out.</summary>
        public static Vector3 ScatterTarget(int index, int count, Vector3 origin, in BackyardPathLayout layout)
        {
            float a = index * 137.5f * Mathf.Deg2Rad;
            float radius = Mathf.Lerp(ScatterMin, ScatterMax, Frac(index * 0.618f));
            float x = origin.x + Mathf.Cos(a) * radius;
            float z = origin.z + Mathf.Sin(a) * radius;

            float xHalf = Mathf.Max(0f, layout.ArenaHalfWidth - ArenaMargin);
            float zMax = layout.ArenaEndZ - DoorClearZ;
            float zMin = layout.ArenaEndZ - layout.ArenaLength + ArenaMargin;

            x = Mathf.Clamp(x, -xHalf, xHalf);
            z = Mathf.Clamp(z, zMin, zMax);
            return new Vector3(x, LandingY, z);
        }

        /// <summary>True once Max is standing in the open gateway in the arena's back wall — within the
        /// doorway's half-width of the centre line and up against the far edge. This is "entered the
        /// door", the cue to show results.</summary>
        public static bool IsAtDoor(Vector3 maxPos, in BackyardPathLayout layout, float doorDepth)
        {
            return Mathf.Abs(maxPos.x) <= layout.GateHalfWidth
                && maxPos.z >= layout.ArenaEndZ - doorDepth;
        }

        /// <summary>A part's position along its throw at normalised time <paramref name="t"/>: a straight
        /// lerp on the floor plane with a sine hop added, so it leaves the blast, rises, and drops onto
        /// its landing spot (hop is 0 at both ends, peaks at the middle).</summary>
        public static Vector3 Arc(Vector3 from, Vector3 to, float t, float hop)
        {
            float x = Mathf.Lerp(from.x, to.x, t);
            float z = Mathf.Lerp(from.z, to.z, t);
            float y = Mathf.Lerp(from.y, to.y, t) + Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * hop;
            return new Vector3(x, y, z);
        }

        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}
