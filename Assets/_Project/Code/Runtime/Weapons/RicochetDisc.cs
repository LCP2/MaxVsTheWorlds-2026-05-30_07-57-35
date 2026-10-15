using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// RICOCHET's own disc (MV-1195, design v5 §3.1) — a pooled, bouncing projectile fired by
    /// <see cref="MaxWorlds.Combat.Ricochet"/>. Unlike <see cref="SeekerPulse"/>/<see cref="PlayerRocket"/>
    /// (fire-and-forget, created/destroyed per shot), a disc is reused from a fixed pool capped at
    /// <see cref="MaxDiscsInFlight"/> — "24 discs in flight" is a budget on live instances, not a
    /// one-shot allocation limit. A retired disc deactivates (<see cref="gameObject"/> disabled) rather
    /// than destroying, and its trail clears again on its NEXT <see cref="Spawn"/>, not at retire time
    /// (spec: "trails are cleared on reuse").
    ///
    /// Self-ticks via <see cref="Update"/> (<see cref="MaxWorlds.Core.DevMode"/> aside, same as every
    /// other free-flying projectile here) but exposes <see cref="Tick"/> publicly with an explicit
    /// <c>dt</c> so an EditMode test can drive a fired disc to its bounces/contacts deterministically —
    /// the same seam <see cref="BolterBolt"/>/<see cref="SeekerPulse"/> already carry.
    /// </summary>
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class RicochetDisc : MonoBehaviour
    {
        public const int MaxDiscsInFlight = 24;

        /// <summary>A small forward nudge after a bounce so the disc doesn't immediately re-collide
        /// with the same collider it just reflected off, the same contact-point-plus-epsilon idiom
        /// <see cref="MaxWorlds.Enemies.RobotEnemy.TickConversion"/>-adjacent code uses elsewhere for a
        /// reflection/contact step.</summary>
        private const float BounceEpsilon = 0.02f;

        /// <summary>How wide a cone ahead AIMING searches for a robot to turn toward (design v5: "turns
        /// toward the nearest robot ahead" — a generous cone, not a tight lock, since the disc is
        /// already travelling roughly at a robot to begin with).</summary>
        private const float AimConeDegrees = 60f;

        private static readonly List<RicochetDisc> s_pool = new List<RicochetDisc>(MaxDiscsInFlight);

        /// <summary>Every pooled disc instance, active or not — test-only visibility into the pool's
        /// own size/reuse, the same "expose the registry" shape <see cref="RobotEnemy.Active"/> gives
        /// a test.</summary>
        public static IReadOnlyList<RicochetDisc> Pool => s_pool;

        private bool _active;
        private Vector3 _direction;
        private float _speed;
        private int _bouncesRemaining;
        private int _bouncesTaken;
        private float _damage;
        private float _bankBonusPerBounce;
        private float _turnRateDegPerSec;
        private float _lookaheadMetres;
        private float _radius;
        private float _lifetime;
        private float _age;
        private TrailRenderer _trail;

        private static readonly RaycastHit[] s_hits = new RaycastHit[16];

        /// <summary>Whether this pooled instance is currently a live, flying disc.</summary>
        public bool IsActiveDisc => _active;

        /// <summary>How many bounces this disc has already taken — what the bank bonus (design v5:
        /// "+20% damage per bounce already taken") is read against, and what an EditMode test asserts
        /// to prove the bounce count itself.</summary>
        public int BouncesTaken => _bouncesTaken;

        /// <summary>Fire one disc. Reuses the first inactive pooled instance, or builds a new one while
        /// under <see cref="MaxDiscsInFlight"/> — returns null, spawning nothing, once the pool is
        /// already full of live discs (design v5: "24 discs in flight" is a hard cap, not a queue).</summary>
        public static RicochetDisc Spawn(Vector3 origin, Vector3 direction, float speed, int bounces,
            float damage, float bankBonusPerBounce, float turnRateDegPerSec, float lookaheadMetres,
            float radius, float lifetime)
        {
            RicochetDisc disc = null;
            for (int i = 0; i < s_pool.Count; i++)
            {
                if (!s_pool[i]._active) { disc = s_pool[i]; break; }
            }

            if (disc == null)
            {
                if (s_pool.Count >= MaxDiscsInFlight) return null;
                var go = new GameObject("RicochetDisc (stand-in)");
                BuildVisual(go.transform, out TrailRenderer trail);
                disc = go.AddComponent<RicochetDisc>();
                disc._trail = trail;
                s_pool.Add(disc);
            }

            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.forward;
            direction.Normalize();

            disc.gameObject.SetActive(true);
            disc.transform.position = origin;
            disc.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            disc._direction = direction;
            disc._speed = speed;
            disc._bouncesRemaining = Mathf.Max(0, bounces);
            disc._bouncesTaken = 0;
            disc._damage = damage;
            disc._bankBonusPerBounce = bankBonusPerBounce;
            disc._turnRateDegPerSec = turnRateDegPerSec;
            disc._lookaheadMetres = lookaheadMetres;
            disc._radius = radius;
            disc._lifetime = lifetime;
            disc._age = 0f;
            disc._active = true;
            // Spec: "Trails are cleared on reuse" -- cleared here, at the NEXT spawn, not at retire.
            if (disc._trail != null) disc._trail.Clear();
            return disc;
        }

        /// <summary>MV-1195 spec: "Freeze, death or resume: discs in flight are cleared." Deactivates
        /// every pooled disc without destroying the pool itself — the next <see cref="Spawn"/> reuses
        /// them exactly as if they had expired normally.</summary>
        public static void ClearAll()
        {
            for (int i = 0; i < s_pool.Count; i++)
                if (s_pool[i]._active) s_pool[i].Retire();
        }

        /// <summary>Test-only teardown — destroys every pooled instance and empties the pool, so an
        /// EditMode test run doesn't leak GameObjects into whatever runs next in the same domain. Same
        /// "drop the cache so nothing holds a stale reference" idiom as
        /// <see cref="SeekerPulse.ResetForTests"/>.</summary>
        public static void DestroyPoolForTests()
        {
            for (int i = 0; i < s_pool.Count; i++)
                if (s_pool[i] != null) DestroyImmediate(s_pool[i].gameObject);
            s_pool.Clear();
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The real per-frame flight step — public so an EditMode test can drive a fired disc
        /// deterministically, the same seam every other projectile here carries.</summary>
        public void Tick(float dt)
        {
            if (!_active) return;
            _age += dt;

            if (_turnRateDegPerSec > 0f) Steer(dt);

            Vector3 from = transform.position;
            Vector3 next = from + _direction * (_speed * dt);

            if (TryFindContact(from, next, out RaycastHit contact))
            {
                transform.position = contact.point;

                if (contact.collider.TryGetComponent(out IDamageable target) &&
                    target.IsAlive && target.Team == Team.Enemy)
                {
                    if (target is RobotEnemy robot && robot.IsDormant) robot.Activate();
                    float multiplier = 1f + _bankBonusPerBounce * _bouncesTaken;
                    target.TakeDamage(new DamageInfo(_damage * multiplier, contact.point, _direction,
                        Team.Player, source: DamageSource.PrimaryWeapon));
                }

                if (_bouncesRemaining > 0)
                {
                    _bouncesRemaining--;
                    _bouncesTaken++;
                    Vector3 priorDirection = _direction;
                    _direction = ReflectHorizontal(_direction, contact.normal);
                    // Offset along the HIT'S OWN NORMAL (not the new travel direction) by just past the
                    // disc's own radius -- a shallow-angle bounce only advances a little along its own
                    // new heading per unit of clearance, so offsetting along the travel direction
                    // instead (as a plain "step forward" idiom would) can leave the sphere still
                    // overlapping the surface it just left, re-triggering the same contact next tick.
                    Vector3 clearNormal = contact.normal; clearNormal.y = 0f;
                    if (clearNormal.sqrMagnitude < 1e-6f) clearNormal = -priorDirection;
                    else clearNormal.Normalize();
                    transform.position = contact.point + clearNormal * (_radius + BounceEpsilon);
                    transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
                }
                else
                {
                    Retire();
                    return;
                }
            }
            else
            {
                transform.position = next;
            }

            if (_age >= _lifetime) Retire();
        }

        /// <summary>Reflects <paramref name="direction"/> off <paramref name="normal"/>, flattened to
        /// the horizontal plane every disc travels in — pure, so a test can prove a bounce's new
        /// heading without a live instance.</summary>
        public static Vector3 ReflectHorizontal(Vector3 direction, Vector3 normal)
        {
            normal.y = 0f;
            if (normal.sqrMagnitude < 1e-6f) return direction;
            normal.Normalize();
            Vector3 reflected = Vector3.Reflect(direction, normal);
            reflected.y = 0f;
            return reflected.sqrMagnitude < 1e-6f ? direction : reflected.normalized;
        }

        /// <summary>AIMING (design v5 §3.1): while it has any turn rate at all, bend toward the nearest
        /// alive enemy robot within <see cref="_lookaheadMetres"/> and <see cref="AimConeDegrees"/> of
        /// the disc's own heading, on the same combat level — the same "steer, don't teleport" shape
        /// <see cref="HomingSteering.TurnToward"/> gives every other seeking projectile here.</summary>
        private void Steer(float dt)
        {
            Vector3 origin = transform.position;
            RobotEnemy candidate = FindSteerCandidate(origin, _direction, _lookaheadMetres);
            if (candidate == null) return;

            Quaternion turned = HomingSteering.TurnToward(transform.rotation, origin,
                candidate.transform.position, _turnRateDegPerSec, dt);
            transform.rotation = turned;
            Vector3 dir = turned * Vector3.forward;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-6f) _direction = dir.normalized;
        }

        private static RobotEnemy FindSteerCandidate(Vector3 origin, Vector3 direction, float range)
        {
            var active = RobotEnemy.Active;
            MapData map = EnemyNavigation.Map;
            RobotEnemy best = null;
            float bestDistSq = float.MaxValue;
            float rangeSq = range * range;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (candidate == null || !candidate.IsAlive || candidate.IsDormant) continue;
                if (!CombatLevel.SameLevel(map, origin, candidate.transform.position)) continue;

                Vector3 to = candidate.transform.position - origin;
                to.y = 0f;
                float distSq = to.sqrMagnitude;
                if (distSq > rangeSq || distSq < 1e-6f) continue;

                float angle = Vector3.Angle(direction, to.normalized);
                if (angle > AimConeDegrees) continue;

                if (distSq < bestDistSq) { bestDistSq = distSq; best = candidate; }
            }

            return best;
        }

        /// <summary>The first thing this frame's segment meets, among anything on the Cover layer
        /// (walls/solid cover — design v5: "bounce off anything that blocks shots") or any alive
        /// Team.Enemy <see cref="IDamageable"/> (robots/gates/bosses). A multi-hit query, not a single
        /// blocking one, so a collider that is neither (wire fencing, glass, see-through cover — none of
        /// which are on the Cover layer and none of which carry a Team.Enemy IDamageable) is correctly
        /// passed straight through, the same "the first REAL hit wins, everything else is see-through"
        /// idiom <see cref="SeekerPulse.TryHitWorldDamageable"/> already uses.</summary>
        private bool TryFindContact(Vector3 from, Vector3 to, out RaycastHit contact)
        {
            contact = default;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-5f) return false;
            Vector3 dir = delta / dist;

            int count = Physics.SphereCastNonAlloc(from, _radius, dir, s_hits, dist, ~0,
                QueryTriggerInteraction.Ignore);

            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit rh = s_hits[i];
                bool isCover = CoverLayer.Exists && rh.collider.gameObject.layer == CoverLayer.Index;
                bool isEnemy = !isCover && rh.collider.TryGetComponent(out IDamageable d) &&
                               d.IsAlive && d.Team == Team.Enemy;
                if (!isCover && !isEnemy) continue;

                if (rh.distance < bestDist) { bestDist = rh.distance; contact = rh; found = true; }
            }

            return found;
        }

        private void Retire()
        {
            _active = false;
            gameObject.SetActive(false);
        }

        /// <summary>A flat, unlit disc 0.40 m across in disc blue, with a short trail (spec item 7 —
        /// the placeholder look; the real disc/launcher art is a follow-up ticket). Collider stripped,
        /// same idiom <see cref="BolterBolt"/> uses: this disc's own hit test is a manual
        /// <see cref="Physics.SphereCastNonAlloc"/>, never physics collision response.</summary>
        private static readonly Color DiscColor = new Color(0.35f, 0.65f, 1.0f);
        private const float DiscDiameter = 0.40f;
        private const float DiscThickness = 0.06f;
        private const float TrailSeconds = 0.15f;

        private static void BuildVisual(Transform parent, out TrailRenderer trail)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            StripCollider(disc);
            disc.transform.SetParent(parent, false);
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Unity's cylinder primitive is 2 units pole-to-pole and 1 unit across at scale 1.
            disc.transform.localScale = new Vector3(DiscDiameter, DiscThickness * 0.5f, DiscDiameter);
            Material discMat = VfxMaterials.AdditiveTinted(DiscColor);
            if (discMat != null) disc.GetComponent<MeshRenderer>().sharedMaterial = discMat;

            trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = TrailSeconds;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = DiscDiameter * 0.6f;
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Solid());
            Color trailColor = DiscColor;
            trail.startColor = trailColor;
            trailColor.a = 0f;
            trail.endColor = trailColor;
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
    }
}
