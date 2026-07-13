using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// Applies the Backyard's lighting rig and post-processing stack (YT-49).
    ///
    /// Everything is built in code — the lights, the ambient gradient, and the URP
    /// VolumeProfile with its overrides — so a fresh clone renders identically in CI with
    /// no authored asset and no hand-wiring (docs/CODE_DRIVEN_SCENES.md). It installs itself
    /// after scene load, so the scene file is untouched.
    ///
    /// It only touches rendering: lights, ambient, fog, the volume, and the camera's
    /// post-processing flag. It changes no gameplay and moves no camera — the fixed ~72°
    /// angle set by the Cinemachine rig (YT-33) is deliberately left alone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardLighting : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BackyardLighting>() != null) return;
            new GameObject("BackyardLighting").AddComponent<BackyardLighting>();
        }

        [SerializeField] private BackyardLook look = BackyardLook.Default;

        /// <summary>The hand-written sky shader (YT-76). Nothing but Shader.Find references it, so
        /// it has to be in Always Included Shaders or the build strips it and the yard goes back to
        /// rendering against a grey void — in the build only, which is the worst place to find out.
        /// See <c>Editor/VfxShaderInclude.cs</c>.</summary>
        public const string SkyShaderName = "MaxWorlds/StylizedSky";

        private VolumeProfile _profile;
        private Material _sky;

        private void Awake() => Apply(look);

        /// <summary>Build the whole look. Idempotent — safe to call again after a tweak.</summary>
        public void Apply(BackyardLook l)
        {
            look = l;
            ApplyLights(l);
            ApplySky(l);
            ApplyAmbient(l);
            ApplyVolume(l);
            EnablePostProcessingOnCamera();
        }

        /// <summary>The profile this built, so tests (and a future tuning UI) can inspect it.</summary>
        public VolumeProfile Profile => _profile;

        /// <summary>The sky material this built, so tests can look at it without a camera.</summary>
        public Material Sky => _sky;

        // --- lights ---

        private void ApplyLights(BackyardLook l)
        {
            // Re-use the scene's existing directional light as the key rather than adding a
            // second sun beside it — the scaffolded scene already ships one.
            Light key = FindExistingSun();
            if (key == null) key = NewLight("Sun (Key)");

            key.type = LightType.Directional;
            key.color = l.KeyColor;
            key.intensity = l.KeyIntensity;
            key.transform.rotation = Quaternion.Euler(l.KeyEuler);
            key.shadows = LightShadows.Soft;
            key.shadowStrength = l.ShadowStrength;
            RenderSettings.sun = key;

            var fill = NewLight("Sky Fill");
            fill.type = LightType.Directional;
            fill.color = l.FillColor;
            fill.intensity = l.FillIntensity;
            fill.transform.rotation = Quaternion.Euler(l.FillEuler);
            fill.shadows = LightShadows.None;   // a second shadow-caster doubles the cost and muddies the first

            var rim = NewLight("Rim");
            rim.type = LightType.Directional;
            rim.color = l.RimColor;
            rim.intensity = l.RimIntensity;
            rim.transform.rotation = Quaternion.Euler(l.RimEuler);
            rim.shadows = LightShadows.None;
        }

        private static Light FindExistingSun()
        {
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                // Skip the ones we own, so a re-Apply doesn't promote the fill to the key.
                if (light.type != LightType.Directional) continue;
                if (light.gameObject.name == "Sky Fill" || light.gameObject.name == "Rim") continue;
                return light;
            }
            return null;
        }

        private Light NewLight(string name)
        {
            var existing = transform.Find(name);
            if (existing != null) return existing.GetComponent<Light>();

            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            return go.AddComponent<Light>();
        }

        // --- sky ---

        /// <summary>
        /// The sky (YT-76). Without one, everything the yard's geometry doesn't cover renders as
        /// Unity's default flat grey — and at a camera that looks 72° DOWN, what isn't covered is
        /// the ground past the edge of the world. That grey is why the arena reads as a slab
        /// floating in a void, and it is the single most "unfinished" thing left in the frame.
        ///
        /// Built in code from the look, like everything else here — so it's a tunable, not an asset
        /// somebody has to author and remember to assign to the scene.
        /// </summary>
        private void ApplySky(BackyardLook l)
        {
            var shader = Shader.Find(SkyShaderName);
            if (shader == null || !shader.isSupported)
            {
                // A missing sky is a grey background — ugly, not broken. Say so and carry on; the
                // one thing we never do is leave a magenta dome over the game (YT-58).
                Debug.LogWarning($"[BackyardLighting] '{SkyShaderName}' unavailable; the yard keeps " +
                                 "the default background.");
                return;
            }

            if (_sky == null)
            {
                _sky = new Material(shader) { name = "BackyardSky", hideFlags = HideFlags.HideAndDontSave };
            }

            _sky.SetColor("_Zenith", l.SkyZenith);
            _sky.SetColor("_Horizon", l.SkyHorizon);
            _sky.SetColor("_GroundHaze", l.SkyGroundHaze);
            _sky.SetColor("_SunColor", l.SkySun);
            _sky.SetColor("_CloudColor", l.SkyCloud);
            _sky.SetFloat("_SunGlow", l.SkySunGlow);
            _sky.SetFloat("_SunIntensity", l.SkySunIntensity);
            _sky.SetFloat("_CloudAmount", l.SkyCloudAmount);
            _sky.SetFloat("_CloudScale", l.SkyCloudScale);

            // The sun in the sky has to be the sun in the scene, or the shadows point one way and
            // the glare comes from the other — the cheapest possible way to look fake.
            Vector3 toSun = -(Quaternion.Euler(l.KeyEuler) * Vector3.forward);
            _sky.SetVector("_SunDir", toSun.normalized);

            RenderSettings.skybox = _sky;
        }

        // --- ambient ---

        private static void ApplyAmbient(BackyardLook l)
        {
            // A three-band gradient, not a flat colour: warm sky above, grass bouncing up from
            // below. This is most of what stops the greybox reading as dead flat grey.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = l.AmbientSky;
            RenderSettings.ambientEquatorColor = l.AmbientEquator;
            RenderSettings.ambientGroundColor = l.AmbientGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = l.FogColor;
            RenderSettings.fogDensity = l.FogDensity;
        }

        // --- post-processing ---

        private void ApplyVolume(BackyardLook l)
        {
            var volume = GetComponent<Volume>();
            if (volume == null) volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;

            // Built in memory, never saved as an asset — hence HideAndDontSave.
            if (_profile == null)
            {
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.name = "BackyardProfile";
                _profile.hideFlags = HideFlags.HideAndDontSave;
            }
            volume.sharedProfile = _profile;

            var tone = Get<Tonemapping>();
            tone.mode.Override(TonemappingMode.Neutral);   // filmic rolloff without ACES' highlight desaturation

            var grade = Get<ColorAdjustments>();
            grade.postExposure.Override(l.PostExposure);
            grade.contrast.Override(l.Contrast);
            grade.saturation.Override(l.Saturation);
            grade.colorFilter.Override(l.ColorFilter);

            // The split-tone is what sells "Hades-adjacent": cool shadows against warm light.
            var smh = Get<ShadowsMidtonesHighlights>();
            smh.shadows.Override(ToTrackball(l.ShadowTint));
            smh.highlights.Override(ToTrackball(l.HighlightTint));

            var bloom = Get<Bloom>();
            bloom.threshold.Override(l.BloomThreshold);
            bloom.intensity.Override(l.BloomIntensity);
            bloom.scatter.Override(l.BloomScatter);
            bloom.tint.Override(l.BloomTint);
            bloom.highQualityFiltering.Override(false);   // the cheap path — this ships to WebGL/mobile

            var vig = Get<Vignette>();
            vig.intensity.Override(l.VignetteIntensity);
            vig.smoothness.Override(l.VignetteSmoothness);

            var grain = Get<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(l.FilmGrain);
        }

        private T Get<T>() where T : VolumeComponent
        {
            if (_profile.TryGet<T>(out var existing)) return existing;
            return _profile.Add<T>(overrides: false);
        }

        /// <summary>Trackball params are (r, g, b, offset) with 1 = neutral, so a tint has to be
        /// expressed relative to white rather than as a raw colour.</summary>
        private static Vector4 ToTrackball(Color tint)
        {
            float peak = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
            if (peak <= 0f) return new Vector4(1f, 1f, 1f, 0f);
            return new Vector4(tint.r / peak, tint.g / peak, tint.b / peak, 0f);
        }

        // --- camera ---

        private bool _postEnabled;

        /// <summary>
        /// Keep trying until the camera exists.
        ///
        /// The original version gave up silently if Camera.main was null at Awake — and the camera
        /// is built by the Cinemachine rig, so "not there yet" is a real possibility. One missed
        /// frame and post-processing stays off for the entire session, with nothing logged. That is
        /// exactly the kind of silent failure that made YT-49 look fine in the editor and do nothing
        /// on the web build, so it now retries and says so out loud.
        /// </summary>
        private void Update()
        {
            if (_postEnabled) return;
            EnablePostProcessingOnCamera();
        }

        private void EnablePostProcessingOnCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            // Without this the whole volume stack is built and then ignored: URP only runs
            // post-processing on cameras that opt in.
            var data = cam.GetUniversalAdditionalCameraData();
            if (data == null) return;
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            _postEnabled = true;

            // Logged so the truth is readable from a browser console on the deployed build. The
            // whole YT-49 pass can be silently inert on WebGL, and "it looks right in the editor"
            // is not evidence.
            var urp = UniversalRenderPipeline.asset;
            Debug.Log($"[BackyardLighting] post-processing ON for camera '{cam.name}' — " +
                      $"volume overrides={(_profile != null ? _profile.components.Count : 0)}, " +
                      $"renderScale={(urp != null ? urp.renderScale : -1f)}, " +
                      $"upscaler={(urp != null ? urp.upscalingFilter.ToString() : "?")}");
        }

        private void OnDestroy()
        {
            if (_sky != null)
            {
                if (RenderSettings.skybox == _sky) RenderSettings.skybox = null;
                if (Application.isPlaying) Destroy(_sky);
                else DestroyImmediate(_sky);
            }

            if (_profile == null) return;
            if (Application.isPlaying) Destroy(_profile);
            else DestroyImmediate(_profile);
        }
    }
}
