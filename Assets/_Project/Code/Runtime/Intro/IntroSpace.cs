using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Intro
{
    /// <summary>
    /// The SPACE act of the opening cinematic (YT-156), storyboard beats 1–3:
    ///
    ///   1. Open on a starfield. A sun burns off to one side; Earth turns, small, in high orbit.
    ///   2. A comet scorches across the sky, its ember trail left behind it.
    ///   3. The comet SPLITS. Its pieces — the alien invaders (Brood-Hulk language, YT-150) — fan out
    ///      and streak down to Earth, each landing in a bright flash and leaving a hot scorch. This is
    ///      the invasion ARRIVING (Lee's fiction note, YT-154).
    ///
    /// Built once from primitives + particles; the <see cref="IntroCinematic"/> director drives it by
    /// time. Nothing here collides, casts into gameplay, or reads gameplay — it is a picture.
    /// </summary>
    public sealed class IntroSpace
    {
        public Transform Root { get; }
        public Transform Earth { get; }
        public Transform Comet { get; }
        /// <summary>The pod landing points, in world space — where the camera can look as they arrive.</summary>
        public IReadOnlyList<Vector3> LandingPoints => _landings;

        private const float EarthRadius = 46f;
        private const int PodCount = 5;

        // Comet flight, in the space set's local space: upper-left, far, down across to lower-right.
        private static readonly Vector3 CometStart = new Vector3(-170f, 95f, 60f);
        private static readonly Vector3 CometEnd = new Vector3(150f, -30f, 20f);

        private readonly Transform[] _pods = new Transform[PodCount];
        private readonly Vector3[] _landingsLocal = new Vector3[PodCount];
        private readonly Vector3[] _landings = new Vector3[PodCount];
        private readonly MeshRenderer[] _scorch = new MeshRenderer[PodCount];
        private readonly bool[] _landed = new bool[PodCount];

        private MeshRenderer _cometCore;
        private ParticleSystem _cometTrail;
        private ParticleSystem _stars;
        private VfxBurst _flash;
        private VfxBurst _podTrail;

        private float _spin;

        public IntroSpace(Transform parent, Vector3 localOrigin)
        {
            Root = IntroBuild.Pivot(parent, "IntroSpace", localOrigin);

            BuildStarfield();
            BuildSun();
            Earth = BuildEarth();
            Comet = BuildComet();
            BuildPods();

            _flash = new VfxBurst("IntroLandingFlash", VfxMaterials.Additive(VfxMaterials.Glow()),
                                  260, 0f, perFrameCap: 8, unscaledTime: true);
            _flash.GameObject.transform.SetParent(Root, worldPositionStays: false);

            _podTrail = new VfxBurst("IntroPodTrail", VfxMaterials.Additive(VfxMaterials.Glow()),
                                     360, 0f, perFrameCap: 8, stretched: true, unscaledTime: true);
            _podTrail.GameObject.transform.SetParent(Root, worldPositionStays: false);

            SetActive(false);
        }

        public void SetActive(bool on) => Root.gameObject.SetActive(on);

        // ------------------------------------------------------------------ per-frame housekeeping

        /// <summary>Turn the world, twinkle the trail budget. Called every frame the act is on screen.</summary>
        public void Frame(float dt)
        {
            _spin += dt * 3.2f;                       // a slow orbit — Earth turns beneath the arrival
            if (Earth != null) Earth.localRotation = Quaternion.Euler(-12f, _spin, 0f);
            _flash.EndFrame();
            _podTrail.EndFrame();
        }

        // ------------------------------------------------------------------ beat 2 — the comet

        /// <summary>Fly the comet across the sky, t in 0..1 over the beat. Its trail is world-space, so
        /// the ember streak is LEFT BEHIND it as it goes.</summary>
        public void SetComet(float t)
        {
            t = Mathf.Clamp01(t);
            if (_cometTrail != null)
            {
                var em = _cometTrail.emission;
                em.enabled = t > 0.02f && t < 0.98f;
            }
            if (Comet != null)
            {
                Comet.localPosition = Vector3.Lerp(CometStart, CometEnd, t);
                Comet.gameObject.SetActive(t < 0.999f);
            }
        }

        // ------------------------------------------------------------------ beat 3 — split & land

        /// <summary>
        /// The comet splits and its pieces rain down. t in 0..1 over the beat. Each pod peels off on a
        /// stagger, streaks from the split point to its landing site, and — the frame it arrives — fires
        /// a flash and leaves a hot scorch on the globe. The invaders touching down.
        /// </summary>
        public void SetSplit(float t)
        {
            t = Mathf.Clamp01(t);

            // The head is spent the instant it splits — the pods carry the light from here.
            if (_cometCore != null) IntroBuild.SetGlow(_cometCore, IntroPalette.CometCore * (1f - t));
            if (Comet != null && t > 0.02f) Comet.gameObject.SetActive(false);

            // Tightened from the original 0.55/0.09 (last pod landing at t=0.91, leaving almost no
            // hang time before the beat cuts to the dive — YT-161): every pod is down by t≈0.62 now,
            // leaving a real beat of hang time on the impacts before IntroCinematic's camera-hold ends.
            const float podDuration = 0.4f;
            const float stagger = 0.055f;

            for (int i = 0; i < PodCount; i++)
            {
                var pod = _pods[i];
                if (pod == null) continue;

                float p = Mathf.Clamp01((t - i * stagger) / podDuration);
                if (p <= 0f) { pod.gameObject.SetActive(false); continue; }

                pod.gameObject.SetActive(true);
                Vector3 from = CometEnd;
                Vector3 to = _landingsLocal[i];
                float eased = p * p;                  // accelerate in — it is FALLING
                Vector3 prev = pod.localPosition;
                pod.localPosition = Vector3.Lerp(from, to, eased);

                // A streak behind each falling pod, in the invaders' xeno-teal.
                if (p < 1f && p > 0.02f)
                {
                    Vector3 worldPos = Root.TransformPoint(pod.localPosition);
                    Vector3 vel = Root.TransformPoint(pod.localPosition) - Root.TransformPoint(prev);
                    _podTrail.Emit(worldPos, 1, vel.sqrMagnitude > 1e-5f ? vel : Vector3.down, 6f,
                                   0.1f, 0.3f, 0.9f, 1.5f, 0.18f, 0.32f,
                                   IntroPalette.XenoTeal, IntroPalette.CometEmber);
                }

                if (p >= 1f && !_landed[i]) Land(i);
            }
        }

        private void Land(int i)
        {
            _landed[i] = true;
            Vector3 world = _landings[i];

            // A bright touch-down flash — white-hot at the core, cooling to the invaders' teal. Bigger
            // and brighter than the original burst (YT-161): the framing is now a close hold on the
            // impact site, and the strike needs to read as the subject, not a faint blip on the globe.
            Vector3 outward = (world - Root.position).normalized;
            _flash.Emit(world, 40, outward, 70f, 4f, 12f, 0.7f, 1.9f, 0.45f, 0.9f,
                        Color.white, IntroPalette.XenoTeal);

            // A hot scorch left burning on the globe where it hit — enlarged (YT-161) to stay legible
            // through the beat's hang time on the close-framed impact site.
            if (_scorch[i] != null)
            {
                _scorch[i].gameObject.SetActive(true);
                IntroBuild.SetGlow(_scorch[i], IntroPalette.XenoTeal);
            }
        }

        /// <summary>Reset the split so the act can be replayed (tests, and a future YT-155 scrub).</summary>
        public void ResetSplit()
        {
            for (int i = 0; i < PodCount; i++)
            {
                _landed[i] = false;
                if (_pods[i] != null) _pods[i].gameObject.SetActive(false);
                if (_scorch[i] != null) _scorch[i].gameObject.SetActive(false);
            }
            if (_cometCore != null) IntroBuild.SetGlow(_cometCore, IntroPalette.CometCore);
        }

        // ------------------------------------------------------------------ build

        private void BuildStarfield()
        {
            var go = new GameObject("Stars");
            go.transform.SetParent(Root, worldPositionStays: false);
            _stars = go.AddComponent<ParticleSystem>();

            var main = _stars.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;   // the dome turns with the set
            main.startLifetime = Mathf.Infinity;
            main.startSpeed = 0f;
            main.maxParticles = 320;

            var emission = _stars.emission;
            emission.enabled = false;

            var r = _stars.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            // A fixed shell of stars around the origin, seated once (no emitter — a starfield does not
            // stream). Deterministic-ish spread; a couple of warm ones so it is not a flat white grid.
            const int n = 260;
            var particles = new ParticleSystem.Particle[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                float radius = Random.Range(240f, 300f);
                particles[i].position = dir * radius;
                particles[i].startSize = Random.Range(0.8f, 2.6f);
                particles[i].startLifetime = Mathf.Infinity;
                particles[i].remainingLifetime = Mathf.Infinity;
                bool warm = Random.value < 0.18f;
                Color c = warm ? IntroPalette.StarWarm : IntroPalette.Star;
                particles[i].startColor = c * Random.Range(0.5f, 1f);
            }
            _stars.SetParticles(particles, n);
            _stars.Play();   // no emission; the seeded shell just persists (infinite lifetime)
        }

        private void BuildSun()
        {
            // A far, bright glow low on one side — the key that lights the globe's near edge.
            var sun = IntroBuild.Glow(Root, "Sun", new Vector3(-190f, 70f, 210f), 26f, IntroPalette.SunGlow);
            IntroBuild.Glow(sun.transform, "SunHalo", Vector3.zero, 2.1f, IntroPalette.SunGlow * 0.4f);
        }

        private Transform BuildEarth()
        {
            var earth = IntroBuild.Pivot(Root, "Earth", Vector3.zero);

            var ocean = IntroBuild.Lit("earth_ocean", IntroPalette.Ocean);
            IntroBuild.Part(earth, "Ocean", PrimitiveType.Sphere, Vector3.zero,
                            Vector3.one * (EarthRadius * 2f), ocean, castShadows: false);

            var land = IntroBuild.Lit("earth_land", IntroPalette.Land);
            var dry = IntroBuild.Lit("earth_dry", IntroPalette.LandDry);
            var ice = IntroBuild.Lit("earth_ice", IntroPalette.Ice);

            // Continents — flattened patches laid on the near hemisphere so the camera sees land, not an
            // all-blue marble. Deterministic placement so every replay is the same globe.
            var conts = new (Vector3 dir, float w, float h, bool dryLand)[]
            {
                (new Vector3(-0.35f, 0.30f, -0.85f), 30f, 20f, false),
                (new Vector3(0.55f, 0.10f, -0.75f), 24f, 26f, true),
                (new Vector3(0.05f, -0.45f, -0.80f), 22f, 16f, false),
                (new Vector3(-0.70f, -0.20f, -0.55f), 18f, 22f, false),
                (new Vector3(0.30f, 0.60f, -0.60f), 16f, 14f, true),
            };
            foreach (var c in conts) Continent(earth, c.dir.normalized, c.w, c.h, c.dryLand ? dry : land);

            // Ice caps, top and bottom.
            Continent(earth, Vector3.up, 26f, 14f, ice);
            Continent(earth, Vector3.down, 24f, 12f, ice);

            // A faint cool haze of atmosphere.
            IntroBuild.Glow(earth, "Atmosphere", Vector3.zero, EarthRadius * 2.08f,
                            IntroPalette.Atmosphere * 0.16f);

            return earth;
        }

        /// <summary>A land patch sitting proud of the ocean, laid flat against the surface at a direction.
        /// Its thin axis (local Y) is aligned to the surface normal, so it hugs the globe. FromToRotation
        /// handles the poles cleanly, where a LookRotation up-vector would be degenerate.</summary>
        private static void Continent(Transform earth, Vector3 dir, float w, float h, Material mat)
        {
            Vector3 at = dir * (EarthRadius * 1.004f);
            var rot = Quaternion.FromToRotation(Vector3.up, dir);
            IntroBuild.Part(earth, "Continent", PrimitiveType.Sphere, at,
                            new Vector3(w, 3f, h), mat, rot, castShadows: false);
        }

        private Transform BuildComet()
        {
            var comet = IntroBuild.Pivot(Root, "Comet", CometStart);

            _cometCore = IntroBuild.Glow(comet, "CometCore", Vector3.zero, 5.5f, IntroPalette.CometCore);
            IntroBuild.Glow(comet, "CometHalo", Vector3.zero, 11f, IntroPalette.CometTrail * 0.45f);
            // A small solid rock inside the glow, so it is a body and not only a light.
            IntroBuild.Part(comet, "CometRock", PrimitiveType.Sphere, Vector3.zero,
                            Vector3.one * 3.4f, IntroBuild.Lit("comet_rock", IntroPalette.ChitinPlate),
                            castShadows: false);

            var trailGo = new GameObject("CometTrail");
            trailGo.transform.SetParent(comet, worldPositionStays: false);
            _cometTrail = trailGo.AddComponent<ParticleSystem>();

            var main = _cometTrail.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;   // the streak is left behind
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7f);
            main.maxParticles = 400;
            main.useUnscaledTime = true;

            var emission = _cometTrail.emission;
            emission.enabled = false;
            emission.rateOverTime = 120f;

            var col = _cometTrail.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(IntroPalette.CometCore, 0f),
                    new GradientColorKey(IntroPalette.CometTrail, 0.4f),
                    new GradientColorKey(IntroPalette.CometEmber, 1f),
                },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var r = _cometTrail.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            _cometTrail.Play();

            comet.gameObject.SetActive(false);
            return comet;
        }

        private void BuildPods()
        {
            var chitin = IntroBuild.Lit("pod_chitin", IntroPalette.Chitin);
            var plate = IntroBuild.Lit("pod_plate", IntroPalette.ChitinPlate);

            for (int i = 0; i < PodCount; i++)
            {
                // Landing sites spread across the near hemisphere, so the flashes pepper the globe.
                float a = (i / (float)(PodCount - 1)) * 2.1f - 1.05f;   // -1.05 .. 1.05
                Vector3 dir = new Vector3(Mathf.Sin(a) * 0.9f, Mathf.Cos(a * 1.3f) * 0.5f - 0.1f, -0.9f)
                              .normalized;
                _landingsLocal[i] = dir * (EarthRadius * 1.01f);
                _landings[i] = Root.TransformPoint(_landingsLocal[i]);

                var pod = IntroBuild.Pivot(Root, $"Pod{i}", CometEnd);
                // A little chitin lump — the Brood-Hulk in miniature: a dark shell and one hot eye.
                IntroBuild.Part(pod, "PodShell", PrimitiveType.Sphere, Vector3.zero,
                                new Vector3(2.4f, 1.7f, 2.9f), chitin, castShadows: false);
                IntroBuild.Part(pod, "PodPlate", PrimitiveType.Cube, new Vector3(0f, 0.5f, -0.3f),
                                new Vector3(2.2f, 0.5f, 1.6f), plate, castShadows: false);
                IntroBuild.Glow(pod, "PodEye", new Vector3(0f, 0.2f, 1.4f), 1.1f, IntroPalette.XenoTeal);
                pod.gameObject.SetActive(false);
                _pods[i] = pod;

                // The scorch it leaves — a hot teal ember on the globe, lit only once it lands. Parented
                // to Root (not the spinning Earth) so it stays on the pod's fixed landing target.
                _scorch[i] = IntroBuild.Glow(Root, "Scorch",
                                             _landingsLocal[i], 4.0f, IntroPalette.XenoTeal, flatten: 0.35f);
                _scorch[i].transform.rotation = Quaternion.LookRotation(-_landingsLocal[i].normalized);
                _scorch[i].gameObject.SetActive(false);
            }
        }
    }
}
