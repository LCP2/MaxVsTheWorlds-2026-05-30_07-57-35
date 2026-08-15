using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// The Gunner (Attack) sentinel (MV-362): a low-HP turret — "a hose pipe on a stick", reusing the
    /// primary's own blue — that auto-fires at the nearest robot in range. "Always weaker than Max's
    /// CURRENT primary... it must stay below Max's current power as he upgrades" is enforced
    /// structurally, not by a cap that could drift out of date: every shot's damage is a FRACTION
    /// (&lt; 1.0, see <see cref="AbilityTuning.SentinelGunnerDamagePerShot"/>) of Max's own live RCDA
    /// Damage-track output, read fresh from <see cref="WeaponSystemState"/>/<see cref="WeaponCatalog"/>
    /// on every shot — so it can never overtake the weapon it's a fraction of, at any level either one
    /// is at.
    /// </summary>
    public sealed class GunnerSentinel : Sentinel
    {
        private static readonly Color BodyColor = new Color(0.35f, 0.55f, 0.75f); // the primary's blue
        private static readonly Collider[] s_hits = new Collider[16];

        public override SentinelKind Kind => SentinelKind.Gunner;
        public override string ReadoutName => "GUNNER";

        private float _range;
        private float _fireInterval;
        private int _powerLevel;
        private float _fireCooldown;

        /// <summary>Places and builds the turret. <paramref name="powerLevel"/> is the Gunner Power
        /// track's CURRENT level at deploy time — read fresh from <see cref="WeaponSystemState"/> each
        /// shot in practice, not cached, so a mid-fight upgrade is felt immediately by every turret
        /// already on the field, not just the next one deployed.</summary>
        public void Init(Vector3 position, float maxHp, float range, float fireInterval)
        {
            transform.position = position;
            _range = range;
            _fireInterval = fireInterval;
            InitHealth(maxHp);
            BuildBody();
            WorldHealthBar.Attach(gameObject, this, 1.9f, 1.2f, alwaysShow: true);
            Physics.SyncTransforms(); // see WallSentinel — autoSyncTransforms is off project-wide
        }

        private void BuildBody()
        {
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            vis.name = "Body";
            vis.transform.SetParent(transform, worldPositionStays: false);
            vis.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            vis.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);

            var rend = vis.GetComponent<Renderer>();
            if (rend != null)
            {
                var mpb = new MaterialPropertyBlock();
                rend.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", BodyColor);
                rend.SetPropertyBlock(mpb);
            }

            var col = vis.GetComponent<Collider>();
            if (col != null) col.isTrigger = false; // solid — robots route around it like the wall
        }

        private void Update()
        {
            if (!IsAlive) return;
            _fireCooldown -= Time.deltaTime;
            if (_fireCooldown > 0f) return;

            RobotEnemy target = NearestRobotInRange();
            if (target == null) return;

            _fireCooldown = _fireInterval;

            // Max's CURRENT primary per-tick damage, read live — this is what keeps the sentinel
            // "always weaker" as Max's own Damage track climbs (see the class doc comment).
            float primaryDamage = WeaponCatalog.EffectiveDamagePerTick(
                WaterBlaster.DefaultDamagePerTick,
                WeaponSystemState.TrackLevel(WeaponTrackKind.Damage),
                WeaponCatalog.DefaultRcdaDamagePerLevel);

            int powerLevel = WeaponSystemState.SentinelTrackLevel(SentinelTrackKind.GunnerPower);
            float damage = AbilityTuning.SentinelGunnerDamagePerShot(
                primaryDamage, powerLevel,
                AbilityTuning.DefaultSentinelGunnerPowerFraction,
                AbilityTuning.DefaultSentinelGunnerPowerFractionPerLevel);

            Vector3 dir = target.transform.position - transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;

            if (damage > 0f)
            {
                target.TakeDamage(new DamageInfo(damage, target.transform.position, dir, Team,
                    source: DamageSource.Ability));
            }
        }

        private RobotEnemy NearestRobotInRange()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _range, s_hits, ~0, QueryTriggerInteraction.Ignore);

            RobotEnemy best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (!s_hits[i].TryGetComponent<RobotEnemy>(out var robot) || !robot.IsAlive) continue;
                float d = (robot.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = robot; }
            }
            return best;
        }
    }
}
