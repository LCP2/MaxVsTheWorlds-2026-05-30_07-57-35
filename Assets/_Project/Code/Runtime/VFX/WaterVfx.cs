using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// All firing VFX for the Water Blaster (YT-47): the stream/jet leaving the muzzle,
    /// the muzzle spray, and the impact splash where the stream lands on a body.
    ///
    /// Owned entirely by the art stream — it carries no gameplay state and makes no
    /// gameplay decisions. <see cref="MaxWorlds.Combat.WaterBlaster"/> drives it with
    /// three cosmetic calls (<see cref="Init"/>, <see cref="SetStreaming"/>,
    /// <see cref="Splash"/>); turning it off changes nothing but the picture.
    ///
    /// Built in code (no committed prefab/asset), attached by the blaster itself, so the
    /// scene file is untouched and a fresh clone builds this identically in CI.
    ///
    /// Perf shape: four ParticleSystems total, created once. Splashes are emitted into one
    /// shared world-space system via Emit() rather than spawning a GameObject per impact,
    /// and are capped per frame (<see cref="WaterVfxTuning.MaxSplashesPerFrame"/>), so a
    /// stream raking across 20–30 enemies costs no allocations and no spawn churn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterVfx : MonoBehaviour
    {
        [Header("Palette")]
        [Tooltip("Core of the jet — near-white, reads as pressurised water.")]
        [SerializeField] private Color coreColor = new Color(0.85f, 0.97f, 1f, 1f);
        [Tooltip("Body of the stream and the droplets.")]
        [SerializeField] private Color waterColor = new Color(0.31f, 0.76f, 0.97f, 1f);
        [Tooltip("Tail of the stream as it thins out and falls away.")]
        [SerializeField] private Color deepColor = new Color(0.01f, 0.53f, 0.82f, 0.35f);

        [Header("Stream")]
        [Tooltip("Seconds a droplet takes to cross the full stream length. Lower = faster, harder jet.")]
        [SerializeField] private float travelTime = 0.32f;
        [Tooltip("Droplets per second in the stream body. This has to be high: the stream is only " +
                 "on screen for travelTime, so the live particle count is roughly rate x travelTime. " +
                 "Too low and it reads as a scatter of dots instead of a jet.")]
        [SerializeField] private float streamRate = 320f;
        // The stream's spread is NOT authored here any more (YT-110). It is derived from the
        // blaster's own cone in Init, so the water you can see and the arc the reticle draws cannot
        // drift apart the way they had: this was a 6 degree jet drawn under a 35 degree indicator,
        // and the reticle got the blame for being "far wider than the spray" when it was the one
        // telling the truth about the weapon.
        private float streamAngle = SprayHalfAngleFor(35f);

        /// <summary>
        /// How much of the blaster's cone the visible water fills.
        ///
        /// A half, so the indicator ends up drawing twice the width of the spray — which is what
        /// YT-110 asked for, and it is the right shape for the job: the water reads as a definite
        /// jet with a softer envelope of reach around it, rather than a wall of particles that
        /// hides the robots you are aiming at (Craft Bible: juice must never obscure readability).
        /// </summary>
        public const float SprayFillsFractionOfCone = 0.5f;

        /// <summary>The visible stream's cone half-angle for a weapon with this spread.</summary>
        public static float SprayHalfAngleFor(float coneHalfAngle) =>
            Mathf.Max(1f, coneHalfAngle * SprayFillsFractionOfCone);

        /// <summary>The stream's actual cone half-angle, in degrees. Exposed so a test can hold it
        /// against the reticle's without reading particles off the screen.</summary>
        public float StreamHalfAngle => streamAngle;

        /// <summary>The cone half-angle the LIVE stream EMITTER is shaped to, read off the particle
        /// system itself (not a cached field). A nozzle upgrade must move THIS, not just the reticle —
        /// the emitter reading a stale value is the YT-141 bug. 0 before the stream is built.</summary>
        public float EmitterHalfAngle => _stream != null ? _stream.shape.angle : 0f;

        /// <summary>The stream emitter's current top launch speed. It scales with reach (the droplet
        /// lifetime is fixed), so a longer beam is a faster jet — this is how a test proves the
        /// emitter's REACH grew, not only the aim outline's. 0 before the stream is built.</summary>
        public float EmitterSpeed => _stream != null ? _stream.main.startSpeed.constantMax : 0f;
        [Tooltip("How far in front of the blaster's origin the water leaves the nozzle, in " +
                 "multiples of the stream radius. Stretched particles trail a tail behind " +
                 "themselves, so emitting at the origin makes the jet appear to pass through " +
                 "and out the back of the gun. This pushes the nozzle clear of the body.")]
        [SerializeField] private float muzzleOffset = 1.9f;

        [Header("Splash")]
        [SerializeField] private float splashSpeed = 4.5f;
        [SerializeField] private float splashLifetime = 0.45f;
        [Tooltip("Cone half-angle the splash droplets scatter through, degrees.")]
        [SerializeField] private float splashSpread = 42f;

        private ParticleSystem _stream;     // the water body — alpha-blended droplets
        private ParticleSystem _core;       // bright additive centre of the jet
        private ParticleSystem _muzzle;     // spray + bloom at the nozzle
        private ParticleSystem _splash;     // shared, world-space; Emit()-driven per impact
        private ParticleSystem _flash;      // shared, additive pop at each impact

        private float _range = 6f;
        private float _radius = 0.6f;
        private bool _built;
        private bool _streaming;
        private int _splashesThisFrame;

        /// <summary>Build the systems to match the blaster's actual reach. Safe to call twice.</summary>
        /// <summary>
        /// Build the water for a weapon of this reach and spread.
        ///
        /// <paramref name="coneHalfAngle"/> is the blaster's REAL cone — the same number the hit
        /// test and the aim reticle use — so the stream is sized from the weapon rather than
        /// authored next to it and left to rot (YT-110).
        /// </summary>
        public void Init(float range, float radius, float coneHalfAngle)
        {
            _range = Mathf.Max(0.1f, range);
            _radius = Mathf.Max(0.05f, radius);
            streamAngle = SprayHalfAngleFor(coneHalfAngle);
            if (_built) { Refit(); return; }   // a nozzle upgrade re-shapes the live stream (YT-141)
            _built = true;

            _stream = BuildStream();
            _core = BuildCore();
            _muzzle = BuildMuzzle();
            _splash = BuildSplash();
            _flash = BuildFlash();

            SetStreaming(false, force: true);
        }

        /// <summary>
        /// Re-apply reach and spread to the already-built stream (YT-141), so a nozzle upgrade
        /// re-shapes the visible water to match the reticle and the hit test — the three must agree
        /// (YT-110). Cheaper than rebuilding the particle systems: the shape angle and the droplet
        /// lifetime (which is what makes the water die at the weapon's real reach) are just re-set.
        /// </summary>
        private void Refit()
        {
            if (_stream != null)
            {
                float speed = Reach / Mathf.Max(0.05f, travelTime);   // mirrors BuildStream
                var m = _stream.main;
                m.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.82f, speed * 1.15f);
                m.startLifetime = Reach / speed;
                var s = _stream.shape; s.angle = streamAngle;
            }
            if (_core != null)
            {
                float speed = Reach / Mathf.Max(0.05f, travelTime * 0.9f);   // mirrors BuildCore
                var m = _core.main;
                m.startSpeed = speed;
                m.startLifetime = (Reach * 0.8f) / speed;
                var s = _core.shape; s.angle = streamAngle * 0.4f;
            }
        }

        /// <summary>Start/stop the jet. Only acts on change, so it is free to call every frame.</summary>
        public void SetStreaming(bool on) => SetStreaming(on, force: false);

        private void SetStreaming(bool on, bool force)
        {
            if (!_built) return;
            if (!force && on == _streaming) return;
            _streaming = on;

            Toggle(_stream, on);
            Toggle(_core, on);
            Toggle(_muzzle, on);
        }

        private static void Toggle(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            var emission = ps.emission;
            emission.enabled = on;
            if (on) ps.Play();
            else ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); // in-flight droplets finish
        }

        /// <summary>
        /// Splash at a contact point. Called once per damaged body per fire tick.
        /// <paramref name="fireDir"/> is the stream's travel direction. Returns false when
        /// the impact was dropped because the frame's splash budget is spent — a wide hit
        /// across a crowd must not spike the particle count.
        /// </summary>
        public bool Splash(Vector3 point, Vector3 fireDir, float damage = 4f)
        {
            if (!_built || _splash == null) return false;
            if (_splashesThisFrame >= WaterVfxTuning.MaxSplashesPerFrame) return false;
            _splashesThisFrame++;

            int n = WaterVfxTuning.SplashDroplets(damage);
            Vector3 axis = WaterVfxTuning.SplashAxis(fireDir);
            float cos = Mathf.Cos(splashSpread * Mathf.Deg2Rad);

            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                // Random direction inside a cone around `axis` — cheap, uniform enough.
                Vector3 dir = Vector3.Slerp(axis, Random.onUnitSphere, 1f - cos).normalized;
                if (Vector3.Dot(dir, axis) < 0f) dir = -dir;

                ep.position = point;
                ep.velocity = dir * splashSpeed * Random.Range(0.55f, 1.25f);
                ep.startSize = _radius * Random.Range(0.3f, 0.62f);
                ep.startLifetime = splashLifetime * Random.Range(0.7f, 1.2f);
                ep.startColor = Color.Lerp(coreColor, waterColor, Random.value);
                _splash.Emit(ep, 1);
            }

            if (_flash != null)
            {
                var fp = new ParticleSystem.EmitParams
                {
                    applyShapeToPosition = false,
                    position = point,
                    velocity = Vector3.zero,
                    startSize = _radius * 2.6f,
                    startLifetime = 0.14f,
                    startColor = coreColor,
                };
                _flash.Emit(fp, 1);
            }

            return true;
        }

        private void LateUpdate() => _splashesThisFrame = 0;

        // --- construction ---

        /// <summary>Distance the water still has to cover once it has left the nozzle.</summary>
        private float Reach => Mathf.Max(0.1f, _range - _radius * muzzleOffset);

        private ParticleSystem BuildStream()
        {
            var ps = NewSystem("WaterStream", VfxMaterials.AlphaBlend(VfxMaterials.Droplet()), parented: true);
            float speed = Reach / Mathf.Max(0.05f, travelTime);

            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.82f, speed * 1.15f);
            // Dies at the end of the blaster's actual reach — the water must not visibly
            // outrun the volume that deals the damage, or the stream lies about its range.
            main.startLifetime = Reach / speed;
            // Mixed droplet sizes: a uniform size reads as machine-made dots, a spread reads as water.
            main.startSize = new ParticleSystem.MinMaxCurve(_radius * 0.22f, _radius * 0.55f);
            main.startColor = waterColor;
            main.maxParticles = 700;
            main.gravityModifier = 0.4f;           // a touch of droop — water, not a laser

            var emission = ps.emission;
            emission.rateOverTime = streamRate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = streamAngle;
            shape.radius = _radius * 0.18f;

            // Stretch each droplet along its own velocity. This is what turns a cloud of
            // billboards into a jet: the particles elongate into streaks in the direction of
            // travel, so the eye joins them into a continuous stream.
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 2.6f;
            r.velocityScale = 0.06f;

            // Fat and bright at the nozzle, thinning and deepening as it flies out — this is
            // what makes it read as a directional jet rather than a cloud of dots.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Ramp(coreColor, waterColor, deepColor));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0.65f, 1f, 0.75f));

            return ps;
        }

        private ParticleSystem BuildCore()
        {
            var ps = NewSystem("WaterStreamCore", VfxMaterials.Additive(VfxMaterials.Glow()), parented: true);
            float speed = Reach / Mathf.Max(0.05f, travelTime * 0.9f);

            var main = ps.main;
            main.startSpeed = speed;
            main.startLifetime = (Reach * 0.8f) / speed;   // the glow doesn't carry as far as the water
            main.startSize = new ParticleSystem.MinMaxCurve(_radius * 0.75f, _radius * 1.15f);
            main.startColor = coreColor;
            main.maxParticles = 260;

            var emission = ps.emission;
            emission.rateOverTime = 130f;   // dense enough to fuse into one bright spine

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = streamAngle * 0.4f;
            shape.radius = _radius * 0.1f;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 2.4f;
            r.velocityScale = 0.04f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(coreColor, 0.55f));

            return ps;
        }

        private ParticleSystem BuildMuzzle()
        {
            var ps = NewSystem("WaterMuzzle", VfxMaterials.Additive(VfxMaterials.Glow()), parented: true);

            var main = ps.main;
            main.startSpeed = 1.2f;
            main.startLifetime = 0.14f;
            main.startSize = _radius * 1.1f;
            main.startColor = coreColor;
            main.maxParticles = 60;

            var emission = ps.emission;
            emission.rateOverTime = 40f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _radius * 0.16f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(coreColor, 1f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(1f, 0.8f, 0.2f));

            return ps;
        }

        private ParticleSystem BuildSplash()
        {
            // Unparented + world space: splashes stay where they landed while Max keeps moving.
            var ps = NewSystem("WaterSplash", VfxMaterials.AlphaBlend(VfxMaterials.Droplet()), parented: false);

            var main = ps.main;
            main.startSpeed = 0f;              // velocity comes from EmitParams per droplet
            main.startLifetime = splashLifetime;
            main.startSize = _radius * 0.3f;
            main.startColor = waterColor;
            main.maxParticles = 300;
            main.gravityModifier = 1.6f;       // droplets arc and fall — sells the weight of water
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;        // Emit()-driven only
            emission.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(Color.white, 1f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(1f, 0.9f, 0.35f));

            ps.Play();                          // running but emitting nothing until Emit() is called
            return ps;
        }

        private ParticleSystem BuildFlash()
        {
            var ps = NewSystem("WaterImpactFlash", VfxMaterials.Additive(VfxMaterials.Glow()), parented: false);

            var main = ps.main;
            main.startSpeed = 0f;
            main.startLifetime = 0.12f;
            main.startSize = _radius * 1.6f;
            main.startColor = coreColor;
            main.maxParticles = 60;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(coreColor, 1f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0.4f, 1f, 1.25f));  // quick expanding pop

            ps.Play();
            return ps;
        }

        /// <summary>A stopped, world-simulated, material-assigned ParticleSystem. Assigning the
        /// material is not optional: AddComponent leaves the renderer with none, and a particle
        /// system with no material draws nothing.</summary>
        private ParticleSystem NewSystem(string name, Material material, bool parented)
        {
            var go = new GameObject(name);
            if (parented)
            {
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = new Vector3(0f, 0f, _radius * muzzleOffset);
                go.transform.localRotation = Quaternion.identity;
            }
            else
            {
                go.transform.position = transform.position;
            }

            var ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (material != null) r.sharedMaterial = material;

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private void OnDestroy()
        {
            // The splash systems are unparented (so splashes stay put in the world while Max
            // moves), which means they would otherwise outlive the blaster and leak.
            Dispose(_splash);
            Dispose(_flash);
        }

        private static void Dispose(ParticleSystem ps)
        {
            if (ps == null) return;
            // Destroy() is a play-mode-only call; EditMode tests tear these down too.
            if (Application.isPlaying) Destroy(ps.gameObject);
            else DestroyImmediate(ps.gameObject);
        }

        // --- curve/gradient helpers ---

        private static Gradient Ramp(Color a, Color b, Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(a, 0f),
                    new GradientColorKey(b, 0.35f),
                    new GradientColorKey(c, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(a.a, 0f),
                    new GradientAlphaKey(b.a, 0.6f),
                    new GradientAlphaKey(0f, 1f),
                });
            return g;
        }

        /// <summary>Constant colour, alpha falling from <paramref name="peak"/> to zero.</summary>
        private static Gradient Fade(Color c, float peak)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(peak, 0f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        private static AnimationCurve Curve(float start, float mid, float end)
        {
            return new AnimationCurve(
                new Keyframe(0f, start),
                new Keyframe(0.4f, mid),
                new Keyframe(1f, end));
        }
    }
}
