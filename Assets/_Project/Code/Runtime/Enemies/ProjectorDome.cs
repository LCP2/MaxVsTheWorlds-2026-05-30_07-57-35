using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-1284 point 5: a Shield Projector's own protective dome visual — a translucent white-blue
    /// hemisphere with a brighter rim, plus a thin beam from the Projector's own dish to the dome's
    /// apex. Built the same "ground a full sphere so only the upper half ever reads" shortcut
    /// <see cref="MaxWorlds.Weapons.ForceFieldBubble"/>'s own energy-shield dome already uses — reusing
    /// its <c>MaxWorlds/ForceFieldShield</c> shader (Fresnel rim, falls back to a flat alpha-blended
    /// fill if the shader is unavailable) rather than authoring a second one, tinted white-blue via a
    /// <see cref="MaterialPropertyBlock"/> instead of that bubble's own blue-to-amber decay colours.
    ///
    /// Owned and repositioned every recompute by the <see cref="RobotEnemy"/> that projects it (see
    /// RobotEnemy's own TickProjectorDome) — NOT parented to it, since the dome's own centre is
    /// the pack's centroid, not the Projector's own position. Independent GameObject, same "VFX prop
    /// outlives a pooled owner's own reset" footing as <see cref="MoltenPatch"/>: <see cref="Collapse"/>
    /// detaches from its owner and keeps fading on its own for <see cref="CollapseSeconds"/> (point 4's
    /// "drops at once (0.3 s collapse VFX)") even if the Projector is pooled and re-<c>Apply</c>'d the
    /// very same frame it dies or is captured.
    /// </summary>
    [MaxWorlds.Core.PerfSection("projectordome")]
    public sealed class ProjectorDome : MonoBehaviour
    {
        /// <summary>Point 4's own "0.3 s collapse VFX".</summary>
        public const float CollapseSeconds = 0.3f;

        // Point 5's own "<=20% opacity fill" / "a brighter rim >=3 px" — translucent white-blue, a
        // noticeably brighter near-white rim, matching ForceFieldBubble's fill/rim split shape.
        // MV-1375: the dome, beam and the Projector's own body share one neon lime (#7CFF3A) so you
        // can see who makes the shield. Fill alpha unchanged from MV-1284.
        public static readonly Color Lime = new Color(0.486f, 1f, 0.227f, 1f);
        private static readonly Color FillColor = new Color(Lime.r, Lime.g, Lime.b, 0.18f);
        private static readonly Color RimColor = Lime;
        private static readonly Color BeamColor = new Color(Lime.r, Lime.g, Lime.b, 0.95f);

        /// <summary>MV-1389: the thin bright core's width, metres (was a 0.25 m flat bar).</summary>
        public const float BeamWidth = 0.07f;
        /// <summary>MV-1389: the additive glow sheath's width, metres.</summary>
        public const float GlowWidth = 0.3f;
        /// <summary>MV-1389: end flare diameter, metres (spec cap 0.4).</summary>
        public const float FlareSize = 0.35f;
        /// <summary>MV-1389: streak scroll speed along the beam, metres/second (spec >= 1.5).</summary>
        public const float FlowSpeed = 2f;
        public const int BeamPoints = 12;
        public const float WobbleAmplitude = 0.06f;
        /// <summary>MV-1375: beam pulse rate, Hz. MV-1389: applied as brightness/alpha only, never width.</summary>
        public const float BeamPulseHz = 1.5f;

        private static readonly Color CoreColor = new Color(0.847f, 1f, 0.69f, 1f); // #D8FFB0
        private static Texture2D s_streak;
        private static Texture2D s_flare;

        private LineRenderer _glow;
        private Material _coreMat;
        private Transform _flareA, _flareB;
        private Vector3 _from, _to;
        private float _flow;
        private float _pulse = 1f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

        private MeshRenderer _domeRenderer;
        private MaterialPropertyBlock _mpb;
        private LineRenderer _beam;
        private float _radius;
        private bool _collapsing;
        private float _collapseRemaining;

        public static ProjectorDome Spawn(Vector3 center, float radius, Vector3 beamOrigin)
        {
            var go = new GameObject("ProjectorDome (stand-in)");
            var dome = go.AddComponent<ProjectorDome>();
            dome._mpb = new MaterialPropertyBlock();
            dome._radius = radius;
            dome.Build(radius);
            dome.Reposition(center, beamOrigin);
            return dome;
        }

        private void Build(float radius)
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.name = "Visual";
            var col = vis.GetComponent<Collider>();
            if (col != null)
            {
                // Pure visual — never a solid; a robot standing "inside" the dome must still walk
                // through it freely (point 2 is a damage rule, not a physical wall).
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            vis.transform.SetParent(transform, worldPositionStays: false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale = Vector3.one * radius * 2f;

            // Same reason ForceFieldBubble's own visual carries this: anything driving its own
            // renderer colour through a MaterialPropertyBlock must carry SelfDrivenTint, or
            // RuntimeSurfaceDirector's sweep claims it within a frame and overwrites it with a
            // generic opaque world-prop material.
            vis.AddComponent<SelfDrivenTint>();

            _domeRenderer = vis.GetComponent<MeshRenderer>();
            _domeRenderer.sharedMaterial = DomeMaterial();
            _domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _domeRenderer.receiveShadows = false;

            var beamGo = new GameObject("Beam");
            beamGo.transform.SetParent(transform, worldPositionStays: false);
            _beam = beamGo.AddComponent<LineRenderer>();
            _beam.positionCount = BeamPoints;
            _beam.widthMultiplier = BeamWidth;
            _beam.useWorldSpace = true;
            _beam.textureMode = LineTextureMode.Tile;
            // Own material instance: the scrolling offset must not leak into shared VfxMaterials.
            _coreMat = new Material(VfxMaterials.Additive(Streak())) { name = "ProjectorBeamCore", hideFlags = HideFlags.HideAndDontSave };
            _coreMat.mainTexture = Streak();
            _beam.material = _coreMat;
            _beam.startColor = _beam.endColor = CoreColor;
            _beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beam.receiveShadows = false;

            var glowGo = new GameObject("BeamGlow");
            glowGo.transform.SetParent(transform, worldPositionStays: false);
            _glow = glowGo.AddComponent<LineRenderer>();
            _glow.positionCount = BeamPoints;
            _glow.widthMultiplier = GlowWidth;
            _glow.useWorldSpace = true;
            _glow.textureMode = LineTextureMode.Stretch;
            _glow.material = VfxMaterials.Additive(VfxMaterials.LineGlow());
            _glow.startColor = _glow.endColor = BeamColor;
            _glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _glow.receiveShadows = false;

            _flareA = MakeFlare("FlareDish");
            _flareB = MakeFlare("FlareDome");

            ApplyFraction(1f);
        }

        /// <summary>Re-centres the dome and re-aims its dish beam — called every
        /// RobotEnemy's own TickProjectorDome recompute while the dome is up. The sphere is
        /// grounded at the centre's own Y (always 0 — <see cref="RobotEnemy.DomeCenter"/> is always
        /// XZ-flattened) so only its upper half ever reads above the floor, same shortcut
        /// <see cref="MaxWorlds.Weapons.ForceFieldBubble"/>'s own dome uses rather than building a true
        /// hemisphere mesh.</summary>
        public void Reposition(Vector3 center, Vector3 beamOrigin)
        {
            if (_collapsing) return;
            transform.position = center;
            gameObject.SetActive(true);
            if (_beam == null) return;
            _from = beamOrigin;
            _to = center + Vector3.up * _radius;
            ApplyBeamShape();
        }

        private Transform MakeFlare(string n)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = n;
            var c = q.GetComponent<Collider>();
            if (c != null) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
            q.transform.SetParent(transform, worldPositionStays: false);
            q.transform.localScale = Vector3.one * FlareSize;
            var r = q.GetComponent<MeshRenderer>();
            var fm = new Material(VfxMaterials.Additive(Flare())) { name = "ProjectorFlare", hideFlags = HideFlags.HideAndDontSave };
            fm.SetColor("_BaseColor", BeamColor);
            fm.SetColor("_Color", BeamColor);
            r.sharedMaterial = fm;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            q.AddComponent<SelfDrivenTint>();
            return q.transform;
        }

        private static Texture2D Streak()
        {
            if (s_streak != null) return s_streak;
            // u along the beam: bright dashes with soft tails (reads as flow); v: constant.
            const int w = 64, h = 4;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "projbeam_streak", wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w;
                float a = Mathf.Clamp01(0.35f + 0.65f * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(Mathf.Repeat(u * 2f, 1f) - 0.25f) * 4f), 1.5f));
                for (int y = 0; y < h; y++) t.SetPixel(x, y, new Color(a, a, a, a));
            }
            t.Apply();
            return s_streak = t;
        }

        private static Texture2D Flare()
        {
            if (s_flare != null) return s_flare;
            return s_flare = VfxMaterials.Glow();
        }

        /// <summary>MV-1389: sine wobble over <see cref="BeamPoints"/> points, ends pinned; flares at both ends.</summary>
        private void ApplyBeamShape()
        {
            if (_beam == null) return;
            Vector3 d = _to - _from;
            Vector3 side = Vector3.Cross(d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward, Vector3.up);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side.Normalize();
            for (int i = 0; i < BeamPoints; i++)
            {
                float k = i / (float)(BeamPoints - 1);
                float env = Mathf.Sin(k * Mathf.PI);
                float w = Mathf.Sin(k * 6f * Mathf.PI - _flow * 3f) * WobbleAmplitude * env;
                Vector3 p = Vector3.Lerp(_from, _to, k) + side * w;
                _beam.SetPosition(i, p);
                if (_glow != null) _glow.SetPosition(i, p);
            }
            _beam.textureMode = LineTextureMode.Tile;
            _coreMat.mainTextureScale = new Vector2(1f / 0.5f, 1f);
            if (_flareA != null) _flareA.position = _from;
            if (_flareB != null) _flareB.position = _to;
            var cam = Camera.main;
            if (cam != null)
            {
                if (_flareA != null) _flareA.rotation = cam.transform.rotation;
                if (_flareB != null) _flareB.rotation = cam.transform.rotation;
            }
        }

        private void ApplyBeamColor(float fade)
        {
            if (_beam == null) return;
            Color c = CoreColor; c.a *= fade * _pulse;
            _beam.startColor = _beam.endColor = c;
            Color g = BeamColor; g.a *= fade * _pulse;
            if (_glow != null) _glow.startColor = _glow.endColor = g;
        }

        /// <summary>No robots left in range (point 1: "with no such robots, no dome") — hides the
        /// dome instantly so it can reappear the moment the pack drifts back in range, without paying
        /// for a fresh <see cref="Spawn"/>/<see cref="Collapse"/> round-trip. Distinct from
        /// <see cref="Collapse"/>: this is "temporarily nobody to protect", not "the Projector itself
        /// is gone" — no 0.3 s fade, point 4's collapse VFX is reserved for death/capture.</summary>
        public void HideImmediate()
        {
            if (!_collapsing) gameObject.SetActive(false);
        }

        /// <summary>Point 4: the Projector died or was captured — fades this dome out over
        /// <see cref="CollapseSeconds"/>, then destroys itself. Independent of its owner from this
        /// call onward (see the class doc comment for why).</summary>
        public void Collapse()
        {
            if (_collapsing) return;
            _collapsing = true;
            _collapseRemaining = CollapseSeconds;
            gameObject.SetActive(true);
        }

        /// <summary>The pooling-reset path (<see cref="RobotEnemy.Apply"/>) — a previous Projector
        /// life's dome must not leak into the next one, and this is a pooling reset, not a gameplay
        /// death, so no fade.</summary>
        public void DestroyImmediateSelf() => DestroySelf();

        private void Update()
        {
            Tick(Time.deltaTime);
            // MV-1389: 1.5 Hz brightness pulse (alpha only, width never changes).
            float k = 0.5f + 0.5f * Mathf.Sin(Time.time * BeamPulseHz * 2f * Mathf.PI);
            _pulse = Mathf.Lerp(0.7f, 1f, k);
            if (!_collapsing) ApplyBeamColor(1f);
        }

        /// <summary>Public and dt-parameterised, same "an EditMode test can drive it directly"
        /// reasoning as <see cref="MoltenPatch.Tick"/> — <c>Update()</c> never runs outside Play mode
        /// and this project authors no PlayMode tests.</summary>
        public void Tick(float dt)
        {
            if (_coreMat != null)
            {
                // Flow dish -> dome: FlowSpeed m/s; Tile mode maps 0.5 m of beam to one texture repeat.
                _flow += dt;
                _coreMat.mainTextureOffset = new Vector2(-Mathf.Repeat(_flow * FlowSpeed / 0.5f, 1f), 0f);
                ApplyBeamShape();
            }
            if (!_collapsing) return;
            _collapseRemaining -= dt;
            float t = Mathf.Clamp01(_collapseRemaining / CollapseSeconds);
            ApplyFraction(t);
            if (_collapseRemaining <= 0f) DestroySelf();
        }

        private void ApplyFraction(float t)
        {
            if (_domeRenderer == null) return;
            Color fill = FillColor; fill.a *= t;
            Color rim = RimColor; rim.a *= t;
            _mpb.SetColor(BaseColorId, fill);
            _mpb.SetColor(RimColorId, rim);
            _domeRenderer.SetPropertyBlock(_mpb);
            ApplyBeamColor(t);
        }

        private void DestroySelf()
        {
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>Same shader/fallback choice as <see cref="MaxWorlds.Weapons.ForceFieldBubble"/>'s
        /// own dome material — a real Fresnel rim where available, a flat translucent fill (a lost
        /// rim, not a magenta or opaque sphere) where it isn't.</summary>
        private static Material DomeMaterial()
        {
            var shader = Shader.Find("MaxWorlds/ForceFieldShield");
            if (shader == null || !shader.isSupported)
                return VfxMaterials.AlphaBlend(VfxMaterials.Solid());

            return new Material(shader)
            {
                name = "ProjectorDome",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
