using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// A planted-foot walk cycle for a six-legged body (MV-1132), built for
    /// <see cref="SludgequeenRig"/> but kept generic ("another legged machine" per the ticket) — it owns
    /// no transforms, just world-space foot state. Unlike <see cref="LegGaitDriver"/>'s sine-swing (which
    /// rotates a rigid hip and lets the foot skate across the floor), every foot here holds a WORLD
    /// position while planted, and only moves during its own short step — so a planted foot never drifts
    /// under a moving or turning body.
    ///
    /// Each leg independently starts a step when its "home" (where it belongs under the current body
    /// pose) has drifted far enough from where it is actually planted, provided neither ring-adjacent
    /// neighbour is already airborne. For six legs in a ring that local rule is enough on its own to
    /// produce the two alternating tripods (even legs / odd legs) the ticket asks for — nothing here
    /// hard-codes two groups.
    /// </summary>
    public sealed class HexapodGaitDriver
    {
        private const float StepThresholdAuthored = 0.7f;
        private const float StepOvershootAuthored = 0.6f;
        private const float StepLiftAuthored = 0.7f;
        private const float StepDurationDistanceAuthored = 0.4f;
        private const float JumpThresholdAuthored = 3f;
        private const float BodyDipAuthored = 0.05f;

        private const float StepMaxDuration = 0.35f;
        private const float DipEaseDuration = 0.1f;

        private readonly int _legCount;
        private readonly float[] _legAngleDeg;
        private readonly float _homeRadius;
        private readonly float _restHeight;

        private readonly float _stepThreshold;
        private readonly float _stepOvershoot;
        private readonly float _stepLift;
        private readonly float _stepDurationDistance;
        private readonly float _jumpThreshold;
        private readonly float _bodyDip;

        private readonly Vector3[] _plantedPos;
        private readonly Vector3[] _currentFootPos;
        private readonly bool[] _stepping;
        private readonly bool[] _justLanded;
        private readonly Vector3[] _stepStart;
        private readonly Vector3[] _stepEnd;
        private readonly float[] _stepElapsed;
        private readonly float[] _stepDuration;
        private readonly bool[] _airborneScratch;
        private readonly Vector3[] _homeScratch;
        private readonly float[] _excessScratch;
        private readonly int[] _priorityOrder;
        private readonly Vector3[] _lastHome;
        private readonly float[] _homeSpeedScratch;

        private Vector3 _lastBodyPos;
        private float _dipBlend;
        private bool _frozen;

        /// <summary>How far the body should sink this tick (always &lt;= 0) while any foot is airborne
        /// — apply this to the body's own Y after <see cref="Tick"/>, not before.</summary>
        public float BodyDipOffset { get; private set; }

        /// <param name="legAngleDeg">Each leg's bearing (degrees, same 0=local +Z convention
        /// <see cref="SludgequeenRig"/>'s own leg placement uses), relative to the body's own facing.</param>
        /// <param name="homeRadius">World distance from the body's centre to a planted foot's home.</param>
        /// <param name="restHeight">World height above the body's own Y a planted foot sits at.</param>
        /// <param name="scale">The rig's own scale factor — every other tunable here is authored for a
        /// body width of 6 and scales with it, same as every distance in the ticket.</param>
        public HexapodGaitDriver(float[] legAngleDeg, float homeRadius, float restHeight, float scale,
            Vector3 initialBodyPosition, float initialBodyYawDeg)
        {
            _legCount = legAngleDeg.Length;
            _legAngleDeg = (float[])legAngleDeg.Clone();
            _homeRadius = homeRadius;
            _restHeight = restHeight;

            _stepThreshold = StepThresholdAuthored * scale;
            _stepOvershoot = StepOvershootAuthored * scale;
            _stepLift = StepLiftAuthored * scale;
            _stepDurationDistance = StepDurationDistanceAuthored * scale;
            _jumpThreshold = JumpThresholdAuthored * scale;
            _bodyDip = BodyDipAuthored * scale;

            _plantedPos = new Vector3[_legCount];
            _currentFootPos = new Vector3[_legCount];
            _stepping = new bool[_legCount];
            _justLanded = new bool[_legCount];
            _stepStart = new Vector3[_legCount];
            _stepEnd = new Vector3[_legCount];
            _stepElapsed = new float[_legCount];
            _stepDuration = new float[_legCount];
            _airborneScratch = new bool[_legCount];
            _homeScratch = new Vector3[_legCount];
            _excessScratch = new float[_legCount];
            _priorityOrder = new int[_legCount];
            for (int i = 0; i < _legCount; i++) _priorityOrder[i] = i;
            _lastHome = new Vector3[_legCount];
            _homeSpeedScratch = new float[_legCount];

            ResetAllToHome(initialBodyPosition, initialBodyYawDeg);
            for (int i = 0; i < _legCount; i++) _lastHome[i] = _plantedPos[i];
            _lastBodyPos = initialBodyPosition;
        }

        public Vector3 FootPosition(int leg) => _currentFootPos[leg];

        /// <summary>True only on the tick <paramref name="leg"/> landed — the footfall event
        /// (MV-1132 AC1i), consumed once per <see cref="Tick"/>.</summary>
        public bool JustLanded(int leg) => _justLanded[leg];

        /// <summary>MV-1132 Change #9: no new step starts after this, and any leg currently mid-step
        /// drops to the floor exactly where it is on the next <see cref="Tick"/>.</summary>
        public void Freeze() => _frozen = true;

        public void Tick(Vector3 bodyPosition, float bodyYawDeg, float dt)
        {
            for (int i = 0; i < _legCount; i++) _justLanded[i] = false;

            if (_frozen)
            {
                for (int i = 0; i < _legCount; i++)
                {
                    if (!_stepping[i]) continue;
                    Vector3 drop = _currentFootPos[i];
                    drop.y = bodyPosition.y + _restHeight;
                    _plantedPos[i] = drop;
                    _currentFootPos[i] = drop;
                    _stepping[i] = false;
                }
                _lastBodyPos = bodyPosition;
                UpdateDip(dt, false);
                return;
            }

            Vector3 delta = bodyPosition - _lastBodyPos;
            delta.y = 0f;
            float travelled = delta.magnitude;
            _lastBodyPos = bodyPosition;

            if (travelled > _jumpThreshold)
            {
                ResetAllToHome(bodyPosition, bodyYawDeg);
                UpdateDip(dt, false);
                return;
            }

            Vector3 travelDir = travelled > 1e-5f ? delta / travelled : Vector3.zero;

            for (int i = 0; i < _legCount; i++)
            {
                _homeScratch[i] = HomeFor(i, bodyPosition, bodyYawDeg);
                _airborneScratch[i] = _stepping[i];
                // Negative/zero for a leg that isn't even eligible yet (still stepping, or not past
                // threshold) -- those sort last and StartStep's own distance re-check below is what
                // actually gates them, this is purely about WHICH eligible leg goes first.
                _excessScratch[i] = _stepping[i] ? float.NegativeInfinity
                    : Vector3.Distance(_homeScratch[i], _plantedPos[i]) - _stepThreshold;
                _priorityOrder[i] = i;

                // How fast THIS leg's own home is actually moving right now -- the translating case
                // (home moves at the body's own speed) and the turning-in-place case (home sweeps a
                // circle at radius * angular speed even though the body's own centre never moves) both
                // fall out of the same measurement, so StartStep's duration cap below naturally shortens
                // a step whenever ITS OWN target is moving fast, not only when the body is translating.
                _homeSpeedScratch[i] = dt > 1e-5f ? Vector3.Distance(_homeScratch[i], _lastHome[i]) / dt : 0f;
                _lastHome[i] = _homeScratch[i];
            }

            // Most-overdue-first (MV-1132 AC1g): a fixed scan order gives leg 0 a permanent first-mover
            // advantage over its neighbour (whichever leg a same-tick scan reaches first wins any tie,
            // since the neighbour checked afterward sees that decision already made) -- under sustained
            // fast rotation (home sweeping ~3 m/s at the authored 3.85 m foot radius) two legs two apart
            // can re-trigger every single cycle and NEVER both go idle on the same tick their blocked
            // neighbour's decision runs, starving it outright. A leg that keeps losing a tie keeps
            // drifting further past its own threshold every tick it stays blocked, while a leg that
            // keeps winning resets to ~0 excess every time it steps -- sorting by excess descending
            // means the most overdue leg always gets first pick, so a blocked leg's own growing excess
            // is what eventually guarantees its turn, regardless of index.
            for (int a = 1; a < _legCount; a++)
            {
                int key = _priorityOrder[a];
                float keyExcess = _excessScratch[key];
                int b = a - 1;
                while (b >= 0 && _excessScratch[_priorityOrder[b]] < keyExcess)
                {
                    _priorityOrder[b + 1] = _priorityOrder[b];
                    b--;
                }
                _priorityOrder[b + 1] = key;
            }

            for (int k = 0; k < _legCount; k++)
            {
                int i = _priorityOrder[k];
                if (_stepping[i]) continue;
                float dist = Vector3.Distance(_homeScratch[i], _plantedPos[i]);
                int left = (i - 1 + _legCount) % _legCount;
                int right = (i + 1) % _legCount;
                if (dist > _stepThreshold && !_airborneScratch[left] && !_airborneScratch[right])
                {
                    StartStep(i, _homeScratch[i], travelDir, _homeSpeedScratch[i]);
                    _airborneScratch[i] = true;
                }
            }

            bool anyAirborne = false;
            for (int i = 0; i < _legCount; i++)
            {
                if (!_stepping[i])
                {
                    _currentFootPos[i] = _plantedPos[i];
                    continue;
                }

                _stepElapsed[i] += dt;
                float t = Mathf.Clamp01(_stepElapsed[i] / _stepDuration[i]);
                float smooth = Mathf.SmoothStep(0f, 1f, t);
                Vector3 horiz = Vector3.Lerp(_stepStart[i], _stepEnd[i], smooth);
                float lift = _stepLift * Mathf.Sin(Mathf.PI * t);
                _currentFootPos[i] = horiz + Vector3.up * lift;

                if (t >= 1f)
                {
                    _plantedPos[i] = _stepEnd[i];
                    _currentFootPos[i] = _stepEnd[i];
                    _stepping[i] = false;
                    _justLanded[i] = true;
                }
                else anyAirborne = true;
            }

            UpdateDip(dt, anyAirborne);
        }

        private void StartStep(int leg, Vector3 homeNow, Vector3 travelDir, float homeSpeed)
        {
            _stepStart[leg] = _plantedPos[leg];
            Vector3 landing = homeNow + travelDir * _stepOvershoot;
            landing.y = homeNow.y;
            _stepEnd[leg] = landing;

            float duration = StepMaxDuration;
            if (homeSpeed > 1e-4f) duration = Mathf.Min(duration, _stepDurationDistance / homeSpeed);
            _stepDuration[leg] = Mathf.Max(duration, 1e-4f);
            _stepElapsed[leg] = 0f;
            _stepping[leg] = true;
        }

        private void ResetAllToHome(Vector3 bodyPosition, float bodyYawDeg)
        {
            for (int i = 0; i < _legCount; i++)
            {
                Vector3 home = HomeFor(i, bodyPosition, bodyYawDeg);
                _plantedPos[i] = home;
                _currentFootPos[i] = home;
                _stepping[i] = false;
                _lastHome[i] = home;
            }
        }

        private void UpdateDip(float dt, bool anyAirborne)
        {
            float target = anyAirborne ? 1f : 0f;
            _dipBlend = Mathf.MoveTowards(_dipBlend, target, dt / DipEaseDuration);
            BodyDipOffset = -_bodyDip * _dipBlend;
        }

        private Vector3 HomeFor(int leg, Vector3 bodyPosition, float bodyYawDeg)
        {
            Vector3 dir = AngleDir(bodyYawDeg + _legAngleDeg[leg]);
            Vector3 home = bodyPosition + dir * _homeRadius;
            home.y = bodyPosition.y + _restHeight;
            return home;
        }

        /// <summary>0 degrees = local +Z, increasing toward +X — must match
        /// <see cref="SludgequeenRig"/>'s own leg-placement convention exactly, since the two are
        /// geometrically bound (the hip world position the rig reports and the home this computes must
        /// agree on where "leg i's angle" points).</summary>
        private static Vector3 AngleDir(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }
    }
}
