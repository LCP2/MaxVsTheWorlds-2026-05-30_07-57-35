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
            if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - dt);

            if (_marking)
            {
                _markElapsed += dt;
                PulseRing();
                if (_markElapsed < MarkSeconds) return false;
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
                ArcStrikeFx.Spawn(_center);
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
        }

        /// <summary>Destroy the ring object (robot teardown / test cleanup).</summary>
        public void Dispose()
        {
            Cancel();
            if (_ring == null) return;
            if (Application.isPlaying) Object.Destroy(_ring.gameObject);
            else Object.DestroyImmediate(_ring.gameObject);
            _ring = null;
        }
    }

    /// <summary>The strike's presentation: a bright jagged vertical bolt and a floor flash, 0.3 s.</summary>
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class ArcStrikeFx : MonoBehaviour
    {
        private const float Life = 0.3f;
        private const float BoltHeight = 16f;

        private float _age;
        private LineRenderer _line;
        private GroundRing _flash;

        public static ArcStrikeFx Spawn(Vector3 ground)
        {
            var go = new GameObject("ArcStrikeFx");
            go.transform.position = ground;
            var fx = go.AddComponent<ArcStrikeFx>();
            fx.Build(ground);
            return fx;
        }

        private void Build(Vector3 ground)
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial = VfxMaterials.Additive(VfxMaterials.LineGlow());
            _line.useWorldSpace = true;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            const int segs = 9;
            _line.positionCount = segs + 1;
            var rng = new System.Random(ground.GetHashCode());
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                float jag = (i == 0 || i == segs) ? 0f : ((float)rng.NextDouble() - 0.5f) * 1.1f;
                float jag2 = (i == 0 || i == segs) ? 0f : ((float)rng.NextDouble() - 0.5f) * 1.1f;
                _line.SetPosition(i, new Vector3(ground.x + jag, ground.y + 0.05f + (1f - t) * BoltHeight, ground.z + jag2));
            }
            _line.widthMultiplier = 0.35f;
            _line.startColor = _line.endColor = Color.white;

            _flash = GroundRing.Create("ArcStrike Flash", VfxMaterials.Glow(), additive: true);
            _flash.transform.SetParent(transform, true);
            _flash.Show(ground, ArcStrikerBattery.CircleDiameter * 0.75f, new Color(0.7f, 0.85f, 1f, 1f));
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(_age / Life);
            if (_line != null)
            {
                _line.widthMultiplier = 0.35f * (0.4f + 0.6f * k);
                Color c = new Color(1f, 1f, 1f, k);
                _line.startColor = _line.endColor = c;
            }
            if (_flash != null) _flash.Show(transform.position, ArcStrikerBattery.CircleDiameter * (0.5f + 0.35f * k), new Color(0.7f, 0.85f, 1f, k));
            if (_age >= Life) Destroy(gameObject);
        }
    }
}
