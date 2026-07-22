using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Bosses
{
    /// <summary>
    /// The cadence of Big Bermuda's SECOND attack (YT-157): the brood volley. It periodically opens the
    /// side hatches, telegraphs, then flings a volley of robots. Pure sequencer — no MonoBehaviour — in
    /// the same shape as <see cref="BigBermudaBrain"/>, so its timing (and the all-important dodge/read
    /// window before the fling) is unit-testable without a scene.
    ///
    /// It runs ALONGSIDE the charge cycle, not inside it: the brain owns Reposition→Charge→Recover, and
    /// this owns the volley, so the two attacks compose rather than fight. The one place they touch is
    /// the <c>canVent</c> gate the boss passes in — the hatches never open while it is committing to a
    /// charge, so the spawn read and the charge read never blur into one another (the rig relies on the
    /// same rule, YT-150).
    ///
    /// Reads its numbers through <see cref="DevTuning"/> so the Settings panel (YT-138) retimes the
    /// volley live, exactly like the blade rain and the charge read their tuning fresh each time.
    /// </summary>
    public sealed class BroodVolley
    {
        private enum State { Idle, Windup, OpenHold }

        // A big "already waited long enough" mark. Used to re-arm READY after an abort so the volley
        // fires the instant the window reopens, rather than sitting through a fresh interval.
        private const float ReadyMark = 1e6f;

        private State _state = State.Idle;
        private float _timer;   // Idle: counts UP toward the interval. Windup/OpenHold: counts UP.

        public BroodVolley()
        {
            _timer = 0f;
        }

        /// <summary>True on the single tick the fling happens — the boss reads this edge to launch the
        /// robots, the same way it reads <see cref="BigBermudaBrain.JustEntered"/> to start a charge.</summary>
        public bool JustFired { get; private set; }

        /// <summary>
        /// The spawn telegraph, 0 shut … 1 flung, for the rig to open the hatches on (YT-150 seam). It
        /// ramps up across the wind-up — the hatches crack and the cavity floods BEFORE the robots come
        /// out, which is the whole point of a telegraph — and holds at 1 while the shell gapes and the
        /// swarm spills, then drops back to 0.
        /// </summary>
        public float SpawnWindup01
        {
            get
            {
                switch (_state)
                {
                    case State.Windup: return Mathf.Clamp01(_timer / Mathf.Max(0.01f, Windup));
                    case State.OpenHold: return 1f;
                    default: return 0f;
                }
            }
        }

        /// <summary>How many robots this volley throws, given the current phase — more once it reddens
        /// (the ticket asks the pressure to ramp with enrage). Read on the <see cref="JustFired"/> edge.</summary>
        public int RobotsThisVolley(bool enraged) => enraged
            ? Mathf.Max(1, Mathf.RoundToInt(DevTuning.Or(DevTuning.BossAddsPerVolley, BossTuning.RobotsPerVolleyEnraged)))
            : Mathf.Max(1, Mathf.RoundToInt(DevTuning.Or(DevTuning.BossAddsPerVolley, BossTuning.RobotsPerVolley)));

        /// <summary>
        /// Advance the cadence. <paramref name="canVent"/> is the boss's veto: false while it is
        /// committing to a charge (keep the two reads distinct) or while the arena already holds the most
        /// adds it is allowed (keep it kiteable). When vetoed mid-wind-up the hatches snap shut and the
        /// volley re-arms so it fires the moment the window reopens, rather than burning a whole interval.
        /// </summary>
        public void Tick(float dt, bool enraged, bool canVent)
        {
            JustFired = false;
            dt = Mathf.Max(0f, dt);

            switch (_state)
            {
                case State.Idle:
                    // Count time since the last volley UP, and open once it reaches the interval AND the
                    // window is clear. The interval is read fresh each tick, so enraging shortens the wait
                    // the player is ALREADY in, not only the next one — the waves speed up the moment it
                    // reddens.
                    _timer += dt;
                    if (_timer >= Interval(enraged) && canVent)
                    {
                        _state = State.Windup;
                        _timer = 0f;
                    }
                    break;

                case State.Windup:
                    // A charge (or hitting the add cap) during the wind-up aborts it: shut the hatches and
                    // re-arm READY, so it retries the instant the window reopens rather than after a fresh
                    // interval — otherwise the volley would rarely land in the gaps between charges.
                    if (!canVent)
                    {
                        _state = State.Idle;
                        _timer = ReadyMark;
                        break;
                    }
                    _timer += dt;
                    if (_timer >= Windup)
                    {
                        JustFired = true;
                        _state = State.OpenHold;
                        _timer = 0f;
                    }
                    break;

                case State.OpenHold:
                    _timer += dt;
                    if (_timer >= OpenHold)
                    {
                        _state = State.Idle;
                        _timer = 0f;
                    }
                    break;
            }
        }

        /// <summary>Seconds between volleys, shortened when enraged (faster adds as it reddens). Live
        /// through <see cref="DevTuning"/>.</summary>
        private static float Interval(bool enraged)
        {
            float baseInterval = DevTuning.Or(DevTuning.BossVolleyInterval, BossTuning.VolleyInterval);
            return enraged ? baseInterval * BossTuning.VolleyEnrageScale : baseInterval;
        }

        private static float Windup => DevTuning.Or(DevTuning.BossVolleyWindup, BossTuning.VolleyWindup);
        private static float OpenHold => BossTuning.VolleyOpenHold;
    }
}
