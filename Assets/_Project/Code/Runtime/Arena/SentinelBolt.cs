using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// MV-806: the Sentinel's own firing bolt, replacing the reused <see cref="WaterVfx"/> beam that
    /// used to draw the SAME jet Max's own RCDA fires — a friendly turret spraying the player's own
    /// weapon read as a bigger, brighter version of it and upstaged him. Built the same way Max's own
    /// LPPE bolt is (<see cref="MaxWorlds.Weapons.SeekerPulse"/>'s <c>BuildVisual</c>: a generated
    /// capsule, an additive unlit trail material, a ground glow) but in the world's own hazard red
    /// (<see cref="StormdrainLightKit.Red"/>) at 0.7x the LPPE bolt's resolved size, and travelling a
    /// straight line at constant speed — no homing, no steering. The shot is already a resolved hitscan
    /// by the time <see cref="Sentinel.Update"/> spawns this (damage already landed), so this
    /// component's only job is cosmetic: fly from the muzzle to the point that was already hit, then
    /// vanish.
    /// </summary>
    [MaxWorlds.Core.PerfSection("sentinel")]
    public sealed class SentinelBolt : MonoBehaviour
    {
        /// <summary>The world's existing hazard red (MV-806 spec: "the world's existing hazard red
        /// (StormdrainLightKit.LAMP.red)") — never the sentinel's own former cyan, so a friendly turret
        /// reads as ordnance, not as a copy of Max's own jet.</summary>
        public static readonly Color BoltColor = StormdrainLightKit.Red;

        /// <summary>MV-806: "slightly smaller than Max's" — cross-section, length, trail width and
        /// ground-glow diameter all read off <see cref="CombatVfxTuning.LppeBolt"/> and scaled by this,
        /// rather than re-typed, so this bolt can never drift bigger than Max's own when his is retuned.</summary>
        private const float SizeScale = 0.7f;

        private Vector3 _from;
        private Vector3 _to;
        private float _flightSeconds;
        private float _age;
        private bool _spent;
        private bool _externallyDriven;
        private GroundRing _groundGlow;
        private float _groundGlowRadius;
        private Color _boltColor;

        /// <summary>Fires one bolt from <paramref name="muzzle"/> straight to
        /// <paramref name="impactPoint"/> — the hitscan's own already-resolved hit point — at
        /// <paramref name="speed"/> m/s. No target is tracked: this never re-aims mid-flight.
        /// <paramref name="boltColor"/>/<paramref name="thicknessScale"/> are the firing
        /// <see cref="Sentinel"/>'s own already-resolved per-world style (MV-1069) — defaulted to
        /// <see cref="BoltColor"/>/1x so every pre-existing caller/test that doesn't pass one keeps
        /// today's uniform red, <see cref="SizeScale"/>-only bolt. <paramref name="externallyDriven"/>
        /// (MV-1125) suppresses this bolt's own <see cref="Update"/> tick — for a caller (e.g.
        /// <see cref="MaxWorlds.VFX.WorldFinaleGate"/>'s exit beat) that already drives <see cref="Tick"/>
        /// itself once a frame; without it, a bolt fired from Play mode got ticked TWICE a frame (its own
        /// Update AND the owner's explicit Tick call), arriving in roughly half the intended flight time.</summary>
        public static SentinelBolt Fire(Vector3 muzzle, Vector3 impactPoint, float speed,
            Color? boltColor = null, float thicknessScale = 1f, bool externallyDriven = false)
        {
            Color color = boltColor ?? BoltColor;

            var go = new GameObject("SentinelBolt");
            go.transform.position = muzzle;
            Vector3 dir = impactPoint - muzzle;
            go.transform.rotation = dir.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(dir.normalized, Vector3.up)
                : Quaternion.identity;
            BuildVisual(go.transform, color, thicknessScale);

            var bolt = go.AddComponent<SentinelBolt>();
            bolt._externallyDriven = externallyDriven;
            bolt.Init(muzzle, impactPoint, speed, color);
            return bolt;
        }

        private void Init(Vector3 from, Vector3 to, float speed, Color boltColor)
        {
            _from = from;
            _to = to;
            _boltColor = boltColor;
            float distance = Vector3.Distance(from, to);
            _flightSeconds = Mathf.Max(0.02f, distance / Mathf.Max(0.1f, speed));

            CombatVfxTuning.LppeBoltTuning t = CombatVfxTuning.LppeBolt();
            _groundGlowRadius = t.GroundGlowDiameter * SizeScale * 0.5f;
            _groundGlow = GroundRing.Create("SentinelBoltGroundGlow", additive: true);
            UpdateGroundGlow();
        }

        private void Update()
        {
            if (_externallyDriven) return;
            Tick(Time.deltaTime);
        }

        /// <summary>Advance one step. Public — same reason <see cref="MaxWorlds.Weapons.SeekerPulse.Tick"/>
        /// is public — so an EditMode test can drive the straight-line flight deterministically without
        /// a live Unity frame loop.</summary>
        public void Tick(float dt)
        {
            if (_spent) return;
            _age += dt;
            float t = Mathf.Clamp01(_age / _flightSeconds);
            transform.position = Vector3.Lerp(_from, _to, t);
            UpdateGroundGlow();
            if (t >= 1f) Retire();
        }

        private void UpdateGroundGlow()
        {
            if (_groundGlow == null) return;
            Vector3 pos = transform.position;
            Color glowColor = _boltColor;
            glowColor.a = 0.5f;
            _groundGlow.Show(new Vector3(pos.x, 0f, pos.z), _groundGlowRadius, glowColor);
        }

        private void Retire()
        {
            if (_spent) return;
            _spent = true;
            if (_groundGlow != null)
            {
                GameObject glowGo = _groundGlow.gameObject;
                if (Application.isPlaying) Destroy(glowGo); else DestroyImmediate(glowGo);
                _groundGlow = null;
            }
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>MV-815/825: this bolt's own cached straight mesh -- never Max's own (MV-806 made
        /// this bolt small, red and straight-line-only precisely so it cannot be confused with his;
        /// sharing his own mesh would undo that). Built once, lazily, from the same straight ogive
        /// profile (<see cref="SeekerPulse.BuildStraightBoltMesh"/>) Max's own bolt used before MV-815
        /// -- never rebuilt per shot (MV-810).</summary>
        private static Mesh s_boltMesh;

        /// <summary>The single cached straight bolt mesh, built lazily on first use and shared by
        /// every Sentinel bolt. MV-825: sized off Max's own new core length/diameter
        /// (<see cref="CombatVfxTuning.LppeBolt"/>'s <c>CoreLength</c>/<c>CoreDiameter</c>) scaled by
        /// <see cref="SizeScale"/> -- same "can never drift bigger than Max's own when his is retuned"
        /// coupling MV-806 established, just re-pointed at the core dimensions now that Max's own bolt
        /// is no longer a single capsule (see MV806SentinelBoltTests' own 0.6x-0.8x ratio check).</summary>
        public static Mesh GetBoltMesh()
        {
            if (s_boltMesh == null)
            {
                CombatVfxTuning.LppeBoltTuning shape = CombatVfxTuning.LppeBolt();
                s_boltMesh = SeekerPulse.BuildStraightBoltMesh(shape.CoreLength, shape.CoreDiameter * 0.5f);
            }
            return s_boltMesh;
        }

        /// <summary>Drop this class's own reference to the cached bolt mesh -- same idiom as
        /// <see cref="SeekerPulse.ResetForTests"/>.</summary>
        public static void ResetForTests() => s_boltMesh = null;

        /// <summary>Same build idiom as <see cref="MaxWorlds.Weapons.SeekerPulse.BuildVisual"/>: the
        /// same cached lathed bolt mesh (MV-810 -- was its own <c>GameObject.CreatePrimitive</c> capsule,
        /// rebuilt and its collider destroyed on every single shot), an additive unlit material, a short
        /// trail — just smaller and tinted <paramref name="boltColor"/> instead of cyan-white. MV-1069:
        /// <paramref name="thicknessScale"/> widens the mesh's cross-section and the trail width on top
        /// of <see cref="SizeScale"/> — length (mesh-local Y, the lathe's own revolve axis, which the
        /// 90° X rotation below aligns to the parent's travel direction) and the ground glow (set in
        /// <see cref="Init"/>, off <see cref="SizeScale"/> alone) are untouched, matching the ticket's
        /// "cross-section x1.6 while length is unchanged".</summary>
        private static void BuildVisual(Transform parent, Color boltColor, float thicknessScale)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Material boltMat = VfxMaterials.AdditiveTinted(boltColor);
            CombatVfxTuning.LppeBoltTuning t = CombatVfxTuning.LppeBolt();
            float crossScale = SizeScale * thicknessScale;

            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = t.TrailLifetime;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = t.TrailWidth * crossScale;
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = boltMat;
            trail.Clear();

            var bolt = new GameObject("Bolt");
            bolt.transform.SetParent(parent, false);
            bolt.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bolt.transform.localScale = new Vector3(crossScale, SizeScale, crossScale);
            bolt.AddComponent<MeshFilter>().sharedMesh = GetBoltMesh();
            var meshRenderer = bolt.AddComponent<MeshRenderer>();
            if (boltMat != null) meshRenderer.sharedMaterial = boltMat;
        }
    }
}
