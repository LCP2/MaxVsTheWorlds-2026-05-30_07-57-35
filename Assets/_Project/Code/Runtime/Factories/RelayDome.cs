using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// MV-1304: a Teleport Relay's own protective dome — up for as long as at least one
    /// <see cref="Pylon.LiveFeedersOf"/> pylon still feeds it. A translucent red hemisphere (radius =
    /// relay footprint + 1 m, ticket point 2) carrying ONE bright ring per live feeder, concentric at
    /// its base (design reference MV-1304.png: two close concentric ellipses for two pylons). A
    /// feeding pylon's death bursts the outermost ring (>= 10 frames); once the last one falls the
    /// whole dome collapses over <see cref="CollapseSeconds"/> and self-destroys.
    ///
    /// Built/owned/repositioned by the <see cref="Replicator"/> it protects (see
    /// <c>Replicator.UpdateRelayDome</c>) but an independent GameObject that ticks its own fades via
    /// <c>Update</c> — same "VFX prop outlives/outruns its owner's own per-frame hook" footing as
    /// <see cref="MaxWorlds.Enemies.ProjectorDome"/>, whose grounded-full-sphere shortcut and
    /// <c>MaxWorlds/ForceFieldShield</c> shader this reuses, tinted red instead of white-blue.
    /// </summary>
    [MaxWorlds.Core.PerfSection("relaydome")]
    public sealed class RelayDome : MonoBehaviour
    {
        public const int MaxRings = 8;
        public const float CollapseSeconds = 0.3f; // ticket point 2: "dome collapses (0.3 s)"
        public const float BurstSeconds = 0.2f;    // ticket point 2: ">= 10 frames" (12 frames at 60fps)
        public const float BlockedFlashSeconds = 0.15f; // ticket point 3: "a blocked hit flashes the dome"

        private static readonly Color FillColor = new Color(0.95f, 0.12f, 0.08f, 0.16f);
        private static readonly Color RimColor = new Color(1f, 0.35f, 0.22f, 1f);
        private static readonly Color RingColor = new Color(1f, 0.28f, 0.16f, 1f);
        private static readonly Color BlockedFillColor = new Color(1f, 0.55f, 0.35f, 0.4f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int RimColorId = Shader.PropertyToID("_RimColor");

        private MeshRenderer _domeRenderer;
        private MaterialPropertyBlock _domeMpb;
        private readonly List<GameObject> _rings = new List<GameObject>(MaxRings);
        private float _radius;
        private float _footprintRadius;

        private int _liveCount;
        private int _burstingRingIndex = -1;
        private float _burstRemaining;
        private bool _collapsing;
        private float _collapseRemaining;
        private float _flashRemaining;

        public static RelayDome Spawn(Vector3 groundCenter, float radius, float footprintRadius, int initialLiveCount)
        {
            var go = new GameObject("RelayDome (stand-in)");
            go.transform.position = groundCenter;
            var dome = go.AddComponent<RelayDome>();
            dome._domeMpb = new MaterialPropertyBlock();
            dome._radius = radius;
            dome._footprintRadius = footprintRadius;
            dome.Build();
            dome._liveCount = Mathf.Clamp(initialLiveCount, 0, MaxRings);
            for (int i = 0; i < dome._rings.Count; i++)
                dome._rings[i].SetActive(i < dome._liveCount);
            return dome;
        }

        private void Build()
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            vis.name = "Visual";
            var col = vis.GetComponent<Collider>();
            if (col != null)
            {
                // Pure visual, never a solid — a robot or Max must still walk through it freely.
                if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
            }
            vis.transform.SetParent(transform, worldPositionStays: false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localScale = Vector3.one * _radius * 2f;

            // Anything driving its own renderer colour through a MaterialPropertyBlock must carry
            // this, or RuntimeSurfaceDirector's sweep claims it within a frame (same note
            // ProjectorDome's own Build carries).
            vis.AddComponent<SelfDrivenTint>();

            _domeRenderer = vis.GetComponent<MeshRenderer>();
            _domeRenderer.sharedMaterial = DomeMaterial();
            _domeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _domeRenderer.receiveShadows = false;

            for (int i = 0; i < MaxRings; i++)
                _rings.Add(BuildRing(i));

            ApplyDomeAlpha(1f);
        }

        private GameObject BuildRing(int index)
        {
            var go = new GameObject($"Ring{index}");
            go.transform.SetParent(transform, worldPositionStays: false);

            var lr = go.AddComponent<LineRenderer>();
            const int segments = 24;
            lr.positionCount = segments + 1;
            lr.useWorldSpace = false;
            lr.widthMultiplier = 0.1f;
            lr.material = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            lr.startColor = lr.endColor = RingColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            // Concentric at the relay's own footprint, fanned out slightly per index so a multi-pylon
            // relay's rings read as distinct rather than drawing exactly on top of one another.
            float ringRadius = _footprintRadius + index * 0.15f;
            for (int s = 0; s <= segments; s++)
            {
                float t = (float)s / segments * Mathf.PI * 2f;
                lr.SetPosition(s, new Vector3(Mathf.Cos(t) * ringRadius, 0.05f, Mathf.Sin(t) * ringRadius));
            }

            go.SetActive(false);
            return go;
        }

        /// <summary>Re-centres the dome — the relay itself never moves, but this keeps the dome
        /// correct if it's ever rebuilt mid-life.</summary>
        public void Reposition(Vector3 groundCenter) => transform.position = groundCenter;

        /// <summary>Point 2: a feeding pylon's death drops the live count by one — bursts the
        /// outermost ring (>= 10 frames) and, once that burst finishes with nothing left feeding this
        /// relay, collapses the whole dome over <see cref="CollapseSeconds"/> and self-destroys.
        /// Called every tick by <c>Replicator.UpdateRelayDome</c> with the live
        /// <see cref="Pylon.LiveFeedersOf"/> count — a no-op when nothing has changed.</summary>
        public void SetLiveFeederCount(int count)
        {
            count = Mathf.Clamp(count, 0, MaxRings);
            if (count == _liveCount) return;

            if (count < _liveCount)
            {
                // A ring already mid-burst finishes instantly rather than stacking two bursts — the
                // one that just dropped takes over.
                if (_burstingRingIndex >= 0) _rings[_burstingRingIndex].SetActive(false);
                _burstingRingIndex = _liveCount - 1;
                _burstRemaining = BurstSeconds;
            }
            else
            {
                // Growing (a pylon can never un-die in shipped gameplay, kept correct/symmetric
                // anyway): show the new rings immediately, no burst-in animation.
                for (int i = _liveCount; i < count; i++) _rings[i].SetActive(true);
            }

            _liveCount = count;
        }

        /// <summary>Point 3: "a blocked hit flashes the dome" — a brief bright pulse, independent of
        /// the collapse/burst fades above.</summary>
        public void FlashBlocked() => _flashRemaining = BlockedFlashSeconds;

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Public and dt-parameterised, same "an EditMode test can drive it directly"
        /// reasoning as every other VFX prop in this codebase (ProjectorDome.Tick, MoltenPatch.Tick) —
        /// unused by this ticket's own test (the dome never spawns outside Play mode, see
        /// Replicator.UpdateRelayDome), kept for parity with the convention regardless.</summary>
        public void Tick(float dt)
        {
            if (_flashRemaining > 0f) _flashRemaining = Mathf.Max(0f, _flashRemaining - dt);

            if (_burstingRingIndex >= 0)
            {
                _burstRemaining -= dt;
                float t = Mathf.Clamp01(_burstRemaining / BurstSeconds);
                ApplyRingBurstVisual(_burstingRingIndex, t);
                if (_burstRemaining <= 0f)
                {
                    _rings[_burstingRingIndex].SetActive(false);
                    _burstingRingIndex = -1;
                    if (_liveCount <= 0 && !_collapsing) BeginCollapse();
                }
            }

            if (_collapsing)
            {
                _collapseRemaining -= dt;
                float t = Mathf.Clamp01(_collapseRemaining / CollapseSeconds);
                ApplyDomeAlpha(t);
                if (_collapseRemaining <= 0f) Discard();
                return;
            }

            ApplyDomeAlpha(1f);
        }

        private void BeginCollapse()
        {
            _collapsing = true;
            _collapseRemaining = CollapseSeconds;
        }

        private void ApplyRingBurstVisual(int index, float t)
        {
            GameObject ring = _rings[index];
            if (!ring.activeSelf) ring.SetActive(true);
            var lr = ring.GetComponent<LineRenderer>();
            // Flares outward and fades as it bursts — t runs 1 -> 0 over BurstSeconds.
            lr.widthMultiplier = Mathf.Lerp(0.1f, 0.3f, 1f - t);
            Color c = RingColor; c.a *= t;
            lr.startColor = lr.endColor = c;
        }

        private void ApplyDomeAlpha(float t)
        {
            if (_domeRenderer == null) return;
            Color fill = _flashRemaining > 0f ? BlockedFillColor : FillColor;
            Color rim = RimColor;
            fill.a *= t;
            rim.a *= t;
            _domeMpb.SetColor(BaseColorId, fill);
            _domeMpb.SetColor(RimColorId, rim);
            _domeRenderer.SetPropertyBlock(_domeMpb);
        }

        /// <summary>Owner-forced teardown — the relay itself was destroyed (or torn down for a scene
        /// reload) and this dome has nothing left to protect, regardless of its own fade state.</summary>
        public void Discard()
        {
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        /// <summary>Same shader/fallback choice as <see cref="MaxWorlds.Enemies.ProjectorDome"/>'s own
        /// dome material — a real Fresnel rim where available, a flat translucent fill where it isn't.</summary>
        private static Material DomeMaterial()
        {
            var shader = Shader.Find("MaxWorlds/ForceFieldShield");
            if (shader == null || !shader.isSupported)
                return VfxMaterials.AlphaBlend(VfxMaterials.Solid());

            return new Material(shader)
            {
                name = "RelayDome",
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
