using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Water Balloon's landing splash (v0.5 recut spec §6a, WV-241): a satisfying water burst plus
    /// an expanding ground ring, both sized to the ability's real splash radius
    /// (<see cref="MaxWorlds.Weapons.AbilityTuning.WaterBalloonSplashRadius"/>) — spec: "an area ≈ 2×
    /// the large robot's footprint".
    ///
    /// Owned entirely by the art stream, same rule as <see cref="WaterVfx"/>: it carries no gameplay
    /// state and makes no gameplay decision. Splash damage and the robot-stopping effect are WV-231's;
    /// this only answers "what does the impact look like". Whoever wires the actual throw (WV-240) calls
    /// <see cref="Play"/> with the landing point once the balloon lands.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterBalloonSplashVfx : MonoBehaviour
    {
        [Header("Palette")]
        [SerializeField] private Color coreColor = new Color(0.85f, 0.97f, 1f, 1f);
        [SerializeField] private Color waterColor = new Color(0.31f, 0.76f, 0.97f, 1f);

        [Header("Burst")]
        [Tooltip("Droplets thrown per landing. A splash reads as a burst, not a puff, only once " +
                 "there are enough droplets to fill the whole ring.")]
        [SerializeField] private int dropletCount = 36;
        [SerializeField] private float dropletLifetime = 0.55f;

        [Tooltip("Seconds the ground ring takes to grow to the splash's full radius and fade — the " +
                 "VISUAL life of the telegraph, not the gameplay stop-duration (WV-231's).")]
        [SerializeField] private float ringLifetime = 0.35f;

        private ParticleSystem _burst;   // scattering droplets — alpha-blended, arcs and falls
        private ParticleSystem _flash;   // bright additive pop at the impact centre
        private GroundRing _ring;        // the splash's true extent, growing out from the impact point

        private float _radius = 1f;
        private bool _built;
        private float _ringTimer = -1f;
        private Vector3 _ringOrigin;

        /// <summary>Build the burst for a splash of this radius, world metres. Safe to call again —
        /// later calls just resize, matching a level-up (WV-241's own art has no distance-driven size,
        /// but the splash radius is itself a tunable, so a live edit should be able to re-fit).</summary>
        public void Init(float radius)
        {
            _radius = Mathf.Max(0.05f, radius);
            if (_built) return;
            _built = true;

            _burst = BuildBurst();
            _flash = BuildFlash();
            _ring = GroundRing.Create("SplashRing");
            _ring.transform.SetParent(transform, worldPositionStays: true);
        }

        /// <summary>Play the burst at a world point. Cosmetic only — call it once per landing.</summary>
        public void Play(Vector3 point)
        {
            if (!_built) Init(_radius);

            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < dropletCount; i++)
            {
                Vector3 dir = RadialDirection(i, dropletCount);
                ep.position = point;
                ep.velocity = dir * Random.Range(_radius * 3f, _radius * 5f) + Vector3.up * Random.Range(1.5f, 3f);
                ep.startSize = Random.Range(_radius * 0.08f, _radius * 0.18f);
                ep.startLifetime = dropletLifetime * Random.Range(0.7f, 1.2f);
                ep.startColor = Color.Lerp(coreColor, waterColor, Random.value);
                _burst.Emit(ep, 1);
            }

            if (_flash != null)
            {
                var fp = new ParticleSystem.EmitParams
                {
                    applyShapeToPosition = false,
                    position = point,
                    velocity = Vector3.zero,
                    startSize = _radius * 1.2f,
                    startLifetime = 0.18f,
                    startColor = coreColor,
                };
                _flash.Emit(fp, 1);
            }

            if (_ring != null)
            {
                _ringOrigin = point;
                _ringTimer = 0f;
                _ring.Show(point, 0.05f, RingColor(0f));
            }
        }

        /// <summary>Radius the splash's ground ring is currently drawn at — 0 when idle.</summary>
        public float CurrentRingRadius => _ring != null && _ring.Visible
            ? Mathf.Lerp(0.05f, _radius, Mathf.Clamp01(_ringTimer / ringLifetime)) : 0f;

        private void Update()
        {
            if (_ringTimer < 0f || _ring == null) return;
            _ringTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_ringTimer / ringLifetime);
            _ring.Show(_ringOrigin, Mathf.Lerp(0.05f, _radius, t), RingColor(t));
            if (t >= 1f) { _ring.Hide(); _ringTimer = -1f; }
        }

        private Color RingColor(float t) => new Color(waterColor.r, waterColor.g, waterColor.b, 0.8f * (1f - t));

        /// <summary>Even coverage around the full circle, not a directional cone — a splash lands in
        /// every direction from the impact point, unlike the blaster's stream splash (<see cref="WaterVfx.Splash"/>),
        /// which scatters around the fire direction.</summary>
        private Vector3 RadialDirection(int i, int count)
        {
            float angle = (float)i / count * 360f + Random.Range(-10f, 10f);
            float rad = angle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }

        // --- construction ---

        private ParticleSystem BuildBurst()
        {
            var ps = NewSystem("WaterBalloonBurst", VfxMaterials.AlphaBlend(VfxMaterials.Droplet()));

            var main = ps.main;
            main.startSpeed = 0f;              // velocity comes from EmitParams per droplet
            main.startLifetime = dropletLifetime;
            main.startSize = _radius * 0.12f;
            main.startColor = waterColor;
            main.maxParticles = 220;
            main.gravityModifier = 2.2f;       // a heavier arc than the blaster's fine spray — these are chunks of a burst balloon, not a mist
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(Color.white, 1f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(1f, 0.85f, 0.3f));

            ps.Play();
            return ps;
        }

        private ParticleSystem BuildFlash()
        {
            var ps = NewSystem("WaterBalloonFlash", VfxMaterials.Additive(VfxMaterials.Glow()));

            var main = ps.main;
            main.startSpeed = 0f;
            main.startLifetime = 0.18f;
            main.startSize = _radius * 1.2f;
            main.startColor = coreColor;
            main.maxParticles = 20;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.enabled = false;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Fade(coreColor, 1f));

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve(0.4f, 1f, 1.3f));   // quick expanding pop

            ps.Play();
            return ps;
        }

        /// <summary>A stopped, world-simulated, unparented, material-assigned ParticleSystem — unparented
        /// so a splash stays exactly where it landed while Max keeps moving, same reasoning as
        /// <see cref="WaterVfx"/>'s own splash system.</summary>
        private ParticleSystem NewSystem(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.position = transform.position;

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
            Dispose(_burst);
            Dispose(_flash);
        }

        private static void Dispose(ParticleSystem ps)
        {
            if (ps == null) return;
            if (Application.isPlaying) Destroy(ps.gameObject);
            else DestroyImmediate(ps.gameObject);
        }

        // --- curve/gradient helpers (same shapes WaterVfx uses) ---

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
