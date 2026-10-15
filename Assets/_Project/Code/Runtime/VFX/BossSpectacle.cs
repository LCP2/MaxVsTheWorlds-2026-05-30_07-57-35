using System.Collections;
using UnityEngine;
using MaxWorlds.UI;
using MaxWorlds.Bosses;
using MaxWorlds.Feel;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Makes the Big Bermuda fight feel like an event (YT-55): an intro beat when it wakes and a jolt
    /// at the phase turn, both still driven entirely off the HudSignals the boss already raises
    /// (BossEngaged / BossHealthChanged). No boss logic, no fight timings, no damage values touched.
    ///
    /// MV-1190: the defeat itself is no longer this class's own fixed choreography on
    /// <c>HudSignals.BossKilled</c> — <see cref="Blast"/> is now a reusable call any boss's own
    /// <see cref="MaxWorlds.Bosses.BossDeathSequence"/> makes at whatever position, moment and size
    /// its own choreography calls for (a chain-reaction hit, then the finale), so the death doesn't
    /// run on a fixed timer independent of the rest of the sequence (the shudder, the chunks, the
    /// orb) playing out around it.
    ///
    /// IMPORTANT — why <see cref="Blast"/>'s VFX run on UNSCALED time: the caller (a rig's death
    /// sequence) runs unscaled for exactly the reason the old fixed defeat sequence did — something
    /// elsewhere may freeze scaled time the moment the boss dies, and the blast must still play out
    /// over that freeze, not vanish under it.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("bosses")]
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
        }

        private void OnDisable()
        {
            HudSignals.BossEngaged -= OnEngaged;
            HudSignals.BossHealthChanged -= OnHealth;

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

        /// <summary>
        /// Give the bursts their frame budget back. This was missing, and it meant the boss's whole
        /// spectacle had a LIFETIME budget rather than a per-frame one.
        ///
        /// <see cref="VfxBurst"/> caps how many bursts it will fire in a frame — an uncapped
        /// burst-per-event is how a particle spike happens — and it refills that budget when its owner
        /// calls <see cref="VfxBurst.EndFrame"/> in LateUpdate. <see cref="CombatVfx"/> does. This did
        /// not. So the counter only ever went UP: four bursts of dust, six of fire, and then the boss
        /// woke up in silence and died without an explosion for the rest of the process.
        ///
        /// It survived a single playthrough by one emission — an intro, a phase turn and a defeat spend
        /// exactly five of the fire budget's six — which is why nobody caught it. Anything that engages
        /// a boss twice without a fresh scene falls off the cliff.
        /// </summary>
        private void LateUpdate()
        {
            _dust.EndFrame();
            _fire.EndFrame();
            _debris.EndFrame();
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

        /// <summary>
        /// MV-1190 step 1's own "medium camera shake" for the 0.0-0.6s death throes — the ticket's
        /// own line, "use the existing GameFeel shake" (the same <see cref="ScreenShake"/> this class's
        /// own <see cref="Shake"/> already reaches). A distinct beat from <see cref="Blast"/>'s own
        /// per-size trauma: the throes' spark blast alone is sized 0.15, which would only ever reach
        /// ~0.045 trauma — nowhere near "medium" next to the chain hit's 0.18 or the finale's 0.6.
        /// </summary>
        public void ThroesShake() => Shake()?.AddTrauma(0.4f);

        /// <summary>
        /// MV-1190: the reusable "boss blast" — what used to be this class's own fixed three-flashes-
        /// then-the-big-one <c>Defeat()</c> coroutine, now callable at whatever position, moment and
        /// size a boss's own <see cref="MaxWorlds.Bosses.BossDeathSequence"/> calls for (a chain-
        /// reaction hit at 60%, the finale at 200%), instead of this class choreographing its own
        /// fixed timing on <see cref="HudSignals.BossKilled"/>. <paramref name="sizeScale"/> 1 is
        /// roughly the old single "big one".
        /// </summary>
        public void Blast(Vector3 at, float sizeScale)
        {
            Vector3 pos = at + Vector3.up * 1.2f;
            Shake()?.AddTrauma(Mathf.Clamp01(0.3f * sizeScale));

            _fire.Emit(pos, Mathf.Max(1, Mathf.RoundToInt(24f * sizeScale)),
                axis: Vector3.up, spreadDegrees: 125f,
                speedMin: 3f * sizeScale, speedMax: 10f * sizeScale,
                sizeMin: 0.5f * sizeScale, sizeMax: 1.4f * sizeScale,
                lifeMin: 0.3f, lifeMax: 0.7f,
                colorA: FireHot, colorB: FireDeep);

            _debris.Emit(pos, Mathf.Max(1, Mathf.RoundToInt(16f * sizeScale)),
                axis: Vector3.up, spreadDegrees: 100f,
                speedMin: 4f * sizeScale, speedMax: 12f * sizeScale,
                sizeMin: 0.2f * sizeScale, sizeMax: 0.5f * sizeScale,
                lifeMin: 0.6f, lifeMax: 1.2f,
                colorA: Debris, colorB: FireDeep);

            StartCoroutine(Shockwave(pos, from: 1f * sizeScale, to: 6f * sizeScale,
                seconds: 0.4f + 0.3f * sizeScale, color: sizeScale >= 1.5f ? ShockColor : FireDeep));
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
