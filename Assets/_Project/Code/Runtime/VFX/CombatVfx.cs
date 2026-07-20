using System.Collections;
using UnityEngine;
using MaxWorlds.UI;
using MaxWorlds.Player;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Combat feedback VFX (YT-48): enemy hit sparks, the enemy death pop, Max's dash trail,
    /// and the Mower Hutch's destruction burst.
    ///
    /// It listens to the existing <see cref="HudSignals"/> bus rather than being called from
    /// gameplay code, and reads <see cref="PlayerController.IsDashing"/> for the dash. That
    /// means this whole feature adds no gameplay coupling: nothing in Enemies/, Factories/ or
    /// Player/ has to know the VFX exists, and deleting this file would change nothing but
    /// the picture.
    ///
    /// Note <c>HudSignals.DamageDealt</c> is only raised by enemy-side receivers (RobotEnemy,
    /// MowerHutch, BigBermudaBoss) — PlayerHealth does not raise it — so subscribing to it
    /// means "an enemy was hit", which is exactly the hit-reaction cue.
    ///
    /// Installs itself at runtime, so the scene file stays untouched (code-driven scenes).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatVfx : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<CombatVfx>() != null) return;
            new GameObject("CombatVFX").AddComponent<CombatVfx>();
        }

        // Palette. Robot enemies are gold/steel; the factory goes hot orange. Both sit against
        // the Backyard's golden/grass ground, so the sparks stay bright and the debris stays dark.
        private static readonly Color SparkHot = new Color(1f, 0.93f, 0.65f, 1f);
        private static readonly Color SparkGold = new Color(0.95f, 0.72f, 0.22f, 1f);
        private static readonly Color Debris = new Color(0.32f, 0.31f, 0.30f, 1f);
        private static readonly Color FireHot = new Color(1f, 0.85f, 0.45f, 1f);
        private static readonly Color FireDeep = new Color(0.92f, 0.35f, 0.10f, 1f);
        private static readonly Color Smoke = new Color(0.22f, 0.20f, 0.19f, 0.75f);
        private static readonly Color DashTrail = new Color(0.62f, 0.92f, 1f, 1f);

        private VfxBurst _hitSparks;    // enemy took a hit
        private VfxBurst _deathSparks;  // enemy died: bright bits
        private VfxBurst _deathDebris;  // enemy died: dark chunks
        private VfxBurst _boom;         // factory: fire
        private VfxBurst _boomDebris;   // factory: chunks
        private VfxBurst _boomSmoke;    // factory: lingering smoke
        private VfxBurst _dash;         // Max's dash trail
        private VfxBurst _levelUp;      // Max got stronger (YT-67)

        /// <summary>The power-up colour — cyan-white, so it can't be mistaken for the gold/orange
        /// that means "something took damage". Getting stronger must look like nothing else.</summary>
        private static readonly Color BoostBright = new Color(0.65f, 0.98f, 1f, 1f);
        private static readonly Color BoostDeep = new Color(0.20f, 0.65f, 1f, 1f);

        private PlayerController _player;
        private Vector3 _lastDashPos;
        private bool _wasDashing;

        private void Awake()
        {
            var droplet = VfxMaterials.Droplet();
            var glow = VfxMaterials.Glow();
            var additive = VfxMaterials.Additive(glow);
            var solid = VfxMaterials.AlphaBlend(droplet);
            var soft = VfxMaterials.AlphaBlend(glow);

            _hitSparks = new VfxBurst("HitSparks", additive, 220, 0.9f, perFrameCap: 10, stretched: true);
            _deathSparks = new VfxBurst("DeathSparks", additive, 300, 0.7f, perFrameCap: 8, stretched: true);
            _deathDebris = new VfxBurst("DeathDebris", solid, 240, 2.2f, perFrameCap: 8);
            _boom = new VfxBurst("FactoryFire", additive, 200, -0.15f, perFrameCap: 2);
            _boomDebris = new VfxBurst("FactoryDebris", solid, 180, 2.4f, perFrameCap: 2);
            _boomSmoke = new VfxBurst("FactorySmoke", soft, 140, -0.25f, perFrameCap: 2);
            _dash = new VfxBurst("DashTrail", additive, 200, 0f, perFrameCap: 90);
            // Negative gravity: the motes RISE. Everything else in this file falls, which is what
            // makes a rush upward read instantly as "you gained something" and not "something broke".
            _levelUp = new VfxBurst("LevelUp", additive, 160, -1.6f, perFrameCap: 2);
        }

        private void OnEnable()
        {
            HudSignals.DamageDealt += OnDamage;
            HudSignals.EnemyKilled += OnEnemyKilled;
            HudSignals.FactoryDestroyed += OnFactoryDestroyed;
            HudSignals.LevelUp += OnLevelUp;
        }

        private void OnDisable()
        {
            // Unsubscribing matters: HudSignals is static, so a missed -= would keep this
            // object (and its particle systems) alive across scene reloads.
            HudSignals.DamageDealt -= OnDamage;
            HudSignals.EnemyKilled -= OnEnemyKilled;
            HudSignals.FactoryDestroyed -= OnFactoryDestroyed;
            HudSignals.LevelUp -= OnLevelUp;
        }

        private void OnDestroy()
        {
            Dispose(_hitSparks); Dispose(_deathSparks); Dispose(_deathDebris);
            Dispose(_boom); Dispose(_boomDebris); Dispose(_boomSmoke); Dispose(_dash);
            Dispose(_levelUp);
        }

        /// <summary>Max got stronger (YT-67): a column of cyan motes surging up through him. It has
        /// to land in the same beat as the popup, or the boost is a number rather than a moment.</summary>
        private void OnLevelUp(int level, Vector3 pos)
        {
            _levelUp.Emit(pos + Vector3.up * 0.25f, 34,
                axis: Vector3.up, spreadDegrees: 22f,
                speedMin: 3.5f, speedMax: 7.5f,
                sizeMin: 0.12f, sizeMax: 0.3f,
                lifeMin: 0.5f, lifeMax: 0.95f,
                colorA: BoostBright, colorB: BoostDeep);
        }

        // --- events ---

        /// <summary>An enemy took a hit: a short spray of sparks off the body. Deliberately
        /// small — this fires every 0.1s per enemy under a sustained stream, so it has to
        /// read as texture, not as an explosion.</summary>
        private void OnDamage(Vector3 pos, float amount, bool crit)
        {
            int n = CombatVfxTuning.HitSparkCount(amount, crit);
            _hitSparks.Emit(pos + Vector3.up * 0.6f, n,
                axis: Vector3.up, spreadDegrees: 78f,
                speedMin: 2.6f, speedMax: 6.5f,
                sizeMin: 0.11f, sizeMax: 0.24f,
                lifeMin: 0.14f, lifeMax: 0.3f,
                colorA: SparkHot, colorB: SparkGold);
        }

        /// <summary>The kill: a bright pop of sparks plus dark chunks that arc and fall.
        /// The two together are what makes it read as a robot coming apart rather than a
        /// puff of dust.</summary>
        private void OnEnemyKilled(Vector3 pos)
        {
            Vector3 at = pos + Vector3.up * 0.7f;

            _deathSparks.Emit(at, 22,
                axis: Vector3.up, spreadDegrees: 90f,
                speedMin: 3.5f, speedMax: 9f,
                sizeMin: 0.09f, sizeMax: 0.22f,
                lifeMin: 0.22f, lifeMax: 0.5f,
                colorA: SparkHot, colorB: SparkGold);

            _deathDebris.Emit(at, 12,
                axis: Vector3.up, spreadDegrees: 70f,
                speedMin: 2f, speedMax: 6f,
                sizeMin: 0.12f, sizeMax: 0.26f,
                lifeMin: 0.45f, lifeMax: 0.8f,
                colorA: Debris, colorB: SparkGold);
        }

        /// <summary>
        /// The factory going down — the biggest moment in the slice, so it is a SEQUENCE rather than
        /// a bang (YT-109).
        ///
        /// All three bursts used to go off on the same frame. Fire, chunks and smoke arriving together
        /// is one event however much of it there is: it was over in about a second, and the thing the
        /// whole slice teaches you to do resolved faster than killing an ordinary robot. Staggering it
        /// costs nothing and buys the beat — the machine fails, THEN it lets go, THEN it burns.
        /// </summary>
        private void OnFactoryDestroyed(Vector3 pos) => StartCoroutine(FactoryDeath(pos));

        /// <summary>Timings live in <see cref="FactoryDeathTiming"/>, so the shape of the beat can be
        /// read (and tested) without stepping through a coroutine.</summary>
        private IEnumerator FactoryDeath(Vector3 pos)
        {
            Vector3 at = pos + Vector3.up * 1.2f;

            // 1. It fails. A short, contained jet out of the core — something inside has let go, but
            //    the building is still standing. This is the "uh oh" frame.
            _boom.Emit(at, 14,
                axis: Vector3.up, spreadDegrees: 45f,
                speedMin: 2.5f, speedMax: 6f,
                sizeMin: 0.3f, sizeMax: 0.8f,
                lifeMin: 0.25f, lifeMax: 0.5f,
                colorA: FireHot, colorB: FireDeep);

            _boomDebris.Emit(at, 8,
                axis: Vector3.up, spreadDegrees: 40f,
                speedMin: 3f, speedMax: 7f,
                sizeMin: 0.12f, sizeMax: 0.28f,
                lifeMin: 0.5f, lifeMax: 0.9f,
                colorA: Debris, colorB: FireDeep);

            yield return new WaitForSeconds(FactoryDeathTiming.FailToBlast);

            // 2. It lets go. The big one — everything the single-frame version used to be, and then
            //    some, now landing on a building the player has already watched start to fail.
            _boom.Emit(at, 44,
                axis: Vector3.up, spreadDegrees: 110f,
                speedMin: 3f, speedMax: 12f,
                sizeMin: 0.55f, sizeMax: 1.6f,
                lifeMin: 0.35f, lifeMax: 0.8f,
                colorA: FireHot, colorB: FireDeep);

            _boomDebris.Emit(at, 30,
                axis: Vector3.up, spreadDegrees: 85f,
                speedMin: 4f, speedMax: 13f,
                sizeMin: 0.18f, sizeMax: 0.48f,
                lifeMin: 0.9f, lifeMax: 1.6f,
                colorA: Debris, colorB: FireDeep);

            yield return new WaitForSeconds(FactoryDeathTiming.BlastToSmoke);

            // 3. It burns. Smoke arriving AFTER the fire is what makes it read as aftermath; arriving
            //    with it, it was just more stuff in the same puff.
            _boomSmoke.Emit(at, 26,
                axis: Vector3.up, spreadDegrees: 120f,
                speedMin: 0.5f, speedMax: 2.4f,
                sizeMin: 1.2f, sizeMax: 2.6f,
                lifeMin: 1.6f, lifeMax: 2.6f,
                colorA: Smoke, colorB: FireDeep);

            yield return new WaitForSeconds(FactoryDeathTiming.SmokeToEmbers);

            // 4. Last embers off the wreck, small and slow. The tail of the beat, not a fourth bang.
            _boom.Emit(at - Vector3.up * 0.6f, 10,
                axis: Vector3.up, spreadDegrees: 70f,
                speedMin: 0.8f, speedMax: 2.6f,
                sizeMin: 0.18f, sizeMax: 0.42f,
                lifeMin: 0.6f, lifeMax: 1.2f,
                colorA: FireDeep, colorB: Smoke);
        }

        // --- dash trail ---

        private void Update()
        {
            if (_player == null)
            {
                _player = FindFirstObjectByType<PlayerController>();
                if (_player == null) return;
            }

            bool dashing = _player.IsDashing;
            Vector3 pos = _player.transform.position;

            if (dashing)
            {
                // Lay the trail along the path actually travelled this frame, not just at the
                // current position: a dash covers a lot of ground in a few frames, and emitting
                // only at the sampled points leaves visible gaps in the streak.
                Vector3 from = _wasDashing ? _lastDashPos : pos;
                int steps = CombatVfxTuning.TrailSteps(Vector3.Distance(from, pos));
                for (int i = 0; i < steps; i++)
                {
                    Vector3 p = Vector3.Lerp(from, pos, (i + 1f) / steps) + Vector3.up * 0.7f;
                    _dash.Emit(p, 1,
                        axis: Vector3.up, spreadDegrees: 25f,
                        speedMin: 0.1f, speedMax: 0.7f,
                        sizeMin: 0.35f, sizeMax: 0.62f,
                        lifeMin: 0.16f, lifeMax: 0.3f,
                        colorA: DashTrail, colorB: Color.white);
                }
            }

            _wasDashing = dashing;
            _lastDashPos = pos;
        }

        private void LateUpdate()
        {
            _hitSparks.EndFrame(); _deathSparks.EndFrame(); _deathDebris.EndFrame();
            _boom.EndFrame(); _boomDebris.EndFrame(); _boomSmoke.EndFrame(); _dash.EndFrame();
            // _levelUp was missing here, which silently killed the boost VFX from level 3 on: its
            // per-frame budget is 2 and nothing ever refilled it, so the counter reached the cap on
            // the second level-up and every one after that was dropped. Found while adding YT-101.
            _levelUp.EndFrame();
        }

        private static void Dispose(VfxBurst b)
        {
            var go = b?.GameObject;
            if (go == null) return;
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
