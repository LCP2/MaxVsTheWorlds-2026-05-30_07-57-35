using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// World 2's light fittings (MV-787, "Stormdrain Surface Kit" lighting pass — approved by Lee
    /// 2026-09-12 from the rendered design review; the numbers below are that approved design, not a
    /// substitute).
    ///
    /// The rule this kit exists to keep true: the drain stays dark and cool, and every bright colour
    /// in it is a fitting that throws a pool on the floor — never a bare emissive sticker with no
    /// source. Nothing here is a real <see cref="UnityEngine.Light"/>; same PERFORMANCE CONTRACT
    /// <see cref="StormdrainKit"/> already keeps (see that class's own header) — an emissive mesh plus
    /// a flat additive quad costs nothing extra per pixel, forty real lights would not.
    /// </summary>
    public static class StormdrainLightKit
    {
        // ---------------------------------------------------------------- palette (exact, do not substitute)

        public static readonly Color Amber = new Color(1.00f, 0.62f, 0.16f);
        public static readonly Color Red = new Color(1.00f, 0.16f, 0.12f);
        public static readonly Color Cyan = new Color(0.30f, 0.86f, 1.00f);

        /// <summary>The LED panel's own authored colour list (change 2's table gives every other
        /// fitting a single hue but calls the panel's own "mixed" — this is that list). Cyan/Amber/Red
        /// are the fitting hues this ticket introduces; the last two reuse <see cref="StormdrainKit.Status"/>
        /// and <see cref="StormdrainKit.Hazard"/> — machinery already carries those two, so a panel
        /// drawing from this list never introduces a colour nothing else in the room uses.</summary>
        public static readonly Color[] LedPanelColors =
        {
            Cyan, Amber, Red, StormdrainKit.Status, StormdrainKit.Hazard,
        };

        // ---------------------------------------------------------------- dimensions (the ticket's own numbers)

        public const float LensRadius = 0.30f;
        public const float LensEmissive = 3.2f;
        public const float BloomStrength = 0.40f;
        public const float PoolRadius = 1.0f;
        public const float PoolStrength = 0.13f;
        public const float BulkheadHeight = 2.05f;
        public const float BulkheadStandoff = 0.30f;
        public const float HazardPulsePeriod = 1.4f;

        public const float MinLampSpacing = 6.0f;
        public const float MaxLampSpacing = 9.0f;

        public const float KerbStripSegmentLength = 0.5f;
        public const float KerbStripPitch = 0.55f;
        public const float KerbStripY = 0.26f;
        public const float KerbStripEmissive = 0.45f;

        public const float LedCellSize = 0.022f;
        public const float LedBrightness = 1.9f;
        public const float LedBrightCellBrightness = 3.4f;
        public const float LedPoolRadius = 1.05f;
        public const float LedPoolStrength = 0.09f;
        public const float LedBlinkPeriod = 0.9f;

        // ---------------------------------------------------------------- Change 1: the fitting

        /// <summary>
        /// A caged bulkhead lamp: a bracket arm standing <see cref="BulkheadStandoff"/> proud of the
        /// wall, a lathed back plate and rust bezel, a <see cref="LensRadius"/> sphere lens, two guard
        /// bars across it, a tight additive bloom disc, and a wide additive floor pool tinted to the
        /// lamp. <paramref name="groundAt"/> is the wall-foot attach point (y = 0); the fixture's own
        /// height is applied internally so every call site only ever hands this a floor position, the
        /// same convention <see cref="StormdrainKit.DressWallFace"/> already used for the lamp this
        /// replaces.
        ///
        /// <paramref name="pulsing"/> attaches a <see cref="LightFittingPulse"/> seeded off
        /// <paramref name="groundAt"/> — the hazard variant; the steady bulkhead never pulses.
        ///
        /// <paramref name="mountHeight"/> defaults to the ticket's own <see cref="BulkheadHeight"/>
        /// (2.05 m — right for World 2's real 3.0 m wall, MV-771) but every call site clamps it against
        /// its own <c>wallHeight</c> first (MV-765: a fixture must never mount higher than the wall it
        /// hangs on), the same proportional-safety rule <see cref="StormdrainKit.DressWallFace"/>'s old
        /// lamp already kept.
        /// </summary>
        public static GameObject BuildBulkheadLamp(Transform parent, string name, Vector3 groundAt,
                                                    Vector3 inward, Vector3 along, Color tone, bool pulsing,
                                                    float mountHeight = BulkheadHeight)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = groundAt;

            Vector3 up = Vector3.up * mountHeight;
            Quaternion faceIntoRoom = Quaternion.LookRotation(-inward, Vector3.up);
            Quaternion axisAlongInward = Quaternion.FromToRotation(Vector3.up, inward);

            var bracket = StormdrainKit.AddMeshPart(root.transform, "Bracket",
                CharacterMeshes.Beam(BulkheadStandoff, 0.035f, 0.026f),
                inward * (BulkheadStandoff * 0.5f) + up, SurfaceKind.Metal, StormdrainKit.RustDark);
            bracket.transform.localRotation = axisAlongInward;

            Vector3 fittingCenter = inward * BulkheadStandoff + up;

            var backPlate = StormdrainKit.AddMeshPart(root.transform, "Back Plate", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(LensRadius * 0.95f, 0f),
                    new Vector2(LensRadius * 0.95f, 0.03f),
                    new Vector2(LensRadius * 0.75f, 0.055f),
                }, 16), fittingCenter, SurfaceKind.Metal, StormdrainKit.Soffit);
            backPlate.transform.localRotation = axisAlongInward;

            var bezel = StormdrainKit.AddMeshPart(root.transform, "Bezel", CharacterMeshes.Lathe(new[]
                {
                    new Vector2(LensRadius * 0.98f, 0f),
                    new Vector2(LensRadius * 1.16f, 0f),
                    new Vector2(LensRadius * 1.16f, 0.04f),
                    new Vector2(LensRadius * 1.02f, 0.07f),
                }, 16), fittingCenter + inward * 0.03f, SurfaceKind.Metal, StormdrainKit.Rust);
            bezel.transform.localRotation = axisAlongInward;

            Color lensTone = tone * LensEmissive;
            var lens = AddEmissiveMeshPart(root.transform, "Lens", CharacterMeshes.Sphere(16),
                fittingCenter + inward * 0.05f, lensTone);
            lens.transform.localScale = Vector3.one * (LensRadius * 2f);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var guard = StormdrainKit.AddMeshPart(root.transform, $"Guard Bar{i}",
                    CharacterMeshes.Beam(LensRadius * 2.1f, 0.018f, 0.018f),
                    fittingCenter + inward * 0.09f + Vector3.up * (side * LensRadius * 0.55f),
                    SurfaceKind.Metal, StormdrainKit.RustDark);
                guard.transform.localRotation = Quaternion.FromToRotation(Vector3.up, along);
            }

            AddAdditiveQuad(root.transform, "Bloom", fittingCenter + inward * 0.10f,
                new Vector3(LensRadius * 2.6f, LensRadius * 2.6f, 1f), faceIntoRoom, tone * BloomStrength);

            AddAdditiveQuad(root.transform, "Pool", inward * (PoolRadius * 0.55f) + Vector3.up * 0.012f,
                new Vector3(PoolRadius * 2f, PoolRadius * 2f, 1f), Quaternion.Euler(90f, 0f, 0f), tone * PoolStrength);

            if (pulsing)
            {
                var pulse = root.AddComponent<LightFittingPulse>();
                pulse.Configure(groundAt, HazardPulsePeriod, lens.GetComponent<Renderer>(), lensTone);
            }

            return root;
        }

        // ---------------------------------------------------------------- Change 2: kerb strip

        /// <summary>Continuous cyan guide-rail segments along one wall face's kerb — 50 cm segments at
        /// 55 cm pitch, y = <see cref="KerbStripY"/>, steady at <see cref="KerbStripEmissive"/>. Never
        /// paired with a pool: the ticket's own words call this "a guide rail, not a feature".</summary>
        public static void BuildKerbStrip(Transform parent, Vector2 a, Vector2 b, Vector3 inward)
        {
            float length = (b - a).magnitude;
            if (length < KerbStripSegmentLength) return;

            Quaternion faceIn = Quaternion.LookRotation(-inward, Vector3.up);
            Color tone = Cyan * KerbStripEmissive;

            // Named "Guide Rail", not "Kerb ..." — StormdrainDressing.Dress counts wall children by a
            // "Kerb" name prefix (one kerb box per face); a name starting with "Kerb" here would double
            // that count for every face that also gets a strip.
            var host = new GameObject("Guide Rail").transform;
            host.SetParent(parent, false);

            int segments = Mathf.Max(1, Mathf.FloorToInt(length / KerbStripPitch));
            for (int i = 0; i < segments; i++)
            {
                float t = (i + 0.5f) / segments;
                Vector2 at2 = Vector2.Lerp(a, b, t);
                Vector3 localPos = new Vector3(at2.x, KerbStripY, at2.y) + inward * 0.02f;

                var seg = GameObject.CreatePrimitive(PrimitiveType.Quad);
                seg.name = $"Segment{i}";
                seg.transform.SetParent(host, false);
                seg.transform.localPosition = localPos;
                seg.transform.localRotation = faceIn;
                seg.transform.localScale = new Vector3(KerbStripSegmentLength, 0.05f, 1f);
                StormdrainKit.Strip(seg);
                var rend = seg.GetComponent<Renderer>();
                if (rend != null) rend.sharedMaterial = StormdrainKit.Unlit(tone, "KerbStrip");
            }
        }

        // ---------------------------------------------------------------- LED panel

        /// <summary>A <c>Prism(6)</c> dark plate carrying a 5x3 grid of lathed cells, coloured in order
        /// off <see cref="LedPanelColors"/>, brightness <see cref="LedBrightness"/> with every fourth
        /// cell at <see cref="LedBrightCellBrightness"/>, two cells blinking out of phase, plus a floor
        /// pool. Always faces local -Z — every call site (machinery cover, which never yaws — see
        /// <see cref="StormdrainKit.BuildPumpHousing"/>'s own "stays square") mounts it on a fixed
        /// face, so there is no live "which way is out" to carry as a parameter.</summary>
        public static GameObject BuildLedPanel(Transform parent, Vector3 localPos,
                                               float panelWidth = 0.20f, float panelHeight = 0.14f)
        {
            var root = new GameObject("LED Panel");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPos;

            float plateRadius = Mathf.Max(panelWidth, panelHeight) * 0.62f;
            var plate = StormdrainKit.AddMeshPart(root.transform, "Plate",
                CharacterMeshes.Prism(6, plateRadius, plateRadius, 0.025f), Vector3.zero,
                SurfaceKind.Metal, StormdrainKit.Soffit);
            plate.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.back);

            const int cols = 5, rows = 3;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    int index = row * cols + col;
                    Color tone = LedPanelColors[index % LedPanelColors.Length];
                    float brightness = (index % 4 == 3) ? LedBrightCellBrightness : LedBrightness;
                    Vector3 cellLocal = new Vector3(
                        (col + 0.5f) / cols * panelWidth - panelWidth * 0.5f,
                        (row + 0.5f) / rows * panelHeight - panelHeight * 0.5f,
                        -0.014f);

                    Color cellTone = tone * brightness;
                    var cell = AddEmissiveMeshPart(root.transform, $"Cell{index}", CharacterMeshes.Lathe(new[]
                        {
                            new Vector2(0f, 0f),
                            new Vector2(LedCellSize * 0.5f, 0f),
                            new Vector2(LedCellSize * 0.5f, 0.008f),
                        }, 8), cellLocal, cellTone);
                    cell.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.back);

                    // Two cells blink out of phase (change 2): phase comes from each cell's own world
                    // position (LightFittingPulse.PhaseFor), so the two picked cells land out of step
                    // for free rather than needing a hand-picked offset.
                    if (index == 2 || index == 9)
                    {
                        var pulse = cell.AddComponent<LightFittingPulse>();
                        pulse.Configure(cell.transform.position, LedBlinkPeriod, cell.GetComponent<Renderer>(), cellTone);
                    }
                }
            }

            // A generated disc, not AddAdditiveQuad's primitive Quad (MV-786's own AC1: zero renderers
            // under a built cover piece may wear a primitive mesh, and a machinery LED panel is built
            // as a child of a Pump Housing, which lives under the Cover host that rule scans).
            AddAdditiveDisc(root.transform, "Pool", new Vector3(0f, -localPos.y + 0.012f, -0.30f),
                LedPoolRadius, Vector3.up, Cyan * LedPoolStrength);

            return root;
        }

        // ---------------------------------------------------------------- additive material

        private static readonly Dictionary<Color, Material> _additive = new();

        /// <summary>A genuinely additive-blended unlit material (not <see cref="StormdrainKit.Unlit"/>,
        /// which is opaque) — same URP Unlit-shader blend setup <c>VfxMaterials.Get</c> uses on the
        /// particle shader (<c>_Surface</c>=1, <c>_Blend</c>=2/additive, <c>_ZWrite</c>=0), duplicated
        /// here rather than referenced because <c>MaxWorlds.Rendering</c> cannot depend on
        /// <c>MaxWorlds.VFX</c> (Gameplay assembly) — see <see cref="SludgeFlowRig"/>'s own header for
        /// the same assembly-boundary note.</summary>
        public static Material AdditiveUnlit(Color tone, string name)
        {
            if (_additive.TryGetValue(tone, out Material cached) && cached != null) return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null || !shader.isSupported) return StormdrainKit.Unlit(tone, name);

            var mat = new Material(shader) { name = $"StormdrainAdditive_{name}", hideFlags = HideFlags.HideAndDontSave };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tone);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tone);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 2f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            _additive[tone] = mat;
            return mat;
        }

        /// <summary>A mesh part carrying an <see cref="StormdrainKit.Unlit"/> material rather than a
        /// <see cref="MaterialLibrary.Tinted"/> one — for the part of a fitting that IS the light
        /// (lens, LED cell). <see cref="StormdrainKit.AddMeshPart"/> always paints through the lit,
        /// shaded surface system, which is right for a bracket or a housing but wrong for a glowing
        /// lens: a lit material shades with the scene's key light instead of glowing in the dark, which
        /// is the entire point of an emissive in a world whose key is deliberately dim (same reasoning
        /// <see cref="StormdrainKit.Unlit"/>'s own doc comment gives).</summary>
        private static GameObject AddEmissiveMeshPart(Transform parent, string name, Mesh mesh, Vector3 localPos, Color tone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = StormdrainKit.Unlit(tone, name);
            return go;
        }

        private static GameObject AddAdditiveQuad(Transform parent, string name, Vector3 localPos,
                                                   Vector3 size, Quaternion rot, Color tone)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            StormdrainKit.Strip(go);
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = AdditiveUnlit(tone, name);
            return go;
        }

        /// <summary>A flat additive disc, generated via <see cref="CharacterMeshes.Lathe"/> (a two-point
        /// profile — a point at the centre, the rim at <paramref name="radius"/> — caps into a single
        /// filled disc lying in local XZ) rather than a primitive Quad. For a pool that has to sit under
        /// a Cover-dressed piece, where <see cref="AddAdditiveQuad"/>'s primitive mesh would trip
        /// MV-786's own "no primitive meshes under a built cover piece" rule.</summary>
        private static GameObject AddAdditiveDisc(Transform parent, string name, Vector3 localPos,
                                                   float radius, Vector3 normal, Color tone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, normal);
            go.AddComponent<MeshFilter>().sharedMesh = CharacterMeshes.Lathe(
                new[] { new Vector2(0f, 0f), new Vector2(radius, 0f) }, 20);
            var rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = AdditiveUnlit(tone, name);
            return go;
        }

        /// <summary>Clears the cached additive materials. Called from <see cref="StormdrainKit.Clear"/>
        /// so a test that switches worlds does not inherit the previous one's instances.</summary>
        public static void ClearCache()
        {
            foreach (var m in _additive.Values)
                if (m != null) { if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m); }
            _additive.Clear();
        }
    }

    /// <summary>
    /// Hand-rolled per-frame pulse/blink timer for a light fitting (MV-787) — the same
    /// "<c>MaxWorlds.Rendering</c> cannot reference <c>AnimSequence</c>" constraint
    /// <see cref="SludgeFlowRig"/> already documents, so this ticks its own clock instead rather than
    /// using <see cref="MaxWorlds.Feel.AnimSequence"/> (Gameplay assembly).
    ///
    /// Phase is a pure function of the fitting's own world position (<see cref="PhaseFor"/>) — never
    /// <see cref="UnityEngine.Random"/> — so two fittings at different positions land at different
    /// phases and the same fitting rebuilt at the same position always phases identically (the
    /// ticket's own determinism rule, change 3).
    /// </summary>
    public sealed class LightFittingPulse : MonoBehaviour
    {
        private Renderer _renderer;
        private Color _baseTone;
        private float _period;
        private float _time;
        private MaterialPropertyBlock _mpb;

        public float Phase { get; private set; }

        public void Configure(Vector3 worldPos, float period, Renderer renderer, Color baseTone)
        {
            Phase = PhaseFor(worldPos);
            _period = Mathf.Max(0.01f, period);
            _renderer = renderer;
            _baseTone = baseTone;
            _mpb = new MaterialPropertyBlock();
        }

        public static float PhaseFor(Vector3 worldPos)
        {
            float v = worldPos.x * 0.7548776662f + worldPos.z * 0.5698402909f;
            return v - Mathf.Floor(v);
        }

        public void Tick(float dt)
        {
            _time += dt;
            if (_renderer == null) return;

            float t = Mathf.Repeat(_time / _period + Phase, 1f);
            float intensity = 0.55f + 0.45f * Mathf.Sin(t * Mathf.PI * 2f);
            Color c = _baseTone * intensity;

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_Color", c);
            _renderer.SetPropertyBlock(_mpb);
        }

        private void Update() => Tick(Time.deltaTime);
    }
}
