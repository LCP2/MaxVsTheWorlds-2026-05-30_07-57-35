using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// One shared, world-space ParticleSystem that effects fire into with <see cref="Emit"/>.
    ///
    /// The pattern this replaces (used everywhere in the game before YT-47/48) was
    /// "new GameObject + AddComponent&lt;ParticleSystem&gt; + burst + stopAction = Destroy" on
    /// every single event — a GameObject and a ParticleSystem allocated and destroyed per
    /// kill. This allocates once, and every burst afterwards is just particles.
    ///
    /// Emissions are capped per frame: a boss AoE or a stream raking a crowd can raise a
    /// dozen events on one frame, and an uncapped burst-per-event is exactly how a particle
    /// spike happens.
    /// </summary>
    public sealed class VfxBurst
    {
        private readonly ParticleSystem _ps;
        private readonly int _perFrameCap;
        private int _usedThisFrame;

        public VfxBurst(string name, Material material, int maxParticles, float gravity,
                        int perFrameCap, bool stretched = false)
        {
            _perFrameCap = perFrameCap;

            var go = new GameObject(name);
            _ps = go.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;          // velocity is supplied per particle
            main.maxParticles = maxParticles;
            main.gravityModifier = gravity;

            var emission = _ps.emission;
            emission.enabled = false;      // Emit()-driven only
            emission.rateOverTime = 0f;

            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 0.25f)));

            var r = _ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = stretched ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretched) { r.lengthScale = 2.2f; r.velocityScale = 0.05f; }
            r.alignment = ParticleSystemRenderSpace.View;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            // Assigning the material is mandatory: AddComponent leaves it null and the
            // system then draws nothing at all. See VfxMaterials.
            if (material != null) r.sharedMaterial = material;

            _ps.Play();   // running, but silent until Emit() is called
        }

        public GameObject GameObject => _ps != null ? _ps.gameObject : null;

        /// <summary>Fire a burst. Returns false if this frame's budget is already spent.</summary>
        public bool Emit(Vector3 position, int count, Vector3 axis, float spreadDegrees,
                         float speedMin, float speedMax, float sizeMin, float sizeMax,
                         float lifeMin, float lifeMax, Color colorA, Color colorB)
        {
            if (_ps == null) return false;
            if (_usedThisFrame >= _perFrameCap) return false;
            _usedThisFrame++;

            Vector3 a = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
            float cos = Mathf.Cos(Mathf.Clamp(spreadDegrees, 0f, 180f) * Mathf.Deg2Rad);

            var ep = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Vector3.Slerp(a, Random.onUnitSphere, 1f - cos).normalized;
                if (Vector3.Dot(dir, a) < 0f) dir = -dir;

                ep.position = position;
                ep.velocity = dir * Random.Range(speedMin, speedMax);
                ep.startSize = Random.Range(sizeMin, sizeMax);
                ep.startLifetime = Random.Range(lifeMin, lifeMax);
                ep.startColor = Color.Lerp(colorA, colorB, Random.value);
                _ps.Emit(ep, 1);
            }
            return true;
        }

        /// <summary>Call once per frame (from the owner's LateUpdate) to refill the budget.</summary>
        public void EndFrame() => _usedThisFrame = 0;
    }
}
