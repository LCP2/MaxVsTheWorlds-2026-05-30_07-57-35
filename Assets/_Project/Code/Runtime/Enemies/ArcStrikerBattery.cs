using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// ARC STRIKER's weapon (MV-1426): every 4 s, while Max is within 18 m and in sight, it places a 2.5 m
    /// blue-white circle on the floor centred on Max's position AT THAT INSTANT (it never follows him),
    /// pulses it for 1.0 s, then a lightning column hits the circle for 60 on Max or a Sentinel standing
    /// inside it. The strike comes from above, so cover never stops it.
    ///
    /// A plain class with a public <see cref="Tick"/> seam (same idiom as <see cref="OrbitalBattery"/>);
    /// <see cref="RobotEnemy"/> owns one and ticks it every awake frame. Damage is applied here; the bolt,
    /// flash and crack cue are presentation and only built in Play mode.
    /// </summary>
    public sealed class ArcStrikerBattery
    {
        public const float MarkRange = 18f;
        public const float CooldownSeconds = 4f;
        public const float MarkSeconds = 1f;
        public const float CircleDiameter = 2.5f;
        public const float Damage = 60f;

        private static readonly Color RingColor = new Color(0.55f, 0.78f, 1f);

        private float _cooldown;          // counts down from CooldownSeconds at each mark; ready at 0
        private float _markElapsed;
        private bool _marking;
        private Vector3 _center;
        private GroundRing _ring;

        /// <summary>MV-1434: where the orb is in world space (set by the rig); null falls back to a point
        /// <see cref="MaxWorlds.VFX.RobotBodies.ArcStrikerOrbHeight"/> above the striker's feet.</summary>
        public System.Func<Vector3?> OrbSource;

        /// <summary>MV-1434: the 3 mark arcs (built with the first mark, active only while marking).</summary>
        public const int ArcCount = 3;
        public const float ArcPeakHeight = 3f;
        public const float ArcReJagSeconds = 0.05f;
        public static readonly Color ArcColor = new Color(0.749f, 0.89f, 1f, 1f);   // #BFE3FF
        private LineRenderer[] _arcs;
        private GameObject _arcRoot;
        private Vector3 _feet;

        /// <summary>The mark arcs' renderers (null until the first mark) - read by tests/captures.</summary>
        public LineRenderer[] Arcs => _arcs;

        public Vector3 OrbPosition()
        {
            Vector3? o = OrbSource != null ? OrbSource() : null;
            return o ?? _feet + Vector3.up * MaxWorlds.VFX.RobotBodies.ArcStrikerOrbHeight;
        }

        /// <summary>Fill <paramref name="pts"/> with a jagged arc from <paramref name="from"/> to <paramref name="to"/>
        /// peaking <see cref="ArcPeakHeight"/> above the floor at <paramref name="to"/>. Endpoints are exact.</summary>
        public static void BuildArcPoints(Vector3[] pts, Vector3 from, Vector3 to, float jag, int seed)
        {
            var rng = new System.Random(seed);
            int n = pts.Length - 1;
            float floorY = to.y;
            float midY = (from.y + to.y) * 0.5f;
            float lift = Mathf.Max(0.5f, ArcPeakHeight - (midY - floorY));
            Vector3 flat = to - from; flat.y = 0f;
            Vector3 side = flat.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 pt = Vector3.Lerp(from, to, t);
                pt.y += 4f * t * (1f - t) * lift;
                if (i != 0 && i != n)
                {
                    pt += side * (((float)rng.NextDouble() - 0.5f) * 2f * jag);
                    pt.y += ((float)rng.NextDouble() - 0.5f) * jag;
                }
                pts[i] = pt;
            }
        }

        private static readonly Vector3[] ArcScratch = new Vector3[12];

        private void UpdateArcs()
        {
            if (_arcs == null)
            {
                _arcRoot = new GameObject("ArcStriker Arcs");
                _arcs = new LineRenderer[ArcCount];
                for (int i = 0; i < ArcCount; i++)
                {
                    var go = new GameObject("Arc" + i);
                    go.transform.SetParent(_arcRoot.transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.sharedMaterial = VfxMaterials.Additive(VfxMaterials.LineGlow());
                    lr.useWorldSpace = true;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lr.widthMultiplier = 0.12f;
                    lr.startColor = lr.endColor = ArcColor;
                    lr.positionCount = ArcScratch.Length;
                    _arcs[i] = lr;
                }
            }
            Vector3 orb = OrbPosition();
            int bucket = (int)(_markElapsed / ArcReJagSeconds);
            float k = MarkProgress;
            for (int i = 0; i < _arcs.Length; i++)
            {
                BuildArcPoints(ArcScratch, orb, _center, 0.3f + 0.2f * i, bucket * 31 + i * 7919);
                _arcs[i].SetPositions(ArcScratch);
                float flicker = ((bucket + i) & 1) == 0 ? 1f : 0.55f;
                Color c = ArcColor; c.a = flicker * Mathf.Lerp(0.6f, 1f, k);
                _arcs[i].startColor = _arcs[i].endColor = c;
                _arcs[i].enabled = true;
            }
        }

        private void HideArcs()
        {
            if (_arcs == null) return;
            foreach (var lr in _arcs) if (lr != null) lr.enabled = false;
        }

        /// <summary>True from the instant a circle is placed until its strike lands.</summary>
        public bool IsMarking => _marking;

        /// <summary>Where the live circle is centred (ground level); meaningless unless <see cref="IsMarking"/>.</summary>
        public Vector3 MarkCenter => _center;

        /// <summary>0..1 through the 1.0 s mark (0 when not marking) — drives the orb/coil glow.</summary>
        public float MarkProgress => _marking ? Mathf.Clamp01(_markElapsed / MarkSeconds) : 0f;

        /// <summary>The floor circle's renderer, for resolved-size reads (null until the first mark).</summary>
        public Renderer MarkRenderer => _ring != null ? _ring.GetComponent<Renderer>() : null;

        /// <summary>Total strikes landed — a counter a test or capture can read.</summary>
        public int Strikes { get; private set; }

        /// <summary>Advance by <paramref name="dt"/>. <paramref name="feet"/> is the striker's own ground
        /// position, <paramref name="hasSight"/> whether it currently sees Max. Returns true on the tick the
        /// strike lands.</summary>
        public bool Tick(float dt, Vector3 feet, Transform target, bool hasSight)
        {
            SetTarget(target);
            _feet = feet;
            if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - dt);

            if (_marking)
            {
                _markElapsed += dt;
                PulseRing();
                if (_markElapsed < MarkSeconds) { UpdateArcs(); return false; }
                Strike();
                return true;
            }

            if (target == null || !hasSight || _cooldown > 0f) return false;
            Vector3 d = target.position - feet; d.y = 0f;
            if (d.magnitude > MarkRange) return false;

            _center = new Vector3(target.position.x, GroundY(target), target.position.z);
            _marking = true;
            _markElapsed = 0f;
            _cooldown = CooldownSeconds;
            _ring ??= GroundRing.Create("ArcStriker Mark", VfxMaterials.Ring(), additive: true);
            PulseRing();
            UpdateArcs();
            return false;
        }

        private static float GroundY(Transform target)
        {
            var cc = target.GetComponent<CharacterController>();
            float half = cc != null ? cc.height * 0.5f - cc.center.y : 0f;
            return target.position.y - half;
        }

        private void PulseRing()
        {
            if (_ring == null) return;
            // Pulse faster as the strike nears; the circle itself stays exactly CircleDiameter.
            float t = MarkProgress;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * (3f + 3f * t));
            Color c = Color.Lerp(RingColor, Color.white, t * 0.6f);
            c.a = Mathf.Lerp(0.55f, 1f, pulse);
            _ring.Show(_center, CircleDiameter * 0.5f, c);
        }

        private readonly List<Sentinel> _sentinelScratch = new List<Sentinel>(8);

        private void Strike()
        {
            _marking = false;
            _markElapsed = 0f;
            Strikes++;
            if (_ring != null) _ring.Hide();
            HideArcs();
            Vector3 orbAtStrike = OrbPosition();

            float r = CircleDiameter * 0.5f;
            _sentinelScratch.Clear();
            foreach (Sentinel s in Sentinel.Active) if (s != null) _sentinelScratch.Add(s);
            foreach (Sentinel s in _sentinelScratch)
            {
                if (!s.IsAlive || Flat(s.transform.position, _center) > r) continue;
                s.TakeDamage(new DamageInfo(Damage, _center, Vector3.down, Team.Enemy));
            }

            if (_lastTarget != null && _lastTargetDamageable != null && _lastTargetDamageable.IsAlive &&
                Flat(_lastTarget.position, _center) <= r)
                _lastTargetDamageable.TakeDamage(new DamageInfo(Damage, _center, Vector3.down, Team.Enemy));

            if (Application.isPlaying)
            {
                ArcStrikeFx.Spawn(_center, orbAtStrike);
                HudSignals.EmitArcStrikeLanded(_center);
            }
        }

        private Transform _lastTarget;
        private IDamageable _lastTargetDamageable;

        /// <summary>Tell the battery who Max is (resolved once, not per tick).</summary>
        public void SetTarget(Transform target)
        {
            if (target == _lastTarget) return;
            _lastTarget = target;
            _lastTargetDamageable = target != null ? target.GetComponent<IDamageable>() : null;
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Drop a live mark with no strike (this robot died or was captured).</summary>
        public void Cancel()
        {
            _marking = false;
            _markElapsed = 0f;
            if (_ring != null) _ring.Hide();
            HideArcs();
        }

        /// <summary>Destroy the ring object (robot teardown / test cleanup).</summary>
        public void Dispose()
        {
            Cancel();
            if (_arcRoot != null)
            {
                if (Application.isPlaying) Object.Destroy(_arcRoot); else Object.DestroyImmediate(_arcRoot);
                _arcRoot = null; _arcs = null;
            }
            if (_ring == null) return;
            if (Application.isPlaying) Object.Destroy(_ring.gameObject);
            else Object.DestroyImmediate(_ring.gameObject);
            _ring = null;
        }
    }

    /// <summary>The strike's presentation (MV-1434): ONE thick jagged bolt from the ORB to the circle centre,
    /// arcing up to 3 m over cover, white core inside a #BFE3FF edge, flashing at both ends, 0.15 s, plus the
    /// floor flash.</summary>
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class ArcStrikeFx : MonoBehaviour
    {
        public const float Life = 0.15f;
        private const int Segs = 11;

        private float _age;
        private LineRenderer _edge, _core, _orbFlash, _hitFlash;
        private GroundRing _flash;

        /// <summary>The bolt's outer (#BFE3FF) line - first point at the orb, last at the circle centre.</summary>
        public LineRenderer Bolt => _edge;

        public static ArcStrikeFx Spawn(Vector3 ground, Vector3 orb)
        {
            var go = new GameObject("ArcStrikeFx");
            go.transform.position = ground;
            var fx = go.AddComponent<ArcStrikeFx>();
            fx.Build(ground, orb);
            return fx;
        }

        private static LineRenderer MakeLine(Transform parent, string name, float width, Color color, int count)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = VfxMaterials.Additive(VfxMaterials.LineGlow());
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 4;
            lr.positionCount = count;
            lr.widthMultiplier = width;
            lr.startColor = lr.endColor = color;
            return lr;
        }

        private void Build(Vector3 ground, Vector3 orb)
        {
            var pts = new Vector3[Segs + 1];
            ArcStrikerBattery.BuildArcPoints(pts, orb, ground, 0.45f, ground.GetHashCode());
            _edge = MakeLine(transform, "BoltEdge", 0.5f, ArcStrikerBattery.ArcColor, pts.Length);
            _core = MakeLine(transform, "BoltCore", 0.2f, Color.white, pts.Length);
            _edge.SetPositions(pts);
            _core.SetPositions(pts);

            _orbFlash = MakeLine(transform, "OrbFlash", 0.8f, Color.white, 2);
            _orbFlash.SetPositions(new[] { orb, orb + Vector3.up * 0.01f });
            _hitFlash = MakeLine(transform, "HitFlash", 1.0f, ArcStrikerBattery.ArcColor, 2);
            _hitFlash.SetPositions(new[] { ground + Vector3.up * 0.1f, ground + Vector3.up * 0.11f });

            _flash = GroundRing.Create("ArcStrike Flash", VfxMaterials.Glow(), additive: true);
            _flash.transform.SetParent(transform, true);
            _flash.Show(ground, ArcStrikerBattery.CircleDiameter * 0.75f, new Color(0.7f, 0.85f, 1f, 1f));
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_age / Life);
            if (_edge != null)
            {
                float w = 0.4f + 0.6f * k;
                _edge.widthMultiplier = 0.5f * w;
                _core.widthMultiplier = 0.2f * w;
                Color e = ArcStrikerBattery.ArcColor; e.a = k;
                _edge.startColor = _edge.endColor = e;
                _core.startColor = _core.endColor = new Color(1f, 1f, 1f, k);
                _orbFlash.widthMultiplier = 0.8f * w;
                _hitFlash.widthMultiplier = 1.0f * w;
            }
            if (_flash != null) _flash.Show(transform.position, ArcStrikerBattery.CircleDiameter * (0.5f + 0.35f * k), new Color(0.7f, 0.85f, 1f, k));
            if (_age >= Life) Destroy(gameObject);
        }
    }
}
