using System;
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Bosses
{
    /// <summary>What kind of blast a <see cref="BossDeathSequence"/> just fired — so a caller counting
    /// "exactly 4 chain explosions and 1 big blast" (MV-1190 AC1b) never miscounts the small spark
    /// burst that opens the sequence as one of them.</summary>
    public enum BossDeathBlastKind { Spark, Chain, Finale }

    /// <summary>
    /// MV-1190: the one shared boss-death choreography, used by every boss kind (Big Bermuda,
    /// Sludgequeen, Anchorhead) instead of each rig inventing its own. Lee, closing MV-152: Big
    /// Bermuda "kind of just sinks into the ground" — the old per-rig <c>TickDeath</c> toppled and
    /// sank the body into the lawn. This sequence replaces that: the body's own transform never moves
    /// below the Y it was standing at when it died — the caller's <see cref="Tick"/> loop only ever
    /// jitters X/Z, never Y.
    ///
    /// Runs on UNSCALED time, same reason every other boss-defeat director does
    /// (BossSpectacle/BossDebris/the old per-rig TickDeath) — the caller feeds it <c>Time.unscaledDeltaTime</c>
    /// so it keeps playing out even if something elsewhere freezes scaled time. <see cref="Tick"/>
    /// itself takes an explicit <paramref name="dt"/> rather than reading <c>Time</c> directly — the
    /// same "pulled out so a test can drive it at a controlled rate" seam
    /// <c>SludgequeenRig.Tick</c>/<c>AnchorheadBoss.Tick</c> already use — so an EditMode test can
    /// sample it frame by frame with no scene and no real clock.
    ///
    /// Three beats: 0.0-0.6s death throes (a spark burst plus the caller's own jitter/core-flicker),
    /// 0.6-1.6s a chain reaction of four explosions 0.25s apart, then the 1.6s finale blast. The
    /// Weapon Core orb (MV-1183) is NOT this class's job — that timing lives in
    /// <see cref="BossVictoryPayoff"/>, which already owns which boss's death counts and where the
    /// orb lands; this class only hands out the finale's own timestamp for other things (like that
    /// orb) to key off.
    /// </summary>
    public sealed class BossDeathSequence
    {
        public const float ThroesEnd = 0.6f;
        public const float ChainInterval = 0.25f;
        public const int ChainCount = 4;

        /// <summary>The finale blast's own timestamp — exactly when the chain reaction's four beats
        /// would have produced a fifth, so the two phases read as one continuous escalation.</summary>
        public const float FinaleTime = ThroesEnd + ChainCount * ChainInterval; // 1.6s

        public const float SequenceEnd = 2.2f;

        private const float JitterAmplitude = 0.08f;
        private const float JitterFrequencyHz = 20f;

        private readonly Vector3 _preDeathPos;
        private readonly Bounds _bodyBounds;
        private readonly Action<Vector3, float, BossDeathBlastKind> _onBlast;
        private readonly Action<Vector3> _onFinale;

        private float _elapsed;
        private bool _sparkFired;
        private int _chainFired;
        private bool _finaleFired;
        private readonly List<float> _chainFireTimes = new List<float>(ChainCount);
        private float? _finaleFireTime;

        /// <summary>Seconds of unscaled time this sequence has been ticked for a test to sample, and
        /// for the caller to decide when to deactivate the body.</summary>
        public float Elapsed => _elapsed;

        /// <summary>How many of the four chain explosions have fired so far — a test's "exactly 4"
        /// assertion reads this once the sequence is done.</summary>
        public int ChainExplosionsFired => _chainFired;

        /// <summary>The elapsed time (seconds) each chain explosion actually fired at — a test's
        /// "at the specified times ±0.05s" assertion (MV-1190 AC1b).</summary>
        public IReadOnlyList<float> ChainFireTimes => _chainFireTimes;

        /// <summary>True once the one finale blast has fired.</summary>
        public bool BigBlastFired => _finaleFired;

        /// <summary>The elapsed time (seconds) the finale blast actually fired at, or null if it
        /// hasn't yet.</summary>
        public float? FinaleFireTime => _finaleFireTime;

        /// <summary>True once the whole 2.2s sequence has played out — the caller's cue to deactivate
        /// the body.</summary>
        public bool Done => _elapsed >= SequenceEnd;

        /// <param name="preDeathPos">Where the body was standing the instant it started dying — every
        /// position <see cref="Tick"/> returns keeps this exact Y forever.</param>
        /// <param name="bodyBounds">The body's own world-space bounds — where the chain explosions
        /// and the finale blast are centred.</param>
        /// <param name="onBlast">Fired once per spark/chain/finale blast: world position, the existing
        /// boss-blast effect's size scale (60% for a chain hit, 200% for the finale), and which kind.</param>
        /// <param name="onFinale">Fired once, at <see cref="FinaleTime"/>, with the blast centre — the
        /// caller's cue for the chunks, the screen flash and (on the actual final boss) the orb.</param>
        public BossDeathSequence(Vector3 preDeathPos, Bounds bodyBounds,
            Action<Vector3, float, BossDeathBlastKind> onBlast, Action<Vector3> onFinale)
        {
            _preDeathPos = preDeathPos;
            _bodyBounds = bodyBounds;
            _onBlast = onBlast;
            _onFinale = onFinale;
        }

        /// <summary>Advance the sequence by <paramref name="dt"/> unscaled seconds. Returns the body's
        /// next position: X/Z jittered during the death throes, Y always exactly
        /// <paramref name="preDeathPos"/>'s — see the class doc for why that is the whole point.</summary>
        public Vector3 Tick(float dt)
        {
            _elapsed += dt;

            if (!_sparkFired)
            {
                _sparkFired = true;
                _onBlast?.Invoke(_bodyBounds.center, 0.15f, BossDeathBlastKind.Spark);
            }

            // A while loop, not an if: a dt larger than one chain interval (a slow frame, or a test
            // ticking in big steps) must still fire every threshold it crossed, never skip one.
            while (_chainFired < ChainCount && _elapsed >= ThroesEnd + _chainFired * ChainInterval)
            {
                Vector3 at = PointOnBounds(_chainFired);
                _chainFired++;
                _chainFireTimes.Add(_elapsed);
                _onBlast?.Invoke(at, 0.6f, BossDeathBlastKind.Chain);
            }

            if (!_finaleFired && _elapsed >= FinaleTime)
            {
                _finaleFired = true;
                _finaleFireTime = _elapsed;
                Vector3 centre = _bodyBounds.center;
                _onBlast?.Invoke(centre, 2f, BossDeathBlastKind.Finale);
                _onFinale?.Invoke(centre);
            }

            Vector3 jitter = Vector3.zero;
            if (_elapsed < ThroesEnd)
            {
                float phase = _elapsed * JitterFrequencyHz * (2f * Mathf.PI);
                jitter = new Vector3(Mathf.Sin(phase), 0f, Mathf.Cos(phase * 1.3f)) * JitterAmplitude;
            }

            return new Vector3(_preDeathPos.x + jitter.x, _preDeathPos.y, _preDeathPos.z + jitter.z);
        }

        /// <summary>A different, deterministic point on the body's bounds per chain explosion — no
        /// <c>Random</c>, same "golden-ratio fraction spread" idiom <c>BossDebris</c>/<c>BossVictoryPayoff</c>
        /// already use, so a test never has to seed anything to get a stable result.</summary>
        private Vector3 PointOnBounds(int index)
        {
            float fx = Frac(index * 0.613f + 0.11f);
            float fy = Frac(index * 0.371f + 0.47f);
            float fz = Frac(index * 0.829f + 0.23f);
            return new Vector3(
                Mathf.Lerp(_bodyBounds.min.x, _bodyBounds.max.x, fx),
                Mathf.Lerp(_bodyBounds.min.y, _bodyBounds.max.y, fy),
                Mathf.Lerp(_bodyBounds.min.z, _bodyBounds.max.z, fz));
        }

        private static float Frac(float x) => x - Mathf.Floor(x);
    }
}
