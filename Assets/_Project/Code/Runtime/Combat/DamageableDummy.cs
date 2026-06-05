using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// TEMPORARY (YT-35 verification only). A static target that implements
    /// <see cref="IDamageable"/> so the Water Blaster's damage + hit feedback are
    /// observable before the real enemy exists (YT-36). On hit it flashes its
    /// material and emits an impact puff; on death it disables itself.
    /// Delete once the YT-36 enemy is the damage receiver.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class DamageableDummy : MonoBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 40f;
        [SerializeField] private Color flashColor = Color.white;
        [SerializeField] private float flashDecay = 6f;

        private float _health;
        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;
        private float _flash;
        private Color _baseColor = new Color(0.8f, 0.3f, 0.3f);

        public bool IsAlive => _health > 0f;
        public Team Team => Team.Neutral; // always damageable (test target)

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _health = maxHealth;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            _health -= info.Amount;
            _flash = 1f;
            SpawnImpactPuff(info.Point, info.Direction);
            if (_health <= 0f)
            {
                gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (_flash <= 0f) return;
            _flash = Mathf.Max(0f, _flash - flashDecay * Time.deltaTime);
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", Color.Lerp(_baseColor, flashColor, _flash));
            _renderer.SetPropertyBlock(_mpb);
        }

        private static void SpawnImpactPuff(Vector3 point, Vector3 dir)
        {
            var go = new GameObject("ImpactPuff");
            go.transform.position = point;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 0.25f;
            main.startSpeed = 3f;
            main.startSize = 0.2f;
            main.startColor = new Color(0.6f, 0.85f, 1f, 1f);
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8) });
            emission.rateOverTime = 0f;
            ps.Play();
        }
    }
}
