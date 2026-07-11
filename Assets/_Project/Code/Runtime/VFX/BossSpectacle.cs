using System.Collections;
using UnityEngine;
using MaxWorlds.UI;
using MaxWorlds.Bosses;
using MaxWorlds.Feel;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Makes the Big Bermuda fight feel like an event (YT-55): an intro beat when it wakes, a jolt
    /// at the phase turn, and a staged defeat sequence.
    ///
    /// Driven entirely off the HudSignals the boss already raises (BossEngaged / BossHealthChanged /
    /// BossDefeated). No boss logic, no fight timings, no damage values are touched.
    ///
    /// IMPORTANT — why the defeat VFX run on UNSCALED time: the run ends the moment the boss dies.
    /// RunTracker hears BossDefeated, ends the run, and ResultScreen pauses everything with
    /// timeScale = 0 in the same frame. A normal, scaled-time explosion would therefore be frozen
    /// solid before a single particle moved. Running the death sequence unscaled means it still
    /// plays out over the stopped game behind the result card.
    ///
    /// It is still not the climax it should be, because the card lands on top of it instantly.
    /// Giving the defeat real screen time means delaying the result card, which lives in RunTracker
    /// — a gameplay-stream file this stream doesn't own. Raised with Lee on the ticket rather than
    /// reached into unilaterally.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossSpectacle : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BossSpectacle>() != null) return;
            new GameObject("BossSpectacle").AddComponent<BossSpectacle>();
        }

        private static readonly Color DustColor = new Color(0.62f, 0.56f, 0.42f, 0.8f);
        private static readonly Color FireHot = new Color(1f, 0.88f, 0.5f, 1f);
        private static readonly Color FireDeep = new Color(0.95f, 0.32f, 0.09f, 1f);
        private static readonly Color Debris = new Color(0.28f, 0.27f, 0.26f, 1f);
        private static readonly Color ShockColor = new Color(1f, 0.7f, 0.3f, 0.85f);

        private VfxBurst _dust;      // intro: the ground kicks up as it wakes
        private VfxBurst _fire;      // defeat
        private VfxBurst _debris;    // defeat
        private GroundRing _shock;   // expanding shockwave ring

        private Transform _boss;
        private float _lastHealth = 1f;
        private bool _phaseTurnDone;

        private void Awake()
        {
            var glow = VfxMaterials.Glow();
            var droplet = VfxMaterials.Droplet();

            _dust = new VfxBurst("BossDust", VfxMaterials.AlphaBlend(glow), 220, -0.1f, perFrameCap: 4);
            _fire = new VfxBurst("BossFire", VfxMaterials.Additive(glow), 320, -0.12f, perFrameCap: 6,
                                 unscaledTime: true);
            _debris = new VfxBurst("BossDebris", VfxMaterials.AlphaBlend(droplet), 260, 2.2f, perFrameCap: 6,
                                 unscaledTime: true);

            _shock = GroundRing.Create("BossShockwave");
            _shock.transform.SetParent(transform, worldPositionStays: false);
        }

        private void OnEnable()
        {
            HudSignals.BossEngaged += OnEngaged;
            HudSignals.BossHealthChanged += OnHealth;
            HudSignals.BossDefeated += OnDefeated;
        }

        private void OnDisable()
        {
            HudSignals.BossEngaged -= OnEngaged;
            HudSignals.BossHealthChanged -= OnHealth;
            HudSignals.BossDefeated -= OnDefeated;

            // A sequence interrupted part-way (scene change, replay) would otherwise leave the
            // shockwave ring stranded on the ground, mid-expansion, forever.
            StopAllCoroutines();
            if (_shock != null) _shock.Hide();
        }

        private void OnDestroy()
        {
            Kill(_dust); Kill(_fire); Kill(_debris);
            if (_shock != null) Destroy(_shock.gameObject);
        }

        private static void Kill(VfxBurst b)
        {
            if (b?.GameObject != null) Destroy(b.GameObject);
        }

        private Vector3 BossPos()
        {
            if (_boss == null)
            {
                var boss = FindFirstObjectByType<BigBermudaBoss>();
                if (boss != null) _boss = boss.transform;
            }
            return _boss != null ? _boss.position : Vector3.zero;
        }

        private static ScreenShake Shake()
        {
            var cam = Camera.main;
            return cam != null ? cam.GetComponent<ScreenShake>() : null;
        }

        // --- intro ---

        private void OnEngaged(string name, int phases)
        {
            _lastHealth = 1f;
            _phaseTurnDone = false;
            StopAllCoroutines();
            StartCoroutine(Intro());
        }

        /// <summary>It wakes: dust kicks off the ground, the camera shoves, a shockwave rolls out.
        /// Short — the boss's own intro window is brief, and holding the player any longer would be
        /// changing the fight, not dressing it.</summary>
        private IEnumerator Intro()
        {
            Vector3 at = BossPos();
            Shake()?.AddTrauma(0.55f);

            _dust.Emit(at + Vector3.up * 0.3f, 40,
                axis: Vector3.up, spreadDegrees: 95f,
                speedMin: 1.5f, speedMax: 6f,
                sizeMin: 0.6f, sizeMax: 1.9f,
                lifeMin: 0.6f, lifeMax: 1.3f,
                colorA: DustColor, colorB: FireDeep);

            yield return Shockwave(at, from: 1.2f, to: 7f, seconds: 0.7f, color: ShockColor);
        }

        // --- phase turn ---

        private void OnHealth(float normalized)
        {
            // The boss reports 2 phases; the turn is at half health. A jolt here tells the player
            // "it just got worse" without a UI element having to say so.
            if (!_phaseTurnDone && _lastHealth > 0.5f && normalized <= 0.5f && normalized > 0f)
            {
                _phaseTurnDone = true;
                StartCoroutine(PhaseTurn());
            }
            _lastHealth = normalized;
        }

        private IEnumerator PhaseTurn()
        {
            Vector3 at = BossPos();
            Shake()?.AddTrauma(0.7f);

            _fire.Emit(at + Vector3.up * 1.2f, 30,
                axis: Vector3.up, spreadDegrees: 110f,
                speedMin: 3f, speedMax: 9f,
                sizeMin: 0.5f, sizeMax: 1.4f,
                lifeMin: 0.25f, lifeMax: 0.6f,
                colorA: FireHot, colorB: FireDeep);

            var stop = FindFirstObjectByType<HitStop>();
            stop?.Request(0.07f, 0.12f);

            yield return Shockwave(at, from: 1f, to: 5.5f, seconds: 0.5f, color: FireDeep);
        }

        // --- defeat ---

        private void OnDefeated() => StartCoroutine(Defeat());

        /// <summary>
        /// Staged, not one bang: three quick flashes that read as it coming apart, then the big one.
        /// Everything here waits on realtime and emits into unscaled-time systems, because the game
        /// is frozen from the same frame the boss dies (see the class summary).
        /// </summary>
        private IEnumerator Defeat()
        {
            Vector3 at = BossPos() + Vector3.up * 1.2f;
            var shake = Shake();

            for (int i = 0; i < 3; i++)
            {
                shake?.AddTrauma(0.3f);
                _fire.Emit(at, 14,
                    axis: Vector3.up, spreadDegrees: 120f,
                    speedMin: 2f, speedMax: 6f,
                    sizeMin: 0.4f, sizeMax: 1.1f,
                    lifeMin: 0.2f, lifeMax: 0.45f,
                    colorA: FireHot, colorB: FireDeep);

                yield return new WaitForSecondsRealtime(0.13f);
            }

            // The big one.
            shake?.AddTrauma(1f);

            _fire.Emit(at, 70,
                axis: Vector3.up, spreadDegrees: 130f,
                speedMin: 4f, speedMax: 16f,
                sizeMin: 0.8f, sizeMax: 2.6f,
                lifeMin: 0.45f, lifeMax: 1.1f,
                colorA: FireHot, colorB: FireDeep);

            _debris.Emit(at, 45,
                axis: Vector3.up, spreadDegrees: 100f,
                speedMin: 5f, speedMax: 17f,
                sizeMin: 0.25f, sizeMax: 0.7f,
                lifeMin: 0.9f, lifeMax: 1.8f,
                colorA: Debris, colorB: FireDeep);

            yield return Shockwave(at, from: 1.5f, to: 11f, seconds: 0.9f, color: ShockColor);
        }

        // --- shared ---

        /// <summary>An expanding, fading ring on the ground. Realtime-driven so it survives the
        /// pause that lands on top of the boss's death.</summary>
        private IEnumerator Shockwave(Vector3 centre, float from, float to, float seconds, Color color)
        {
            float t = 0f;
            var ground = new Vector3(centre.x, 0f, centre.z);

            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / seconds);

                var c = color;
                c.a = color.a * (1f - p);                       // fades as it grows
                _shock.Show(ground, Mathf.Lerp(from, to, p), c);

                yield return null;
            }
            _shock.Hide();
        }
    }
}
