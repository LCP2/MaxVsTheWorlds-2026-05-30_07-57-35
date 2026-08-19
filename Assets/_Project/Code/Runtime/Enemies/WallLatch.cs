using UnityEngine;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Owns "am I currently rounding a wall, and which way" for one robot (MV-447). Pulled out of
    /// <see cref="RobotEnemy"/> for the same reason <see cref="PursuitStall"/> was: pure, no
    /// transform, no clock of its own, testable without a GameObject.
    ///
    /// Two bugs lived in the field it replaces (<c>_wallNormal</c>/<c>_wallTimer</c>, YT-68).
    ///
    /// Cause 1 — a limit cycle: the old code released the slide <c>wallMemory</c> (0.2s) after the
    /// last touch. That is long enough for the desired direction to swing straight back into the
    /// prop it had just cleared, touch again, and repeat at roughly 3-5 Hz. This latches on contact
    /// and releases only once the robot has displaced <see cref="ProgressDistance"/> along the wall
    /// since the latch began, or <see cref="MaxDuration"/> has elapsed since — losing contact does
    /// neither by itself.
    ///
    /// Cause 2 — a same-frame race: <c>OnControllerColliderHit</c> can fire more than once in a
    /// single step (a corner, a curved collider), and the old code kept only the last hit. Physics
    /// gives no ordering guarantee across those simultaneous contacts, so which normal "won" flipped
    /// from frame to frame and the remembered normal alternated between two near-perpendicular
    /// answers. <see cref="NoteHit"/> sums every hit normal reported since the last <see cref="Tick"/>
    /// instead of overwriting, so the combined normal for a given set of simultaneous contacts is the
    /// same regardless of the order physics reports them in. The latched normal itself is only ever
    /// replaced once the newly-combined normal differs from it by more than
    /// <see cref="ReplaceAngleDegrees"/> — a genuinely different surface, not jitter on the same one.
    /// </summary>
    public sealed class WallLatch
    {
        /// <summary>Metres of displacement along the latched wall the robot must make since the latch
        /// began before it is judged to have cleared the obstacle it was rounding.</summary>
        public const float ProgressDistance = 1.5f;

        /// <summary>Hard ceiling on how long a single latch may hold, so it can never truly stick
        /// forever even if the robot is making no along-wall progress at all.</summary>
        public const float MaxDuration = 2.5f;

        /// <summary>How far a newly-combined hit normal must differ from the currently latched one
        /// before it is accepted as a different surface rather than jitter on the current one.</summary>
        public const float ReplaceAngleDegrees = 35f;

        private Vector3 _hitAccum;
        private bool _active;
        private Vector3 _normal;
        private Vector3 _anchor;
        private float _elapsed;

        /// <summary>Call from <c>OnControllerColliderHit</c> for every non-floor, non-character contact
        /// this physics step. Sums rather than overwrites — see the class comment.</summary>
        public void NoteHit(Vector3 normal)
        {
            normal.y = 0f;
            _hitAccum += normal;
        }

        /// <summary>
        /// Call once per Chase tick, before steering. Consumes whatever <see cref="NoteHit"/> calls
        /// have arrived since the last call, advances or releases the latch, and returns the direction
        /// to actually walk: <paramref name="desired"/> unchanged while no wall is latched, or
        /// <paramref name="desired"/> slid along the latched normal otherwise (see
        /// <see cref="ObstacleSteering.SlideAlongWall"/>).
        /// </summary>
        /// <param name="desired">The direction the chase would walk with nothing in the way.</param>
        /// <param name="position">The robot's current position (Y ignored).</param>
        /// <param name="dt">Seconds since the last tick.</param>
        /// <param name="preferSign">This robot's stable tie-break for a head-on wall
        /// (<see cref="ObstacleSteering.PreferSignFor"/>).</param>
        public Vector3 Tick(Vector3 desired, Vector3 position, float dt, float preferSign)
        {
            if (_hitAccum.sqrMagnitude > 1e-6f)
            {
                Vector3 hitNormal = _hitAccum.normalized;
                _hitAccum = Vector3.zero;

                if (!_active)
                {
                    _active = true;
                    _normal = hitNormal;
                    _anchor = Flatten(position);
                    _elapsed = 0f;
                }
                else if (Vector3.Angle(_normal, hitNormal) > ReplaceAngleDegrees)
                {
                    _normal = hitNormal; // a genuinely different surface, not jitter on this one
                }
            }

            if (!_active) return desired;

            _elapsed += dt;
            Vector3 alongWall = Vector3.ProjectOnPlane(Flatten(position) - _anchor, _normal);
            if (alongWall.magnitude >= ProgressDistance || _elapsed >= MaxDuration)
            {
                _active = false;
                return desired;
            }

            return ObstacleSteering.SlideAlongWall(desired, _normal, preferSign);
        }

        /// <summary>Whether a wall is currently latched — exposed for tests only.</summary>
        public bool IsActive => _active;

        /// <summary>Drop any latch. A pooled robot doesn't inherit the last one's wall (MV-447, same
        /// convention as <see cref="PursuitStall.NoteSightHeld"/>).</summary>
        public void Reset()
        {
            _hitAccum = Vector3.zero;
            _active = false;
        }

        private static Vector3 Flatten(Vector3 v) { v.y = 0f; return v; }
    }
}
