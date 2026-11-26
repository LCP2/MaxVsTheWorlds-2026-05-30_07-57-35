using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-858 ARC's own instant connector between the hit point and the arc target. MV-868: was one
    /// faint 0.12m line lost next to the impact flash and spark burst it fires alongside -- now two
    /// <see cref="LineRenderer"/>s on the same jagged vertex path (a wide additive orange SLEEVE built
    /// first so it draws behind, and the original blue-white CORE inside it), held at full alpha then
    /// faded out so the arc leaves rather than pops, with the interior vertices re-randomised once so
    /// it crackles. Still a short-lived world-space effect, not a travelling projectile like the old
    /// FORK release -- the damage already landed the instant the arc fired
    /// (<see cref="MaxWorlds.Combat.PulseLaser.TryArc"/>), so this is pure presentation with no
    /// collision or steering of its own.
    /// </summary>
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class ArcBoltVfx : MonoBehaviour
    {
        public const int Segments = 7;
        private const float JitterMetres = 0.15f;

        // MV-868 spec: hold full alpha for the first 0.12s, then fade to 0 over the remainder of the
        // lifetime (0.08s at the default 0.20s lifetime), re-randomising the interior vertices once at
        // 0.06s so the line crackles instead of standing still.
        private const float HoldSeconds = 0.12f;
        private const float RerandomizeAtSeconds = 0.06f;

        // MV-1469: RICOCHET's chain arc: white core, #BFEFFF glow, 9 segments kinked 0.15-0.25 m, held 0.12 s.
        public const int ChainSegments = 9;
        public const float ChainMinKink = 0.15f;
        public const float ChainMaxKink = 0.25f;
        public const float ChainCoreWidth = 0.04f;
        public const float ChainGlowWidth = 0.14f;
        public const float ChainLifetime = 0.20f;

        private int _segments = Segments;
        private float _minKink;
        private float _maxKink = JitterMetres;
        private Vector3 _from;
        private Vector3 _to;
        private float _lifetime;
        private float _elapsed;
        private bool _rerandomized;

        /// <summary>The arc's far endpoint (MV-1451: a resolved value a test reads to prove where an arc went).</summary>
        public Vector3 To => _to;
        public Vector3 From => _from;

        private LineRenderer _sleeve;
        private LineRenderer _core;
        private Color _sleeveBaseColor;
        private Color _coreBaseColor;

        /// <summary>Spawns the jagged sleeve+core line pair from <paramref name="from"/> to
        /// <paramref name="to"/>, self-destructing after <paramref name="tuning"/>'s lifetime. Returns
        /// the spawned GameObject so callers (and tests) can inspect the built renderers.</summary>
        public static GameObject Show(Vector3 from, Vector3 to, CombatVfxTuning.LppeArcTuning tuning, Color coreColor)
        {
            var go = new GameObject("ArcBolt");
            var vfx = go.AddComponent<ArcBoltVfx>();
            vfx._from = from;
            vfx._to = to;
            vfx._lifetime = tuning.LineLifetime;
            vfx.Build(tuning, coreColor);
            return go;
        }

        /// <summary>MV-1469: RICOCHET chain lightning arc (white core, #BFEFFF glow) - never PulseLaser's ARC colours.</summary>
        public static GameObject ShowChain(Vector3 from, Vector3 to)
        {
            var go = new GameObject("ArcBolt");
            var vfx = go.AddComponent<ArcBoltVfx>();
            vfx._from = from;
            vfx._to = to;
            vfx._lifetime = ChainLifetime;
            vfx._segments = ChainSegments;
            vfx._minKink = ChainMinKink;
            vfx._maxKink = ChainMaxKink;
            vfx._sleeveBaseColor = new Color(JaggedBolt.GlowColor.r, JaggedBolt.GlowColor.g, JaggedBolt.GlowColor.b, JaggedBolt.GlowColor.a);
            vfx._coreBaseColor = JaggedBolt.CoreColor;
            vfx._sleeve = vfx.BuildLine("ArcSleeve", ChainGlowWidth, vfx._sleeveBaseColor);
            vfx._core = vfx.BuildLine("ArcCore", ChainCoreWidth, vfx._coreBaseColor);
            vfx.ApplyJaggedPath();
            return go;
        }

        private void Build(CombatVfxTuning.LppeArcTuning tuning, Color coreColor)
        {
            _sleeveBaseColor = new Color(tuning.SleeveColor.r, tuning.SleeveColor.g, tuning.SleeveColor.b, tuning.SleeveAlpha);
            _coreBaseColor = coreColor;

            // SLEEVE built first so it draws behind the CORE.
            _sleeve = BuildLine("ArcSleeve", tuning.SleeveWidth, _sleeveBaseColor);
            _core = BuildLine("ArcCore", tuning.CoreWidth, _coreBaseColor);

            ApplyJaggedPath();
        }

        private LineRenderer BuildLine(string name, float width, Color color)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, worldPositionStays: true);

            var line = child.AddComponent<LineRenderer>();
            line.positionCount = _segments + 1;
            line.widthMultiplier = width;
            line.useWorldSpace = true;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            line.startColor = color;
            line.endColor = color;
            return line;
        }

        /// <summary>Builds one jittered vertex path and applies it to both renderers, so the sleeve and
        /// core always trace the exact same line. Endpoints stay exact -- only the interior vertices
        /// jitter -- so the arc always reads as connecting the hit point to the arc target, never
        /// missing either one.</summary>
        private void ApplyJaggedPath()
        {
            Vector3 delta = _to - _from;
            Vector3 axis = delta.sqrMagnitude > 1e-6f ? delta.normalized : Vector3.forward;
            Vector3 perp = Vector3.Cross(axis, Vector3.up);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.right;
            perp.Normalize();

            for (int i = 0; i <= _segments; i++)
            {
                float t = (float)i / _segments;
                Vector3 point = Vector3.Lerp(_from, _to, t);
                if (i > 0 && i < _segments)
                    point += perp * (_minKink > 0f
                        ? Random.Range(_minKink, _maxKink) * (Random.value < 0.5f ? -1f : 1f)
                        : Random.Range(-_maxKink, _maxKink));
                _sleeve.SetPosition(i, point);
                _core.SetPosition(i, point);
            }
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            if (!_rerandomized && _elapsed >= RerandomizeAtSeconds)
            {
                _rerandomized = true;
                ApplyJaggedPath();
            }

            float fadeWindow = Mathf.Max(_lifetime - HoldSeconds, 0.0001f);
            float alpha = _elapsed <= HoldSeconds ? 1f : Mathf.Clamp01(1f - (_elapsed - HoldSeconds) / fadeWindow);
            SetAlpha(_sleeve, _sleeveBaseColor, alpha);
            SetAlpha(_core, _coreBaseColor, alpha);

            if (_elapsed >= _lifetime) Destroy(gameObject);
        }

        private static void SetAlpha(LineRenderer line, Color baseColor, float alpha)
        {
            Color faded = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);
            line.startColor = faded;
            line.endColor = faded;
        }
    }
}
