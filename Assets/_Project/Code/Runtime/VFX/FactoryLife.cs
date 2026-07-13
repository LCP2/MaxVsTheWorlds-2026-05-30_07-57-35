using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Mower Hutch, running (YT-78).
    ///
    /// The factory is the thing the whole slice teaches you to kill — clear the pressure by killing
    /// the SOURCE, not the symptoms — and until now it has been a box that robots happened to appear
    /// next to. Nothing about it said "this is a machine, and it is working". A player who doesn't
    /// read the Hutch as the source has no reason to go and break it.
    ///
    /// So while it lives it now RUNS: an impeller turning on its roof, heat coming off its vents, and
    /// exhaust puffing out of its stack — and it coughs, hard, on the exact frame a robot comes out of
    /// it. That last beat is the one that matters: it is what ties the thing chasing you to the thing
    /// that made it.
    ///
    /// When it dies, all of it stops dead. The Hutch hides its own body on death (MowerHutch keeps the
    /// GameObject alive so the robots already parented to it keep fighting), so every part built here
    /// has to go with it — a fan left spinning in the air over a factory that isn't there any more is
    /// worse than no fan at all.
    ///
    /// Reads gameplay, writes nothing to it. The Hutch's health and the spawner's live count are both
    /// public and both read-only from here; no gameplay file is touched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FactoryLife : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<FactoryLife>() != null) return;
            if (FindFirstObjectByType<MowerHutch>() == null) return;   // no factory, nothing to run

            new GameObject("FactoryLife").AddComponent<FactoryLife>();
        }

        [Header("Impeller")]
        [Tooltip("Degrees per second at full health. It winds UP as the factory dies — a machine " +
                 "being destroyed is a machine over-running, not one politely slowing down.")]
        [SerializeField] private float spinSpeed = 150f;
        [SerializeField] private float spinUrgency = 2.6f;

        [Header("Vents")]
        [Tooltip("The heat coming off it. Breathes slowly while it idles; flares on every robot.")]
        [SerializeField] private Color ventColor = new Color(1f, 0.55f, 0.18f);
        [SerializeField] private float ventPulseSpeed = 1.6f;

        [Header("Exhaust")]
        [SerializeField] private float exhaustRate = 5f;
        [SerializeField] private Color exhaustColor = new Color(0.62f, 0.60f, 0.58f, 0.42f);

        /// <summary>How hard it coughs when a robot comes out. Particles, in one burst.</summary>
        [SerializeField] private int coughParticles = 14;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MowerHutch _hutch;
        private EnemySpawner _spawner;

        private Transform _impeller;
        private ParticleSystem _exhaust;
        private readonly MeshRenderer[] _vents = new MeshRenderer[2];
        private MaterialPropertyBlock _ventMpb;

        private int _lastLive;
        private float _flash;     // 1 right after a robot appears, decays
        private bool _running = true;

        public bool Running => _running;

        private void OnEnable() => HudSignals.FactoryDestroyed += OnFactoryDestroyed;
        private void OnDisable() => HudSignals.FactoryDestroyed -= OnFactoryDestroyed;

        private void Awake()
        {
            _hutch = FindFirstObjectByType<MowerHutch>();
            if (_hutch == null) return;

            _spawner = _hutch.GetComponent<EnemySpawner>();
            _ventMpb = new MaterialPropertyBlock();

            // EVERY part of this machine brings its own material, and saying so is load-bearing.
            //
            // TWO separate systems sweep the scene and re-material anything they don't recognise:
            // WorldMaterials at scene load, and RuntimeSurfaceDirector every frame after it. Both
            // classify by SHAPE. They looked at a glowing vent, saw a flat slab, and painted it as a
            // stone floor — so the factory's heat came out as two dead grey rectangles nailed to its
            // roof, and its impeller came out as garden masonry.
            //
            // KeepsOwnMaterial is the marker both of them honour (it is how the garden kit's 217 props
            // survive them), and it covers everything parented below this object.
            gameObject.AddComponent<KeepsOwnMaterial>();

            // Measure the body ONCE, now, while it is still visible: MowerHutch disables its renderer
            // the moment it dies, and a bounds read after that is a zero-sized box at the origin.
            var body = _hutch.GetComponent<Renderer>();
            Bounds b = body != null
                ? body.bounds
                : new Bounds(_hutch.transform.position + Vector3.up, new Vector3(3f, 2f, 3f));

            Build(b);
            _lastLive = _spawner != null ? _spawner.LiveCount : 0;
        }

        // ---------------------------------------------------------------- building the machine

        /// <summary>
        /// Everything is parented to THIS object, not to the Hutch, and placed in world space.
        ///
        /// The Hutch's transform is scaled (3, 2, 3) — it is a stretched cube — and anything parented
        /// under it inherits that scale, which is how the factory's health bar ended up rendering
        /// metres wide (YT-71). Sitting outside it and working in world coordinates means a 20 cm vent
        /// is 20 cm.
        /// </summary>
        private void Build(Bounds b)
        {
            float top = b.max.y;

            // The impeller: a disc on the roof, and the roof is what a 72 deg camera actually SEES of
            // this machine. Anything mounted on a side face would be a detail nobody ever looks at.
            var hub = new GameObject("Impeller");
            hub.transform.SetParent(transform, worldPositionStays: false);
            hub.transform.position = new Vector3(b.center.x, top + 0.06f, b.center.z);
            _impeller = hub.transform;

            for (int i = 0; i < 3; i++)
            {
                var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blade.name = $"Blade{i}";
                Strip(blade);
                blade.transform.SetParent(_impeller, worldPositionStays: false);
                blade.transform.localRotation = Quaternion.Euler(0f, i * 120f, 0f);
                blade.transform.localPosition = blade.transform.localRotation * new Vector3(0.42f, 0f, 0f);
                blade.transform.localScale = new Vector3(0.84f, 0.06f, 0.18f);
                Paint(blade, SurfaceKind.Metal);
            }

            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "Hub";
            Strip(cap);
            cap.transform.SetParent(_impeller, worldPositionStays: false);
            cap.transform.localScale = new Vector3(0.26f, 0.05f, 0.26f);
            Paint(cap, SurfaceKind.Metal);

            // Vents, flanking the impeller on the roof. Additive, so they read as HEAT rather than as
            // paint — a lit panel and a bright panel are different things at a glance.
            for (int i = 0; i < 2; i++)
            {
                var vent = GameObject.CreatePrimitive(PrimitiveType.Quad);
                vent.name = $"Vent{i}";
                Strip(vent);
                vent.transform.SetParent(transform, worldPositionStays: false);
                vent.transform.position = new Vector3(
                    b.center.x + (i == 0 ? -1f : 1f) * b.extents.x * 0.62f,
                    top + 0.02f,                      // a whisker proud of the roof, or it z-fights
                    b.center.z);
                vent.transform.rotation = Quaternion.Euler(90f, 0f, 0f);   // lying flat, facing up
                vent.transform.localScale = new Vector3(0.5f, b.size.z * 0.55f, 1f);

                var r = vent.GetComponent<MeshRenderer>();
                r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                _vents[i] = r;
            }

            // The stack, and the exhaust out of it. Back corner, clear of the vulnerable core on the
            // front face — the core is the thing to shoot, and nothing of ours goes in front of it.
            var stack = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stack.name = "Stack";
            Strip(stack);
            stack.transform.SetParent(transform, worldPositionStays: false);
            Vector3 stackTop = new Vector3(b.center.x + b.extents.x * 0.66f,
                                           top + 0.34f,
                                           b.center.z + b.extents.z * 0.66f);
            stack.transform.position = stackTop;
            stack.transform.localScale = new Vector3(0.22f, 0.34f, 0.22f);
            Paint(stack, SurfaceKind.Metal);

            _exhaust = BuildExhaust(stackTop + Vector3.up * 0.34f);
        }

        private ParticleSystem BuildExhaust(Vector3 at)
        {
            var go = new GameObject("Exhaust");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = at;

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // puffs stay where they were coughed
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.5f);
            main.startColor = exhaustColor;
            main.gravityModifier = -0.03f;      // smoke rises
            main.maxParticles = 60;             // ambience budget: AmbiencePlayTests holds this under 200

            var emission = ps.emission;
            emission.rateOverTime = exhaustRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 14f;
            shape.radius = 0.06f;
            shape.rotation = new Vector3(-90f, 0f, 0f);   // straight up out of the pipe

            // Puffs swell and thin as they climb, which is what makes them read as smoke rather than
            // as a stream of dots.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.18f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.18f;
            noise.frequency = 0.4f;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            // A ParticleSystem from AddComponent has NO material and draws nothing (YT-47).
            r.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            ps.Play();
            return ps;
        }

        /// <summary>The machine's own steel — the same worn painted metal its body is made of, so the
        /// impeller reads as part of the Hutch rather than as something left on top of it.</summary>
        private static void Paint(GameObject go, SurfaceKind kind)
        {
            var r = go.GetComponent<MeshRenderer>();
            var mat = MaterialLibrary.Surface(kind);
            if (mat != null) r.sharedMaterial = mat;
        }

        /// <summary>Scenery. Nothing built here can be walked into or shot — the Hutch's own collider
        /// is the thing the Water Blaster has to hit, and an extra collider bolted to its roof would
        /// silently eat shots that were meant for it.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }

        // ---------------------------------------------------------------- running it

        private void Update()
        {
            if (_hutch == null) return;

            if (_running && !_hutch.IsAlive) Stop();
            if (!_running) return;

            float dt = Time.deltaTime;

            // A robot just came out. LiveCount is read, never written — the spawner is gameplay's, and
            // an art layer that had to be given an event would be an art layer reaching into it.
            if (_spawner != null)
            {
                int live = _spawner.LiveCount;
                if (live > _lastLive) Cough();
                _lastLive = live;
            }

            _flash = Mathf.Max(0f, _flash - dt * 2.4f);

            // Nearer death, it over-runs. Same idea as the core beating faster (YT-38) — the machine
            // is labouring, and that is a tell you can read from across the yard.
            float urgency = Mathf.Lerp(1f, spinUrgency, 1f - _hutch.Normalized);
            _impeller.Rotate(Vector3.up, spinSpeed * urgency * dt, Space.Self);

            // The vents breathe, and flare when it coughs.
            float breath = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * ventPulseSpeed));
            float glow = Mathf.Lerp(breath, 2.6f, _flash);
            Color c = ventColor * glow;
            c.a = 1f;
            foreach (var v in _vents)
            {
                if (v == null) continue;
                v.GetPropertyBlock(_ventMpb);
                _ventMpb.SetColor(BaseColorId, c);
                v.SetPropertyBlock(_ventMpb);
            }
        }

        /// <summary>A robot just came out of it. Cough.</summary>
        private void Cough()
        {
            _flash = 1f;
            if (_exhaust != null) _exhaust.Emit(coughParticles);
        }

        private void OnFactoryDestroyed(Vector3 _) => Stop();

        /// <summary>
        /// The source is gone. Everything stops, and everything goes.
        ///
        /// The Hutch hides its own body but keeps its GameObject alive (the robots it already spawned
        /// are parented to it and have to keep fighting), so nothing here can rely on being destroyed
        /// with it. An impeller still turning in mid-air over an absent factory is a worse bug than
        /// never having built one.
        /// </summary>
        private void Stop()
        {
            if (!_running) return;
            _running = false;

            if (_exhaust != null)
            {
                var emission = _exhaust.emission;
                emission.enabled = false;                  // stop making smoke; let the last of it drift off
            }

            foreach (var r in GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                r.enabled = false;
            }
        }
    }
}
