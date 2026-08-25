using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The Bolter's straight-line rod bolt (MV-539) — the third ranged projectile, and the simplest:
    /// no homing (<see cref="HomingMissile"/>'s whole reason for being), no splash, no sputter/bounce
    /// fuel-out beat. It flies dead straight from the instant it's fired and despawns the moment it
    /// either lands a hit or travels past its own max range — there is nothing here for a fuel timer to
    /// fail gracefully out of.
    ///
    /// Damage is deliberately NOT a number this class is handed at <see cref="Fire"/> time (contrast
    /// <see cref="HomingMissile"/>'s <c>damage</c> parameter, straight off the Launcher archetype's
    /// <see cref="EnemyArchetype.ContactDamage"/>): the ticket's AC1 requires 5% of the player's
    /// resolved max health AT THE MOMENT OF IMPACT, so it is read live off <see cref="PlayerHealth.Max"/>
    /// inside <see cref="Detonate"/> instead of being baked in at spawn — and only ever applied to a
    /// <see cref="PlayerHealth"/> receiver specifically, never a robot, a shed or a Sentinel, whatever
    /// <see cref="IDamageable"/> happens to sit at the point of impact.
    /// </summary>
    public sealed class BolterBolt : MonoBehaviour
    {
        /// <summary>Fraction of the player's max health one bolt deals (MV-539 AC1) — 10 damage at
        /// today's 200 max HP, but never hardcoded as 10: this multiplies whatever
        /// <see cref="PlayerHealth.Max"/> resolves to at the instant of impact.</summary>
        public const float DamagePercentOfMaxHealth = 0.05f;

        /// <summary>Past the archetype's max fire range, plus this much slack (the ticket's own spec),
        /// the bolt gives up and despawns harmlessly rather than flying forever.</summary>
        public const float DespawnRangePadding = 2f;

        private const float FlightHeight = 1.0f;

        private Transform _target;
        private IDamageable _targetDamageable;
        private Vector3 _direction;
        private float _speed;
        private float _hitRadius;
        private float _maxDistance;
        private float _traveled;

        /// <summary>Fire one bolt from <paramref name="origin"/> straight at <paramref name="target"/>'s
        /// position at THIS instant ("fired at Max's position at fire time") — never re-aimed, never
        /// homed; <see cref="_direction"/> is locked here and read nowhere else. <paramref name="maxRange"/>
        /// is the archetype's own fire range (<see cref="EnemyArchetype.LungeRange"/>); this bolt
        /// despawns <see cref="DespawnRangePadding"/> past it. <paramref name="hitRadius"/> is the
        /// archetype's <see cref="EnemyArchetype.ContactRadius"/>, same "ranged kind's ContactRadius
        /// feeds the projectile" idiom <see cref="HomingMissile"/> uses for its splash radius.</summary>
        public static BolterBolt Fire(Vector3 origin, Transform target, float speed, float maxRange,
            float hitRadius)
        {
            var go = new GameObject("BolterBolt (stand-in)");
            go.transform.position = origin + Vector3.up * FlightHeight;

            Vector3 aim = target != null ? target.position - origin : Vector3.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-4f) aim = Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);

            BuildVisual(go.transform);

            var bolt = go.AddComponent<BolterBolt>();
            bolt.Init(target, speed, maxRange, hitRadius);
            return bolt;
        }

        /// <summary>Read live off <see cref="CharacterSkin.BaseColorFor"/> rather than a copied literal
        /// (same reasoning as <see cref="HomingMissile.ShaftColor"/>) — a retune of the Bolter's own
        /// body colour can never drift out of step with its bolt.</summary>
        private static Color RodColor => CharacterSkin.BaseColorFor(CharacterSkin.RoleFor(EnemyKind.Bolter));

        /// <summary>A single thin rod — approx 0.6 m long x 0.08 m radius per the ticket's spec — laid
        /// onto the object's own forward axis the same way <see cref="HomingMissile.BuildVisual"/>'s
        /// shaft is: a Capsule rotated 90° about X so its long axis (local Y) becomes local Z.</summary>
        private static void BuildVisual(Transform parent)
        {
            // Same reason as HomingMissile.BuildVisual: without this, RuntimeSurfaceDirector's sweep
            // claims this a frame after spawn and overwrites the deliberate paint with the generic
            // world-prop material (MV-350).
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Material rodMat = MaterialLibrary.Tinted(SurfaceKind.Metal, RodColor);

            var rod = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            rod.name = "Rod";
            Strip(rod);
            rod.transform.SetParent(parent, false);
            rod.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Unity's capsule primitive is 2 units pole-to-pole and 1 unit across at scale 1 — a local
            // Y scale of 0.3 gives a ~0.6 m rod, a local X/Z scale of 0.16 gives a ~0.08 m radius.
            rod.transform.localScale = new Vector3(0.16f, 0.3f, 0.16f);
            if (rodMat != null) rod.GetComponent<MeshRenderer>().sharedMaterial = rodMat;
        }

        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            // Manual proximity check in Update, not physics — same Application.isPlaying idiom as
            // HomingMissile.Strip: Destroy is illegal outside play mode, which an EditMode test calling
            // BolterBolt.Fire() hits directly.
            if (Application.isPlaying) Object.Destroy(col);
            else Object.DestroyImmediate(col);
        }

        private void Init(Transform target, float speed, float maxRange, float hitRadius)
        {
            _target = target;
            _targetDamageable = target != null ? target.GetComponent<IDamageable>() : null;
            _direction = transform.forward;
            _speed = speed;
            _hitRadius = hitRadius;
            _maxDistance = Mathf.Max(0f, maxRange) + DespawnRangePadding;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 next = Step(transform.position, _direction, _speed, dt);
            _traveled += Vector3.Distance(transform.position, next);
            transform.position = next;

            bool closeEnough = _target != null &&
                WithinHitRadius(transform.position, _target.position, _hitRadius);

            if (closeEnough) { Detonate(); return; }
            if (TraveledPastMaxRange(_traveled, _maxDistance)) { DespawnHarmless(); return; }
        }

        /// <summary>The bolt's per-frame straight-line motion, extracted as a pure static function
        /// (same reasoning as <see cref="HomingMissile.HasRunDry"/>/<c>BlockedByGeometry</c>) so a test
        /// can prove the flight direction never changes across repeated steps without having to drive a
        /// live <see cref="MonoBehaviour.Update"/>.</summary>
        public static Vector3 Step(Vector3 position, Vector3 direction, float speed, float dt) =>
            position + direction * (speed * dt);

        /// <summary>Whether the bolt has flown far enough past its max fire range to give up — pure, so
        /// the despawn threshold is testable without a scene or a clock.</summary>
        public static bool TraveledPastMaxRange(float traveled, float maxDistance) => traveled >= maxDistance;

        /// <summary>Whether the bolt is close enough (horizontally) to count as a hit — pure, extracted
        /// from <see cref="Update"/> the same reason <see cref="TraveledPastMaxRange"/> and
        /// <see cref="HomingMissile.HasRunDry"/> are: testable without driving a live instance's
        /// per-frame Update, which the collider-stripped, manual-proximity-check idiom this class shares
        /// with <see cref="HomingMissile"/> already never used physics for anyway.</summary>
        public static bool WithinHitRadius(Vector3 boltPosition, Vector3 targetPosition, float hitRadius)
        {
            float dx = boltPosition.x - targetPosition.x, dz = boltPosition.z - targetPosition.z;
            return dx * dx + dz * dz <= hitRadius * hitRadius;
        }

        /// <summary>The hit amount (AC1): 5% of the player's OWN max health, resolved here rather than
        /// carried as a number since <see cref="Fire"/> — pure, so a test can prove this scales with
        /// whatever <see cref="PlayerHealth.Max"/> resolves to instead of ever reading a hardcoded 10.</summary>
        public static float DamageFor(float playerMaxHealth) =>
            Mathf.Max(0f, playerMaxHealth) * DamagePercentOfMaxHealth;

        /// <summary>A hit: damage is resolved live off the player's OWN max health (AC1) via
        /// <see cref="DamageFor"/>, and only ever applied to a <see cref="PlayerHealth"/> receiver, so a
        /// bolt can never damage a robot, a shed or a Sentinel even though all three can implement
        /// <see cref="IDamageable"/> under the same <c>Team.Player</c> the friendly-fire rule alone would
        /// let through.</summary>
        private void Detonate()
        {
            if (_targetDamageable is PlayerHealth player && player.IsAlive)
            {
                player.TakeDamage(new DamageInfo(
                    DamageFor(player.Max), transform.position, _direction, Team.Enemy));
            }

            Destroy(gameObject);
        }

        private void DespawnHarmless() => Destroy(gameObject);
    }
}
