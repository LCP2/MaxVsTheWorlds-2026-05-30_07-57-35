using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;
using MaxWorlds.Player;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Environment ambience (YT-56): drifting motes in the air, and scorch marks left where things die.
    ///
    /// The brief is "alive, never distracting". Everything here is deliberately below the threshold
    /// of attention: it should make the arena feel inhabited without ever competing with a
    /// telegraph or a damage number for the player's eye. Combat feedback is loud; ambience is not.
    ///
    /// It used to sway the set dressing as well. It doesn't any more (YT-78) — that sway had never
    /// moved a single prop, and the wind now lives in the vertices where it can. See below.
    ///
    /// Self-installing; reads state and signals, writes nothing.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class AmbienceVfx : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<AmbienceVfx>() != null) return;
            new GameObject("AmbienceVFX").AddComponent<AmbienceVfx>();
        }

        [Header("Motes")]
        [Tooltip("Volume of drifting motes around the player, in world units.")]
        [SerializeField] private float moteVolume = 26f;

        [Tooltip("Motes per second. 14 through a 26 m box is roughly one seed head every two metres " +
                 "of yard, which at the play camera is an empty sky (YT-78).")]
        [SerializeField] private float moteRate = 26f;

        [Tooltip("Warm and translucent. The alpha carries the whole 'never distracting' clause — it " +
                 "is what keeps these from reading as projectiles.")]
        [SerializeField] private Color moteColor = new Color(1f, 0.94f, 0.76f, 0.42f);

        [Header("Decals")]
        [Tooltip("How many marks the lawn can hold at once. 24 was a fight that tidies up after " +
                 "itself: in a long run the oldest scorch was being recycled while the player could " +
                 "still see where it had been. 40 lets a run leave a history you can read off the " +
                 "ground, and is still a hard ceiling — no unbounded growth (YT-79).")]
        [SerializeField] private int maxDecals = 40;

        [Tooltip("How long a mark stays on the lawn. It used to be 9 seconds, which is a scorch mark " +
                 "with somewhere else to be: you kill a robot, you turn to face the next one, and by " +
                 "the time you look back the ground has forgotten it happened. A run leaves a HISTORY " +
                 "— the lawn should show where the fight has been. Bounded by maxDecals, not by time: " +
                 "the oldest mark is recycled, so a long run cannot grow this without limit (YT-79).")]
        [SerializeField] private float decalLife = 50f;
        [SerializeField] private Color scorchColor = new Color(0.10f, 0.09f, 0.07f, 0.62f);
        [SerializeField] private Color wreckColor = new Color(0.13f, 0.10f, 0.07f, 0.75f);

        /// <summary>MV-1296 ticket: "a scorch decal that fades in 5 s" — far short of the ordinary
        /// <see cref="decalLife"/> above, so a canister's mark needs its own, shorter lifetime.</summary>
        private const float CanisterScorchLife = 5f;

        private ParticleSystem _motes;
        private Transform _player;

        // MV-1264: resolved once the active world is known — null until then, since the world config
        // this reads (AreaAccumulationDirector.ActiveWorldConfig, the same "which world am I in" idiom
        // BossVictoryPayoff/WorldFinaleGate/SettingsPanel already use from a MonoBehaviour with no direct
        // world reference) is not guaranteed set yet at this component's own Awake — it self-installs via
        // RuntimeInitializeOnLoadMethod, ahead of whichever director sets it up for the level just loaded.
        private bool? _motesOffForWorld;

        private readonly List<Decal> _decals = new List<Decal>(32);

        private struct Decal
        {
            public Transform Tr;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Mpb;
            public Color Color;
            public float Age;
            public float Life;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            // The marks this layer burns into the lawn bring their own material, and until YT-79
            // nothing said so — so they didn't keep it.
            //
            // RuntimeSurfaceDirector (YT-61) sweeps every frame and re-materials anything it doesn't
            // recognise, and it did not recognise a decal: no CharacterSkin, no IDamageable, no
            // GroundRing, no KeepsOwnMaterial. Every scorch mark was picked up on the very next frame,
            // classified BY SHAPE — a flat quad, therefore ground — and repainted as a lit patch of
            // grass. A transparent black splat became an opaque green tile sitting on the green tile
            // it was drawn on, which is why nobody ever noticed the marks were missing: they had been
            // turned into more lawn.
            //
            // The sweep is right to be greedy. That greed is what stopped the boss's damage zones
            // rendering magenta. The decals just have to say what they are.
            gameObject.AddComponent<KeepsOwnMaterial>();

            _motes = BuildMotes();
        }

        private void OnEnable()
        {
            HudSignals.EnemyKilled += OnEnemyKilled;
            HudSignals.FactoryDestroyed += OnFactoryDestroyed;
            HudSignals.CanisterDetonated += OnCanisterDetonated;
        }

        private void OnDisable()
        {
            HudSignals.EnemyKilled -= OnEnemyKilled;
            HudSignals.FactoryDestroyed -= OnFactoryDestroyed;
            HudSignals.CanisterDetonated -= OnCanisterDetonated;
        }

        // --- motes ---

        /// <summary>
        /// A box of slow motes that travels with the player. Following the player (rather than
        /// filling a ~20x-viewport arena with particles) is what makes this affordable: the motes
        /// only ever exist where someone can actually see them.
        /// </summary>
        private ParticleSystem BuildMotes()
        {
            var go = new GameObject("AmbientMotes");
            go.transform.SetParent(transform, worldPositionStays: false);
            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.10f, 0.42f);

            // 4-12 cm was pollen seen from thirty metres: two to seven pixels of a 30%-alpha dot,
            // which is nothing. 11-26 cm is a drifting seed head — still small, still soft-edged, and
            // now actually on the screen. This is the cheapest "the air is moving" cue in the yard and
            // the only one that keeps working wherever the camera happens to be pointing (YT-78).
            main.startSize = new ParticleSystem.MinMaxCurve(0.11f, 0.26f);
            main.startColor = moteColor;
            main.maxParticles = 180;          // AmbiencePlayTests holds this at or under 200
            main.gravityModifier = -0.008f;   // they drift very slightly upward, like dust in sun

            var emission = ps.emission;
            emission.rateOverTime = moteRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(moteVolume, 6f, moteVolume);

            // Fade in and out at the ends of life, so motes never pop into or out of existence.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(1f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.28f;
            noise.frequency = 0.22f;   // lazy, wandering drift rather than straight lines

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            return ps;
        }

        // --- decals ---

        private void OnEnemyKilled(Vector3 pos) => AddDecal(pos, Random.Range(0.7f, 1.1f), scorchColor);
        private void OnFactoryDestroyed(Vector3 pos) => AddDecal(pos, 3.4f, wreckColor);
        private void OnCanisterDetonated(Vector3 pos) => AddDecal(pos, 1.6f, scorchColor, CanisterScorchLife);

        /// <summary>
        /// Leave a mark on the ground. Capped and recycled oldest-first: a long run kills a lot of
        /// robots, and an uncapped decal list would grow without bound for the whole session.
        /// <paramref name="life"/> defaults to the shared <see cref="decalLife"/> — MV-1296's own
        /// canister scorch is the one caller that overrides it, to the ticket's shorter 5 s.
        /// </summary>
        private void AddDecal(Vector3 worldPos, float radius, Color color, float life = -1f)
        {
            Decal d;
            if (_decals.Count >= maxDecals)
            {
                d = _decals[0];
                _decals.RemoveAt(0);
            }
            else
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Decal";
                var col = quad.GetComponent<Collider>();
                if (col != null) Destroy(col);
                quad.transform.SetParent(transform, worldPositionStays: false);

                d = new Decal
                {
                    Tr = quad.transform,
                    Renderer = quad.GetComponent<MeshRenderer>(),
                    Mpb = new MaterialPropertyBlock(),
                };
                d.Renderer.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Splat());
                d.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                d.Renderer.receiveShadows = false;
            }

            d.Tr.position = new Vector3(worldPos.x, 0.02f, worldPos.z);
            d.Tr.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);   // random spin: no two marks alike
            d.Tr.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            d.Color = color;
            d.Age = 0f;
            d.Life = life > 0f ? life : decalLife;
            _decals.Add(d);
        }

        private void TickDecals(float dt)
        {
            for (int i = _decals.Count - 1; i >= 0; i--)
            {
                var d = _decals[i];
                d.Age += dt;

                float p = Mathf.Clamp01(d.Age / d.Life);
                var c = d.Color;
                c.a *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, p));  // holds, then fades

                d.Renderer.GetPropertyBlock(d.Mpb);
                d.Mpb.SetColor(BaseColorId, c);
                d.Renderer.SetPropertyBlock(d.Mpb);

                if (d.Age >= d.Life)
                {
                    Destroy(d.Tr.gameObject);
                    _decals.RemoveAt(i);
                }
                else
                {
                    _decals[i] = d;
                }
            }
        }

        // --- prop sway: gone, and it never worked (YT-78) ---
        //
        // This used to rotate set-dressing transforms a fraction of a degree. It has been swaying
        // NOTHING since the yard was dressed: every kit prop is marked static, the scan skipped
        // anything static (rightly — a static-batched renderer's vertices are baked into world space,
        // so pushing its transform moves nothing), and after YT-75 every prop in the yard IS static.
        // So the list was empty, the sway was a no-op, and a FindObjectsByType<MeshRenderer>() over
        // the whole scene ran every two seconds to keep it empty.
        //
        // The wind now lives where it can actually move a plant: in the vertices, in
        // StylizedSurface.shader, driven by MaterialLibrary's Foliage profile. Only plants have a
        // wind strength, so only plants bend.

        // --- update ---

        private void Update()
        {
            float dt = Time.deltaTime;

            if (_motesOffForWorld == null)
            {
                var director = FindFirstObjectByType<AreaAccumulationDirector>();
                WorldConfig cfg = director != null ? director.ActiveWorldConfig : null;
                if (cfg != null)
                {
                    // MV-1264: "a sealed hull has no drifting dust at this density" — the Mothership is
                    // the one world this layer never checked against before installing motes everywhere.
                    // WorldConfig itself carries no Kit (that's WorldCatalog's own per-row field, MV-1141)
                    // so the active world's catalog row is looked up off the director's own world index.
                    WorldKit kit = WorldCatalog.Get(director.ActiveWorldIndex).Kit;
                    _motesOffForWorld = kit == WorldKit.Ship;
                    if (_motesOffForWorld.Value && _motes != null)
                        _motes.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            if (_player == null)
            {
                var pc = FindFirstObjectByType<PlayerController>();
                if (pc != null) _player = pc.transform;
            }
            if (_player != null && _motes != null)
            {
                var p = _player.position;
                _motes.transform.position = new Vector3(p.x, 2.5f, p.z);
            }

            TickDecals(dt);
        }
    }
}
