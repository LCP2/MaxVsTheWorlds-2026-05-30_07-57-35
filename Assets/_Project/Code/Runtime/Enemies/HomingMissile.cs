using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// The Bomber's slow homing missile (MV-293). Free-flying — not pooled, not parented to the
    /// spawner — because it's short-lived and self-destroys on impact or timeout, the same "one and
    /// done" lifetime as the death VFX it plays a similar role to.
    ///
    /// Homing is deliberately GENTLE (<see cref="TurnRateDegPerSec"/>): a player who is actually
    /// moving can juke it, which is the whole point of a "slow homing" threat over a hitscan one — it
    /// pressures you into not standing still, it doesn't guarantee a hit the way a perfectly-tracking
    /// projectile would.
    /// </summary>
    public sealed class HomingMissile : MonoBehaviour
    {
        private const float TurnRateDegPerSec = 90f;
        private const float ContactRadius = 0.5f;
        private const float MaxLifetime = 6f;

        private Transform _target;
        private IDamageable _targetDamageable;
        private float _speed;
        private float _damage;
        private float _splashRadius;
        private float _age;
        private bool _detonated;

        /// <summary>Launch one missile from <paramref name="origin"/> toward <paramref name="target"/>.
        /// <paramref name="damage"/>/<paramref name="splashRadius"/> come straight off the Bomber's
        /// <see cref="EnemyArchetype.ContactDamage"/>/<see cref="EnemyArchetype.ContactRadius"/> — same
        /// numbers, ranged-attack meaning.</summary>
        public static HomingMissile Fire(Vector3 origin, Transform target, float speed, float damage,
            float splashRadius)
        {
            var go = new GameObject("HomingMissile (stand-in)");
            go.transform.position = origin;

            Vector3 aim = target != null ? target.position - origin : Vector3.forward;
            aim.y = 0f;
            if (aim.sqrMagnitude < 1e-4f) aim = Vector3.forward;
            go.transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);

            BuildVisual(go.transform);

            var missile = go.AddComponent<HomingMissile>();
            missile.Init(target, speed, damage, splashRadius);
            return missile;
        }

        /// <summary>Gunmetal shaft — the same "painted steel" family the rest of the swarm's hardware
        /// wears, not a saturated hue that would compete with the Bomber's own body colour.</summary>
        private static readonly Color ShaftColor = new Color(0.30f, 0.31f, 0.34f);

        /// <summary>The tail fins and warhead band — the game's one warn colour (see
        /// <see cref="MaxWorlds.VFX.RobotRig"/>'s EyeWarn/EyeWarn-alike), so ordnance in flight reads
        /// the same "incoming" language as every telegraph in the game.</summary>
        private static readonly Color WarnColor = new Color(1f, 0.35f, 0.12f);

        /// <summary>
        /// A slim missile — shaft, tail fins, a warhead band — replacing the plain sphere "ball" this
        /// used to fire (MV-329's AC2). The shaft is a Capsule rotated onto the object's own forward
        /// axis, so it always points the way it's flying without any per-frame work: <see cref="Update"/>
        /// already keeps <c>transform.rotation</c> aimed along the flight path, and everything built here
        /// is a child in that same local space.
        /// </summary>
        private static void BuildVisual(Transform parent)
        {
            Material shaftMat = MaterialLibrary.Tinted(SurfaceKind.Metal, ShaftColor);
            Material warnMat = MaterialLibrary.Tinted(SurfaceKind.Metal, WarnColor);

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shaft.name = "Shaft";
            Strip(shaft);
            shaft.transform.SetParent(parent, false);
            // The capsule's long axis is local Y; rotating 90° about X lays it onto local Z, which is
            // this object's forward — the same trick every beam/tube part in the game uses to point a
            // cylinder primitive down its own travel direction.
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.transform.localScale = new Vector3(0.11f, 0.30f, 0.11f);
            if (shaftMat != null) shaft.GetComponent<MeshRenderer>().sharedMaterial = shaftMat;

            // A warhead band at the nose — the AC's "reads as ordnance", not just "reads as a stick".
            var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "WarheadBand";
            Strip(band);
            band.transform.SetParent(parent, false);
            band.transform.localPosition = new Vector3(0f, 0f, 0.34f);
            band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            band.transform.localScale = new Vector3(0.13f, 0.05f, 0.13f);
            if (warnMat != null) band.GetComponent<MeshRenderer>().sharedMaterial = warnMat;

            // Tail fins — two flat vanes at the back, the part of the silhouette that says "missile"
            // rather than "dropped tool" even at gameplay zoom.
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fin.name = "Fin";
                Strip(fin);
                fin.transform.SetParent(parent, false);
                fin.transform.localPosition = new Vector3(side * 0.10f, 0f, -0.26f);
                fin.transform.localScale = new Vector3(0.02f, 0.16f, 0.12f);
                if (shaftMat != null) fin.GetComponent<MeshRenderer>().sharedMaterial = shaftMat;
            }
        }

        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col); // manual proximity check in Update, not physics
        }

        private void Init(Transform target, float speed, float damage, float splashRadius)
        {
            _target = target;
            _targetDamageable = target != null ? target.GetComponent<IDamageable>() : null;
            _speed = speed;
            _damage = damage;
            _splashRadius = splashRadius;
        }

        private void Update()
        {
            if (_detonated) return;
            float dt = Time.deltaTime;
            _age += dt;

            if (_target != null)
            {
                Vector3 to = _target.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 1e-4f)
                {
                    Quaternion wanted = Quaternion.LookRotation(to.normalized, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted,
                        TurnRateDegPerSec * dt);
                }
            }

            transform.position += transform.forward * (_speed * dt);

            bool closeEnough = _target != null &&
                (transform.position - _target.position).sqrMagnitude <= ContactRadius * ContactRadius;
            if (closeEnough || _age >= MaxLifetime) Detonate();
        }

        private void Detonate()
        {
            _detonated = true;

            if (_targetDamageable != null && _targetDamageable.IsAlive && _target != null &&
                (transform.position - _target.position).magnitude <= _splashRadius)
            {
                _targetDamageable.TakeDamage(
                    new DamageInfo(_damage, transform.position, transform.forward, Team.Enemy));
            }

            Destroy(gameObject);
        }
    }
}
