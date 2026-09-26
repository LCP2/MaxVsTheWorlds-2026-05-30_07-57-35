using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The LPPE's own firing VFX (MV-758): a muzzle flash on every pulse, an impact flash + sparks
    /// where a pulse lands on a robot, and a distinct bigger/brighter beat on the 4th hit that lands
    /// the Shock stun — "a player must be able to count to the stun by eye" (spec). Attached directly
    /// to the weapon and called synchronously from <see cref="MaxWorlds.Combat.PulseLaser"/>, the same
    /// per-weapon-VFX-component idiom <see cref="WaterVfx"/> uses for the RCDA rather than
    /// <see cref="CombatVfx"/>'s HudSignals-bus idiom — this is one weapon's own presentation, not a
    /// generic damage event any listener might care about.
    ///
    /// Every magnitude below is a resolved value read off <see cref="CombatVfxTuning"/>, never a
    /// literal buried in an Emit() call (spec: "so the numbers are tunable rather than buried").
    /// </summary>
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class LppeVfx : MonoBehaviour
    {
        // MV-805: retinted into the same orange family as SeekerPulse's own bolt colour (was a cool
        // cyan-white matching the water-read bolt this ticket fixes) — pushed past 1.0 the same way
        // CombatVfx.MissileFlashColor is (MV-351), so the flash actually clears the bloom threshold
        // against World 2's dim, fog-heavy Stormdrain grade (MV-754) rather than sitting at the same
        // brightness as everything else on screen. Each channel keeps its old over-1.0 headroom.
        private static readonly Color MuzzleColor = new Color(1.95f, 1.05f, 0.30f, 1f);
        private static readonly Color ImpactColor = new Color(1.55f, 0.85f, 0.26f, 1f);
        private static readonly Color SparkColor = new Color(1.00f, 0.66f, 0.28f, 1f);

        // The Shock beat borrows RobotEnemy.ShockTell / ShockZigzagVfx.ZigzagColor's own yellow
        // exactly, so the extra flash reads as the SAME event the zigzag already tells, not a second,
        // competing colour language.
        private static readonly Color ShockFlashColor = new Color(1f, 0.86f, 0.1f, 1f);
        private static readonly Color ShockSparkColor = new Color(1f, 0.95f, 0.45f, 1f);

        // MV-770: brighter than MuzzleColor — the windup has to read as the emitter charging UP to
        // the shot, not just another muzzle flash at the wrong time. MV-805: retinted to the same
        // orange family as MuzzleColor above.
        private static readonly Color WindupColor = new Color(2.20f, 1.30f, 0.42f, 1f);

        /// <summary>MV-858 ARC's own tell colour — bright blue-white (spec), pushed past 1.0 the same
        /// "over-1.0 headroom for bloom" trick <see cref="MuzzleColor"/> uses, deliberately NOT the
        /// bolt's own fire-orange family so an arc reads as a distinct electrical event.</summary>
        private static readonly Color ArcColor = new Color(1.4f, 1.7f, 2.2f, 1f);

        /// <summary>MV-825 item 7: the added hit-streak burst's own tail colour — the sparks fade from
        /// <see cref="SparkColor"/> into this, so they read as an extension of the bolt's own white-hot
        /// core landing, not a third unrelated colour.</summary>
        private static readonly Color BoltStreakColor = new Color(1.35f, 1.05f, 0.85f, 1f);

        private VfxBurst _muzzleFlash;
        private VfxBurst _impactFlash;
        private VfxBurst _impactSparks;
        private VfxBurst _shockFlash;
        private VfxBurst _shockSparks;
        private VfxBurst _windupRing;
        private VfxBurst _arcFlash;
        private VfxBurst _boltStreaks;
        private bool _initialized;

        /// <summary>How many muzzle flashes this instance has ever emitted — the resolved count a
        /// test reads for "one FireTick, one muzzle flash", since a ParticleSystem's own particleCount
        /// can't tell "one Emit call" from "one particle happened to still be alive".</summary>
        public int MuzzleFlashEmitCount { get; private set; }

        /// <summary>Builds every VfxBurst once. Idempotent, and called explicitly by the owner (see
        /// <see cref="WaterVfx.Init"/>'s equivalent shape) rather than from Awake — neither Awake nor
        /// OnEnable reliably run for AddComponent outside Play mode.</summary>
        public void Init()
        {
            if (_initialized) return;
            _initialized = true;

            var additive = VfxMaterials.Additive(VfxMaterials.Glow());

            _muzzleFlash = new VfxBurst("LppeMuzzleFlash", additive, 24, 0f, perFrameCap: 4);
            _impactFlash = new VfxBurst("LppeImpactFlash", additive, 24, 0f, perFrameCap: 4);
            _impactSparks = new VfxBurst("LppeImpactSparks", additive, 60, 0.8f, perFrameCap: 4, stretched: true);
            _shockFlash = new VfxBurst("LppeShockFlash", additive, 24, 0f, perFrameCap: 4);
            _shockSparks = new VfxBurst("LppeShockSparks", additive, 60, 0.8f, perFrameCap: 4, stretched: true);

            // MV-770: its own burst, never sharing _muzzleFlash — sharing would double-count a fire
            // cycle's flashes against the "one FireTick, one muzzle flash" contract (Mv758LppeVfxTests).
            _windupRing = new VfxBurst("LppeWindupRing", VfxMaterials.Additive(VfxMaterials.Ring()), 24, 0f, perFrameCap: 4);

            // MV-858: its own burst — ARC's flash must never share _impactFlash, or an arc landing
            // back-to-back with an ordinary impact would silently swallow one of the two beats.
            _arcFlash = new VfxBurst("LppeArcFlash", additive, 24, 0f, perFrameCap: 4);

            // MV-825 item 7: its own burst — layered on top of whichever impact beat above just
            // played, never replacing it, so it must never share either sparks burst.
            _boltStreaks = new VfxBurst("LppeBoltStreaks", additive, 60, 0.6f, perFrameCap: 4, stretched: true);
        }

        /// <summary>The muzzle punctuation (spec item 1) — a short, hard flash at the emitter, along
        /// the aim axis.</summary>
        public void Muzzle(Vector3 position, Vector3 forward)
        {
            if (!_initialized) return;
            CombatVfxTuning.LppeMuzzleFlashTuning t = CombatVfxTuning.LppeMuzzle();
            Vector3 axis = forward.sqrMagnitude > 1e-6f ? forward : Vector3.forward;

            _muzzleFlash.Emit(position + axis.normalized * t.ForwardOffset, 1,
                axis: axis, spreadDegrees: t.SpreadDegrees,
                speedMin: 0f, speedMax: 0f,
                sizeMin: t.Size, sizeMax: t.Size,
                lifeMin: t.Lifetime, lifeMax: t.Lifetime,
                colorA: MuzzleColor, colorB: MuzzleColor);
            MuzzleFlashEmitCount++;
        }

        /// <summary>The impact beat (spec item 3): a radial flash sized to the damage plus a handful
        /// of sparks along the surface normal, OR — on the hit that lands Shock — a visibly distinct,
        /// brighter flash with more/faster sparks emitted into its OWN separate pair of bursts, so the
        /// two never share an instance and the difference is structural, not just a size guess.</summary>
        public void Impact(Vector3 point, float damage, bool isShockHit)
        {
            if (!_initialized) return;

            if (isShockHit)
                Emit(_shockFlash, _shockSparks, point, CombatVfxTuning.LppeShockImpact(), ShockFlashColor, ShockSparkColor);
            else
                Emit(_impactFlash, _impactSparks, point, CombatVfxTuning.LppeImpact(damage), ImpactColor, SparkColor);

            // MV-825 item 7: "a spark burst of 8 short streaks ... plus the existing impact" — always
            // on top of whichever beat above just played, shock or not.
            CombatVfxTuning.LppeBoltStreakTuning streaks = CombatVfxTuning.LppeBoltImpactStreaks();
            _boltStreaks.Emit(point, streaks.Count,
                axis: Vector3.up, spreadDegrees: streaks.SpreadDegrees,
                speedMin: streaks.SpeedMin, speedMax: streaks.SpeedMax,
                sizeMin: streaks.SizeMin, sizeMax: streaks.SizeMax,
                lifeMin: streaks.Lifetime, lifeMax: streaks.Lifetime,
                colorA: SparkColor, colorB: BoltStreakColor);
        }

        /// <summary>The pre-Shock tell (spec part 2, item 1): a bright ring collapsing into the muzzle
        /// over the <see cref="CombatVfxTuning.LppeWindupTuning.LeadSeconds"/> before the shot that will
        /// land Shock actually fires — see <see cref="MaxWorlds.Combat.PulseLaser"/>'s own prediction of
        /// which shot that is (it never delays firing to play this).</summary>
        public void Windup(Vector3 position, Vector3 forward)
        {
            if (!_initialized) return;
            CombatVfxTuning.LppeWindupTuning t = CombatVfxTuning.LppeWindup();
            Vector3 axis = forward.sqrMagnitude > 1e-6f ? forward : Vector3.forward;

            _windupRing.Emit(position + axis.normalized * t.ForwardOffset, 1,
                axis: axis, spreadDegrees: 0f,
                speedMin: 0f, speedMax: 0f,
                sizeMin: t.RingSize, sizeMax: t.RingSize,
                lifeMin: t.LeadSeconds, lifeMax: t.LeadSeconds,
                colorA: WindupColor, colorB: WindupColor);
        }

        /// <summary>MV-858 ARC's own tell (spec item 3) — an instant jagged line from the hit point to
        /// the arc target plus a small impact flash on the second robot, so the extra damage reads as a
        /// deliberate arc rather than the target simply losing health for no visible reason.</summary>
        public void Arc(Vector3 from, Vector3 to)
        {
            if (!_initialized) return;
            CombatVfxTuning.LppeArcTuning t = CombatVfxTuning.LppeArc();

            ArcBoltVfx.Show(from, to, t, ArcColor);

            _arcFlash.Emit(to, 1,
                axis: Vector3.up, spreadDegrees: 0f,
                speedMin: 0f, speedMax: 0f,
                sizeMin: t.FlashSize, sizeMax: t.FlashSize,
                lifeMin: t.FlashLifetime, lifeMax: t.FlashLifetime,
                colorA: ArcColor, colorB: ArcColor);
        }

        private static void Emit(VfxBurst flash, VfxBurst sparks, Vector3 point,
            CombatVfxTuning.LppeImpactTuning t, Color flashColor, Color sparkColor)
        {
            flash.Emit(point, 1,
                axis: Vector3.up, spreadDegrees: 0f,
                speedMin: 0f, speedMax: 0f,
                sizeMin: t.FlashSize, sizeMax: t.FlashSize,
                lifeMin: t.FlashLifetime, lifeMax: t.FlashLifetime,
                colorA: flashColor, colorB: flashColor);

            sparks.Emit(point, t.SparkCount,
                axis: Vector3.up, spreadDegrees: t.SpreadDegrees,
                speedMin: t.SparkSpeedMin, speedMax: t.SparkSpeedMax,
                sizeMin: t.SparkSizeMin, sizeMax: t.SparkSizeMax,
                lifeMin: t.SparkLifeMin, lifeMax: t.SparkLifeMax,
                colorA: sparkColor, colorB: flashColor);
        }

        private void LateUpdate()
        {
            if (!_initialized) return;
            _muzzleFlash.EndFrame(); _impactFlash.EndFrame(); _impactSparks.EndFrame();
            _shockFlash.EndFrame(); _shockSparks.EndFrame(); _windupRing.EndFrame();
            _arcFlash.EndFrame(); _boltStreaks.EndFrame();
        }

        private void OnDestroy()
        {
            Dispose(_muzzleFlash); Dispose(_impactFlash); Dispose(_impactSparks);
            Dispose(_shockFlash); Dispose(_shockSparks); Dispose(_windupRing);
            Dispose(_arcFlash); Dispose(_boltStreaks);
        }

        private static void Dispose(VfxBurst b)
        {
            var go = b?.GameObject;
            if (go == null) return;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }
}
