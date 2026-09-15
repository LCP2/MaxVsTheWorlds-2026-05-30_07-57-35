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
        private GroundRing _groundGlow;
        private float _groundGlowRadius;

        /// <summary>Fires one bolt from <paramref name="muzzle"/> straight to
        /// <paramref name="impactPoint"/> — the hitscan's own already-resolved hit point — at
        /// <paramref name="speed"/> m/s. No target is tracked: this never re-aims mid-flight.</summary>
        public static SentinelBolt Fire(Vector3 muzzle, Vector3 impactPoint, float speed)
        {
            var go = new GameObject("SentinelBolt");
            go.transform.position = muzzle;
            Vector3 dir = impactPoint - muzzle;
            go.transform.rotation = dir.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(dir.normalized, Vector3.up)
                : Quaternion.identity;
            BuildVisual(go.transform);

            var bolt = go.AddComponent<SentinelBolt>();
            bolt.Init(muzzle, impactPoint, speed);
            return bolt;
        }

        private void Init(Vector3 from, Vector3 to, float speed)
        {
            _from = from;
            _to = to;
            float distance = Vector3.Distance(from, to);
            _flightSeconds = Mathf.Max(0.02f, distance / Mathf.Max(0.1f, speed));

            CombatVfxTuning.LppeBoltTuning t = CombatVfxTuning.LppeBolt();
            _groundGlowRadius = t.GroundGlowDiameter * SizeScale * 0.5f;
            _groundGlow = GroundRing.Create("SentinelBoltGroundGlow", additive: true);
            UpdateGroundGlow();
        }

        private void Update() => Tick(Time.deltaTime);

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
            Color glowColor = BoltColor;
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

        /// <summary>Same build idiom as <see cref="MaxWorlds.Weapons.SeekerPulse.BuildVisual"/>: the
        /// same cached lathed bolt mesh (MV-810 -- was its own <c>GameObject.CreatePrimitive</c> capsule,
        /// rebuilt and its collider destroyed on every single shot), an additive unlit material, a short
        /// trail — just smaller and red instead of cyan-white.</summary>
        private static void BuildVisual(Transform parent)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Material boltMat = VfxMaterials.AdditiveTinted(BoltColor);
            CombatVfxTuning.LppeBoltTuning t = CombatVfxTuning.LppeBolt();

            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = t.TrailLifetime;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = t.TrailWidth * SizeScale;
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = boltMat;
            trail.Clear();

            var bolt = new GameObject("Bolt");
            bolt.transform.SetParent(parent, false);
            bolt.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            bolt.transform.localScale = Vector3.one * SizeScale;
            bolt.AddComponent<MeshFilter>().sharedMesh = SeekerPulse.GetBoltMesh();
            var meshRenderer = bolt.AddComponent<MeshRenderer>();
            if (boltMat != null) meshRenderer.sharedMaterial = boltMat;
        }
    }
}
