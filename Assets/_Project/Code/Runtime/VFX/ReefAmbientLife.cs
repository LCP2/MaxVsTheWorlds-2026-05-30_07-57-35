using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1257: the Reef's ambient-life pass — marine snow, bubble streams, steam puffs and sparks,
    /// plus the light-life drivers (flicker, drum-core pulse). WHERE each of these gets attached is
    /// decided by <c>MaxWorlds.Arena.ReefAmbientLife</c> (needs the built map's own drums/manifolds/
    /// consoles/lamps — Arena-assembly types this VFX-assembly file has no reference to); this is only
    /// WHAT each one does once placed, same split <see cref="MaxWorlds.Rendering.ReefKit"/> and
    /// <c>MaxWorlds.Arena.ReefDressing</c> already keep for the rest of the Reef kit.
    ///
    /// Everything here is cheap by construction (ticket's own budget note): one shared
    /// <see cref="VfxBurst"/> ParticleSystem serves every manifold/console point of its kind rather than
    /// one per prop, and <see cref="ReefLightFlicker"/>/<see cref="ReefDrumCorePulse"/> only ever touch
    /// a renderer's <c>enabled</c> flag or a transform's local scale — no new draw calls, no allocation
    /// per frame.
    /// </summary>
    internal static class ReefAmbientHash
    {
        /// <summary>Same integer hash <see cref="SludgeBubbles"/> already uses for its own per-bubble
        /// phase offsets — reused here rather than re-derived, so every seeded schedule in this file
        /// scatters the same way that one already does.</summary>
        public static float Hash01(int seed, int i)
        {
            unchecked
            {
                int h = seed * 374761393 + i * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }
    }

    /// <summary>Tags one ambient-life particle emitter MV-1257 built for the Reef — marine snow, a
    /// drum's bubble stream, the shared steam-puff source or the shared spark source — so an EditMode
    /// test can count them as a RESOLVED value (testing policy Tier 2) without re-deriving which
    /// GameObjects count as "an emitter" from names or hierarchy shape. Carries no data of its own,
    /// same idiom as <see cref="MaxWorlds.Rendering.OceanLayer"/>/<see cref="MaxWorlds.Rendering.HydroponicBed"/>.</summary>
    public sealed class ReefAmbientEmitter : MonoBehaviour
    {
    }

    /// <summary>
    /// Drifting marine-snow particulate across the whole play view (MV-1257, Change item 1) — faint
    /// blue-white, low density. Follows the player the same way <see cref="AmbienceVfx"/>'s own pollen
    /// motes do: a fixed world-space volume would have to cover the whole map to guarantee it's always
    /// in frame, where a volume that travels with the player only ever exists where someone can see it.
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("reef_ambient")]
    public sealed class ReefMarineSnow : MonoBehaviour
    {
        private const float Volume = 30f;
        private const float Rate = 10f;   // low density per the ticket's own "low density" call
        private static readonly Color SnowColor = new Color(0.78f, 0.90f, 0.97f, 0.30f);

        private ParticleSystem _ps;
        private Transform _player;

        public static GameObject Build(Transform host)
        {
            if (host == null) return null;

            var go = new GameObject("Marine Snow");
            go.transform.SetParent(host, false);
            go.AddComponent<ReefAmbientEmitter>();
            go.AddComponent<ReefMarineSnow>().BuildParticles();
            return go;
        }

        private void BuildParticles()
        {
            var ps = gameObject.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.10f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = SnowColor;
            main.maxParticles = 140;
            main.gravityModifier = 0.012f;   // settles very slightly, like real suspended particulate

            var emission = ps.emission;
            emission.rateOverTime = Rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(Volume, 6f, Volume);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.15f;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            _ps = ps;
        }

        private void Update()
        {
            if (_player == null)
            {
                var pc = FindFirstObjectByType<MaxWorlds.Player.PlayerController>();
                if (pc != null) _player = pc.transform;
            }
            if (_player != null && _ps != null)
            {
                Vector3 p = _player.position;
                transform.position = new Vector3(p.x, 2.2f, p.z);
            }
        }
    }

    /// <summary>
    /// Flickers one renderer off for a brief blink on a seeded schedule (MV-1257, Change item 4: "about
    /// 10% of strip lights and lamps flicker on a seeded random schedule"). A sawtooth cycle with a
    /// short "off" slice near its start, not a per-frame coin flip — a coin flip reads as noise, a
    /// held blink reads as a light actually struggling. Visual only: <c>Update</c> never runs outside
    /// Play mode, so nothing here is EditMode-tested (testing policy Rule 2 — an on/off state over time
    /// is a Tier 3 rendered-pixel concern, not a resolved value).
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("reef_ambient")]
    public sealed class ReefLightFlicker : MonoBehaviour
    {
        private const float BlinkFraction = 0.04f;

        private Renderer _target;
        private float _phase;
        private float _cycleSeconds;
        private bool _isOff;

        public static ReefLightFlicker Attach(Renderer target, int seed)
        {
            if (target == null) return null;
            var flicker = target.gameObject.AddComponent<ReefLightFlicker>();
            flicker.Configure(target, seed);
            return flicker;
        }

        private void Configure(Renderer target, int seed)
        {
            _target = target;
            _cycleSeconds = 2.5f + ReefAmbientHash.Hash01(seed, 0) * 3.5f;   // 2.5-6s between blinks
            _phase = ReefAmbientHash.Hash01(seed, 1) * _cycleSeconds;
        }

        private void Update()
        {
            if (_target == null) return;

            float u = Mathf.Repeat((Time.time + _phase) / _cycleSeconds, 1f);
            bool shouldBeOff = u < BlinkFraction;
            if (shouldBeOff == _isOff) return;

            _isOff = shouldBeOff;
            _target.enabled = !_isOff;
        }
    }

    /// <summary>
    /// Pulses a drum core's local scale slowly (MV-1257, Change item 4: "drum cores pulse slowly, about
    /// once every 3 s") — a breathing scale on <see cref="MaxWorlds.Rendering.ReefKit.BuildDrum"/>'s own
    /// "Core" mesh, cheaper than an emission-intensity material property block and just as readable at
    /// the fixed ~72° camera. Visual only, same "never EditMode-tested" reasoning as
    /// <see cref="ReefLightFlicker"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("reef_ambient")]
    public sealed class ReefDrumCorePulse : MonoBehaviour
    {
        private const float PulsePeriod = 3f;
        private const float PulseAmplitude = 0.12f;

        private Vector3 _baseScale;
        private float _phase;

        public static ReefDrumCorePulse Attach(Transform core, int seed)
        {
            if (core == null) return null;
            var pulse = core.gameObject.AddComponent<ReefDrumCorePulse>();
            pulse.Configure(core, seed);
            return pulse;
        }

        private void Configure(Transform core, int seed)
        {
            _baseScale = core.localScale;
            _phase = ReefAmbientHash.Hash01(seed, 2) * PulsePeriod;
        }

        private void Update()
        {
            float u = Mathf.Repeat((Time.time + _phase) / PulsePeriod, 1f);
            float s = 1f + PulseAmplitude * Mathf.Sin(u * Mathf.PI * 2f);
            transform.localScale = _baseScale * s;
        }
    }

    /// <summary>
    /// One shared <see cref="VfxBurst"/> firing periodic puffs from a list of world positions (MV-1257,
    /// Change item 5: steam from "some manifold banks", sparks from "about 1 in 6 consoles"). One
    /// ParticleSystem per EFFECT KIND, not one per prop — the Reef's own hull-fill piece count (66
    /// manifolds, 35 consoles authored in <c>world3_config.json</c>) makes "one system per prop"
    /// exactly the per-object allocation pattern <see cref="VfxBurst"/>'s own doc comment says it
    /// replaces. Each point fires once per lap of its own seeded cycle, not on a per-frame threshold
    /// check (a threshold re-fires every frame it stays true; a tracked lap index fires exactly once).
    /// </summary>
    [DisallowMultipleComponent]
    [PerfSection("reef_ambient")]
    public sealed class ReefAmbientBurstEmitter : MonoBehaviour
    {
        private VfxBurst _burst;
        private Vector3[] _points;
        private float[] _phase;
        private int[] _lastLap;
        private float _interval;
        private int _count;
        private Vector3 _axis;
        private float _spreadDegrees;
        private float _speedMin, _speedMax, _sizeMin, _sizeMax, _lifeMin, _lifeMax;
        private Color _colorA, _colorB;

        public static ReefAmbientBurstEmitter Attach(Transform host, string name, Material material,
            Vector3[] points, int seed, float interval, int count, Vector3 axis, float spreadDegrees,
            float speedMin, float speedMax, float sizeMin, float sizeMax, float lifeMin, float lifeMax,
            Color colorA, Color colorB, int maxParticles, float gravity, int perFrameCap)
        {
            if (host == null || points == null || points.Length == 0) return null;

            var go = new GameObject(name);
            go.transform.SetParent(host, false);
            go.AddComponent<ReefAmbientEmitter>();

            var emitter = go.AddComponent<ReefAmbientBurstEmitter>();
            emitter._burst = new VfxBurst(name + " Burst", material, maxParticles, gravity, perFrameCap);
            // VfxBurst allocates its own ParticleSystem GameObject at the scene root (every other
            // caller is a combat effect that outlives any one map) — parent it under the emitter's own
            // GameObject so this one's lifetime instead follows the Reef map that built it.
            if (emitter._burst.GameObject != null) emitter._burst.GameObject.transform.SetParent(go.transform, false);
            emitter._points = points;
            emitter._phase = new float[points.Length];
            emitter._lastLap = new int[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                emitter._phase[i] = ReefAmbientHash.Hash01(seed, i) * interval;
                emitter._lastLap[i] = -1;
            }
            emitter._interval = interval;
            emitter._count = count;
            emitter._axis = axis;
            emitter._spreadDegrees = spreadDegrees;
            emitter._speedMin = speedMin;
            emitter._speedMax = speedMax;
            emitter._sizeMin = sizeMin;
            emitter._sizeMax = sizeMax;
            emitter._lifeMin = lifeMin;
            emitter._lifeMax = lifeMax;
            emitter._colorA = colorA;
            emitter._colorB = colorB;
            return emitter;
        }

        private void LateUpdate()
        {
            if (_burst == null || _points == null) return;

            float t = Time.time;
            for (int i = 0; i < _points.Length; i++)
            {
                int lap = Mathf.FloorToInt((t + _phase[i]) / _interval);
                if (lap == _lastLap[i]) continue;
                _lastLap[i] = lap;

                _burst.Emit(_points[i], _count, _axis, _spreadDegrees,
                    _speedMin, _speedMax, _sizeMin, _sizeMax, _lifeMin, _lifeMax, _colorA, _colorB);
            }
            _burst.EndFrame();
        }
    }
}
