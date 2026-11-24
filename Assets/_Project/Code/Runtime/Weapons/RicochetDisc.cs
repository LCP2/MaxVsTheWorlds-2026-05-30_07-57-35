using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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
        private float _robotDamage;
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

        /// <summary>MV-1197: this disc's own first and second bounce CONTACT points (not the
        /// post-bounce offset position <see cref="Tick"/> moves to afterward) -- a resolved value an
        /// EditMode test reads to prove the aim preview's own prediction
        /// (<see cref="ComputePreviewLegs"/>) matches what this disc actually does, both running the
        /// exact same hit/bounce code.</summary>
        public Vector3 FirstBouncePoint { get; private set; }
        public Vector3 SecondBouncePoint { get; private set; }

        /// <summary>Fire one disc. Reuses the first inactive pooled instance, or builds a new one while
        /// under <see cref="MaxDiscsInFlight"/> — returns null, spawning nothing, once the pool is
        /// already full of live discs (design v5: "24 discs in flight" is a hard cap, not a queue).</summary>
        public static RicochetDisc Spawn(Vector3 origin, Vector3 direction, float speed, int bounces,
            int powerLevel, int aimLevel,
            float damage, float bankBonusPerBounce, float turnRateDegPerSec, float lookaheadMetres,
            float radius, float lifetime, float robotDamage = -1f)
        {
            PruneDestroyedPoolEntries();

            RicochetDisc disc = null;
            for (int i = 0; i < s_pool.Count; i++)
            {
                if (!s_pool[i]._active) { disc = s_pool[i]; break; }
            }

            if (disc == null)
            {
                if (s_pool.Count >= MaxDiscsInFlight) return null;
                var go = new GameObject("RicochetDisc (stand-in)");
                Parts parts = BuildVisual(go.transform);
                disc = go.AddComponent<RicochetDisc>();
                disc._trail = parts.Trail;
                disc._bodyRenderer = parts.BodyRenderer;
                disc._ringVisual = parts.Ring;
                disc._ringRenderer = parts.RingRenderer;
                disc._ringFilter = parts.RingFilter;
                disc._coreVisual = parts.Core;
                disc._blueHalo = parts.BlueHalo;
                disc._redHalo = parts.RedHalo;
                disc._embers = parts.Embers;
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
            disc._robotDamage = robotDamage >= 0f ? robotDamage : damage;
            disc._bankBonusPerBounce = bankBonusPerBounce;
            disc._turnRateDegPerSec = turnRateDegPerSec;
            disc._lookaheadMetres = lookaheadMetres;
            disc._radius = radius;
            disc._lifetime = lifetime;
            disc._age = 0f;
            disc._active = true;
            disc.FirstBouncePoint = Vector3.zero;
            disc.SecondBouncePoint = Vector3.zero;
            // Spec: "Trails are cleared on reuse" -- cleared here, at the NEXT spawn, not at retire.
            if (disc._trail != null) disc._trail.Clear();

            // MV-1414: the look resolves off POWER alone; re-applied every Spawn since a pooled disc
            // can carry a different POWER on every reuse. aimLevel no longer changes the look.
            disc.PowerLevel = Mathf.Clamp(powerLevel, 1, MaxPowerLevel);
            disc.VisualDiameter = DiameterForPower(disc.PowerLevel);
            disc.ApplyPowerVisual();
            return disc;
        }

        /// <summary>MV-1195 spec: "Freeze, death or resume: discs in flight are cleared." Deactivates
        /// every pooled disc without destroying the pool itself — the next <see cref="Spawn"/> reuses
        /// them exactly as if they had expired normally.
        ///
        /// MV-1331: must never throw. A scene reload (quit-to-menu, cross-world RESUME) destroys every
        /// disc's GameObject without going through <see cref="Retire"/> first, so a stale pool entry
        /// must be dropped (see <see cref="PruneDestroyedPoolEntries"/>) rather than touched.</summary>
        public static void ClearAll()
        {
            PruneDestroyedPoolEntries();
            for (int i = 0; i < s_pool.Count; i++)
                if (s_pool[i]._active) s_pool[i].Retire();
        }

        /// <summary>MV-1331: a scene reload (<c>RunFlow</c> quit-to-menu, cross-world RESUME — both call
        /// <c>SceneManager.LoadScene</c>) destroys every pooled disc's GameObject, but nothing else ever
        /// removed the stale entry from <see cref="s_pool"/>: the discs are ordinary scene objects, not
        /// <c>DontDestroyOnLoad</c>. The next <see cref="Spawn"/>/<see cref="ClearAll"/> would then touch
        /// a destroyed Unity Object and throw <c>MissingReferenceException</c>. Unity's own <c>== null</c>
        /// check is the only way to detect "destroyed but the C# wrapper is still alive" — a plain field
        /// read (like <c>_active</c>) would not catch it.</summary>
        private static void PruneDestroyedPoolEntries()
        {
            for (int i = s_pool.Count - 1; i >= 0; i--)
                if (s_pool[i] == null) s_pool.RemoveAt(i);
        }

        /// <summary>MV-1331: registered once (the empty unsubscribe-then-subscribe guards a re-run in the
        /// same domain, e.g. between EditMode tests) so a scene unload empties the pool immediately
        /// rather than waiting for the next <see cref="Spawn"/>/<see cref="ClearAll"/> to prune it lazily.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterSceneUnloadHook()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private static void OnSceneUnloaded(Scene scene) => s_pool.Clear();

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
            Flicker(dt);

            // MV-1425: the first leg is dead straight; AIMING only steers once the disc has banked.
            if (_turnRateDegPerSec > 0f && _bouncesTaken >= 1) Steer(dt);

            Vector3 from = transform.position;
            Vector3 next = from + _direction * (_speed * dt);

            if (TryFindContact(from, next, out RaycastHit contact))
            {
                transform.position = contact.point;

                if (TryResolveDamageable(contact.collider, out IDamageable target) &&
                    target.IsAlive && IsDiscTarget(target))
                {
                    if (target is RobotEnemy robot && robot.IsDormant) robot.Activate();
                    float multiplier = 1f + _bankBonusPerBounce * _bouncesTaken;
                    target.TakeDamage(new DamageInfo((target is RobotEnemy ? _robotDamage : _damage) * multiplier, contact.point, _direction,
                        Team.Player, source: DamageSource.PrimaryWeapon));
                }

                if (_bouncesRemaining > 0)
                {
                    _bouncesRemaining--;
                    _bouncesTaken++;
                    if (_bouncesTaken == 1) FirstBouncePoint = contact.point;
                    else if (_bouncesTaken == 2) SecondBouncePoint = contact.point;
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

        /// <summary>Public (MV-1197): also the aim preview's own target-marker selector
        /// (<see cref="MaxWorlds.VFX.RicochetAimPreviewVfx"/>) -- the SAME "which robot will this bend
        /// toward" logic <see cref="Steer"/> uses for the live disc, not a second approximation.</summary>
        public static RobotEnemy FindSteerCandidate(Vector3 origin, Vector3 direction, float range)
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
        private bool TryFindContact(Vector3 from, Vector3 to, out RaycastHit contact) =>
            TryFindContactStatic(from, to, _radius, s_hits, out contact);

        /// <summary>MV-1203: a contact's <see cref="IDamageable"/> is usually just the hit collider's
        /// own component, but an <see cref="AreaGate"/>'s threshold collider (its Cover-layer stand-in,
        /// MV-386) carries no <c>IDamageable</c> of its own — it is a SIBLING of the gate, not a child,
        /// so <c>GetComponentInParent</c> from the threshold could never reach it either. Falling back
        /// to <see cref="AreaGateThresholdLink"/> is what lets a disc that bounces off a closed gate's
        /// threshold still damage the gate it just bounced off, instead of bouncing for free.</summary>
        /// <summary>Internal (MV-1210): also <see cref="LightMissileBall"/>'s own gate fallback for its
        /// hit test — the same AreaGateThresholdLink sibling lookup, never a second copy of it.</summary>
        internal static bool TryResolveDamageable(Collider collider, out IDamageable target)
        {
            if (collider.TryGetComponent(out target)) return true;
            if (collider.TryGetComponent(out AreaGateThresholdLink link) && link.Gate != null)
            {
                target = link.Gate;
                return true;
            }
            return false;
        }

        /// <summary>MV-1395: what a disc damages and stops on -- every Team.Enemy damageable, plus the
        /// Team.Neutral Canister and DestructibleCover (neutral by design, so the old Enemy-only test
        /// let the disc fly through a canister and bounce off cover without hurting it).</summary>
        private static bool IsDiscTarget(IDamageable d) =>
            d.Team == Team.Enemy || d is MaxWorlds.Factories.Canister || d is MaxWorlds.Factories.DestructibleCover;

        /// <summary>The real hit query, pulled out static and buffer-parameterised (MV-1197) so the
        /// aim preview can run the EXACT same swept-sphere/filter code against its OWN buffer
        /// (<see cref="s_previewHits"/>) without touching a live disc's <see cref="s_hits"/> -- the
        /// preview runs every frame regardless of whether a disc is actually in flight.</summary>
        private static bool TryFindContactStatic(Vector3 from, Vector3 to, float radius,
            RaycastHit[] buffer, out RaycastHit contact)
        {
            contact = default;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-5f) return false;
            Vector3 dir = delta / dist;

            int count = Physics.SphereCastNonAlloc(from, radius, dir, buffer, dist, ~0,
                QueryTriggerInteraction.Ignore);

            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit rh = buffer[i];
                bool isCover = CoverLayer.Exists && rh.collider.gameObject.layer == CoverLayer.Index;
                bool isEnemy = !isCover && rh.collider.TryGetComponent(out IDamageable d) &&
                               d.IsAlive && IsDiscTarget(d);
                if (!isCover && !isEnemy) continue;

                if (rh.distance < bestDist) { bestDist = rh.distance; contact = rh; found = true; }
            }

            return found;
        }

        private static readonly RaycastHit[] s_previewHits = new RaycastHit[16];

        /// <summary>MV-1197 (design v5 §3.1): the aim preview's own bounce geometry -- pure, no side
        /// effects, reusing the EXACT <see cref="TryFindContactStatic"/>/<see cref="ReflectHorizontal"/>
        /// code <see cref="Tick"/>'s own bounce branch uses, never a separate approximation.
        ///
        /// <paramref name="pointsOut"/> must have length &gt;= 3 (caller-owned, reused every call -- no
        /// per-frame allocation here). <c>pointsOut[0]</c> is always <paramref name="origin"/>. Returns
        /// the total point count written (2 if the first leg found nothing, 3 otherwise).
        /// <paramref name="bounceCount"/> reports how many of the remaining points are REAL collisions
        /// (0, 1 or 2) rather than a distance-capped "where it would end" point -- a bounce ring is only
        /// correct at a real contact.</summary>
        public static int ComputePreviewLegs(Vector3 origin, Vector3 direction, float radius,
            float maxLegDistance, Vector3[] pointsOut, out int bounceCount)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 1e-4f) direction = Vector3.forward;
            direction.Normalize();

            pointsOut[0] = origin;
            bounceCount = 0;
            Vector3 pos = origin;
            Vector3 dir = direction;

            for (int leg = 0; leg < 2; leg++)
            {
                Vector3 to = pos + dir * maxLegDistance;
                if (TryFindContactStatic(pos, to, radius, s_previewHits, out RaycastHit contact))
                {
                    pointsOut[leg + 1] = contact.point;
                    bounceCount++;

                    // Same clearance-offset idiom as Tick's own bounce branch (see its comment): offset
                    // the NEXT leg's origin along the hit's own normal, not the new travel direction, or
                    // a shallow-angle bounce can leave the sphere still overlapping the surface it just
                    // left.
                    Vector3 priorDirection = dir;
                    dir = ReflectHorizontal(dir, contact.normal);
                    Vector3 clearNormal = contact.normal; clearNormal.y = 0f;
                    if (clearNormal.sqrMagnitude < 1e-6f) clearNormal = -priorDirection;
                    else clearNormal.Normalize();
                    pos = contact.point + clearNormal * (radius + BounceEpsilon);
                }
                else
                {
                    pointsOut[leg + 1] = to;
                    return leg + 2;
                }
            }

            return 3;
        }

        private void Retire()
        {
            _active = false;
            gameObject.SetActive(false);
        }

        // ---- MV-1414: the look. ONE shape at every POWER (a body, a flame ring, a core, two halos, a
        // trail, embers); it changes by LAYERS keyed to t = (POWER - 1) / 7, never by swapping a part
        // in or out at a threshold and never by averaging two colours (blue + orange averaged is grey).

        /// <summary>The full-strength blue body (POWER 1 is 100% this).</summary>
        private static readonly Color DiscColor = new Color(0.35f, 0.65f, 1.0f);
        /// <summary>The flame ring on the rim, and the front of the trail.</summary>
        private static readonly Color FlameOrange = new Color(1.0f, 0.43f, 0.0f);
        /// <summary>The flame halo, and the tail of the fire part of the trail.</summary>
        private static readonly Color FlameRed = new Color(0.9f, 0.22f, 0.21f);
        private static readonly Color CoreBlueWhite = new Color(0.85f, 0.93f, 1.0f);
        private static readonly Color CoreWhiteHot = new Color(1.0f, 1.0f, 0.95f);

        public const int MaxPowerLevel = 8;
        private const float MinDiscDiameter = 0.40f;
        private const float MaxDiscDiameter = 0.55f;
        private const float DiscThickness = 0.06f;
        private const float MinTrailSeconds = 0.15f;
        private const float MaxTrailSeconds = 0.30f;
        private const int MaxEmbers = 8;
        private const int RingSegments = 40;

        private static readonly Quaternion DiscPlaneRotation = Quaternion.Euler(90f, 0f, 0f);

        private MeshRenderer _bodyRenderer;
        private Transform _ringVisual;
        private MeshRenderer _ringRenderer;
        private MeshFilter _ringFilter;
        private Mesh _ringMesh;
        private Transform _coreVisual;
        private Transform _blueHalo;
        private Transform _redHalo;
        private Transform[] _embers;
        private float _flickerSeed;
        private float _spinDeg;

        /// <summary>POWER's position along 0..1: 0 at POWER 1, 1 at POWER 8.</summary>
        public static float PowerT(int powerLevel) =>
            Mathf.Clamp01((Mathf.Clamp(powerLevel, 1, MaxPowerLevel) - 1) / (float)(MaxPowerLevel - 1));

        public static float DiameterForPower(int powerLevel) =>
            Mathf.Lerp(MinDiscDiameter, MaxDiscDiameter, PowerT(powerLevel));

        public static float TrailSecondsForPower(int powerLevel) =>
            Mathf.Lerp(MinTrailSeconds, MaxTrailSeconds, PowerT(powerLevel));

        public static int EmberCountForPower(int powerLevel) =>
            Mathf.RoundToInt(MaxEmbers * PowerT(powerLevel));

        /// <summary>This disc's on-screen diameter in metres, resolved from POWER.</summary>
        public float VisualDiameter { get; private set; }

        /// <summary>The POWER this shot was fired at.</summary>
        public int PowerLevel { get; private set; }

        public int EmberCount { get; private set; }

        /// <summary>Opacity of the blue glow (1 - t) and the red flame halo (t).</summary>
        public float BlueHaloOpacity { get; private set; }
        public float FlameHaloOpacity { get; private set; }

        /// <summary>Whether the orange flame ring is shown at all (absent at POWER 1).</summary>
        public bool FlameRingVisible => _ringVisual != null && _ringVisual.gameObject.activeSelf;

        /// <summary>The flame ring's RESOLVED inner radius in the disc's own local plane, read back off
        /// the ring mesh's vertices (not the authored number it was built from). Equals the outer radius
        /// when the ring is absent.</summary>
        public float FlameRingInnerRadius
        {
            get
            {
                if (!FlameRingVisible || _ringMesh == null) return VisualDiameter * 0.5f;
                float min = float.MaxValue;
                foreach (Vector3 v in _ringMesh.vertices) min = Mathf.Min(min, new Vector2(v.x, v.y).magnitude);
                return min;
            }
        }

        /// <summary>The flame ring's RESOLVED outer radius, read back off the mesh.</summary>
        public float FlameRingOuterRadius
        {
            get
            {
                if (!FlameRingVisible || _ringMesh == null) return VisualDiameter * 0.5f;
                float max = 0f;
                foreach (Vector3 v in _ringMesh.vertices) max = Mathf.Max(max, new Vector2(v.x, v.y).magnitude);
                return max;
            }
        }

        public Color BodyColor => ColorOf(_bodyRenderer);
        public Color FlameRingColor => ColorOf(_ringRenderer);
        public float TrailTime => _trail != null ? _trail.time : 0f;

        private static Color ColorOf(MeshRenderer mr)
        {
            if (mr == null || mr.sharedMaterial == null) return Color.clear;
            return mr.sharedMaterial.HasProperty("_BaseColor")
                ? mr.sharedMaterial.GetColor("_BaseColor") : mr.sharedMaterial.color;
        }

        private sealed class Parts
        {
            public Transform Body, Ring, Core, BlueHalo, RedHalo;
            public MeshRenderer BodyRenderer, RingRenderer;
            public MeshFilter RingFilter;
            public Transform[] Embers;
            public TrailRenderer Trail;
        }

        private static Parts BuildVisual(Transform parent)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();
            var parts = new Parts();

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Disc";
            StripCollider(body);
            body.transform.SetParent(parent, false);
            body.transform.localRotation = DiscPlaneRotation;
            parts.Body = body.transform;
            parts.BodyRenderer = body.GetComponent<MeshRenderer>();

            var ring = new GameObject("FlameRing");
            ring.transform.SetParent(parent, false);
            parts.RingFilter = ring.AddComponent<MeshFilter>();
            parts.RingRenderer = ring.AddComponent<MeshRenderer>();
            parts.RingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            parts.RingRenderer.receiveShadows = false;
            parts.Ring = ring.transform;

            parts.Core = BuildSphere(parent, "Core");
            parts.BlueHalo = BuildSphere(parent, "BlueHalo");
            parts.RedHalo = BuildSphere(parent, "RedHalo");
            parts.Embers = new Transform[MaxEmbers];
            for (int i = 0; i < MaxEmbers; i++) parts.Embers[i] = BuildSphere(parent, "Ember" + i);

            var trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Solid());
            trail.time = MinTrailSeconds;
            parts.Trail = trail;
            return parts;
        }

        private static Transform BuildSphere(Transform parent, string name)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            return go.transform;
        }

        private static void SetMaterial(Transform t, Material mat)
        {
            if (t == null || mat == null) return;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        /// <summary>An annulus (both faces) in the disc's local XY plane. Rebuilt in place each Spawn (a
        /// pooled disc carries a different POWER every shot), so the ring's inner radius is real
        /// geometry, not a scale trick.</summary>
        private static void FillRingMesh(Mesh mesh, float inner, float outer)
        {
            var verts = new Vector3[RingSegments * 2];
            var tris = new int[RingSegments * 12];
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * Mathf.PI * 2f / RingSegments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i * 2] = new Vector3(c * inner, s * inner, 0f);
                verts[i * 2 + 1] = new Vector3(c * outer, s * outer, 0f);
                int n = (i + 1) % RingSegments;
                int i0 = i * 2, o0 = i * 2 + 1, i1 = n * 2, o1 = n * 2 + 1;
                int k = i * 12;
                tris[k] = i0; tris[k + 1] = o0; tris[k + 2] = o1;
                tris[k + 3] = i0; tris[k + 4] = o1; tris[k + 5] = i1;
                tris[k + 6] = i0; tris[k + 7] = o1; tris[k + 8] = o0;
                tris[k + 9] = i0; tris[k + 10] = i1; tris[k + 11] = o1;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
        }

        /// <summary>The look at this shot's POWER. Every value is a linear function of
        /// t = (POWER - 1) / 7, so each level up is a small step and no level adds or swaps a part.
        /// Re-applied every <see cref="Spawn"/>.</summary>
        private void ApplyPowerVisual()
        {
            float t = PowerT(PowerLevel);
            float d = VisualDiameter;
            float r = d * 0.5f;

            // Body: the full-strength blue disc, shrinking to the ring's inner radius so the orange ring
            // takes over from the rim inward (blue and orange never overlap, so never mix).
            float innerR = r * (1f - t);
            if (_bodyRenderer != null)
            {
                Transform bt = _bodyRenderer.transform;
                bt.gameObject.SetActive(innerR > 1e-3f);
                bt.localScale = new Vector3(innerR * 2f, DiscThickness * 0.5f, innerR * 2f);
                SetMaterial(bt, VfxMaterials.AdditiveTinted(DiscColor));
            }

            bool showRing = t > 1e-4f;
            if (_ringVisual != null)
            {
                _ringVisual.gameObject.SetActive(showRing);
                if (showRing)
                {
                    if (_ringMesh == null)
                    {
                        _ringMesh = new Mesh { name = "RicochetFlameRing" };
                        _ringFilter.sharedMesh = _ringMesh;
                    }
                    FillRingMesh(_ringMesh, innerR, r);
                    SetMaterial(_ringVisual, VfxMaterials.AdditiveTinted(FlameOrange));
                }
            }

            // Core: pale blue-white at POWER 1-2, white-hot from POWER 3.
            if (_coreVisual != null)
            {
                _coreVisual.gameObject.SetActive(true);
                _coreVisual.localScale = Vector3.one * (d * 0.30f);
                SetMaterial(_coreVisual, VfxMaterials.AdditiveTinted(PowerLevel >= 3 ? CoreWhiteHot : CoreBlueWhite));
            }

            // Halos: the blue glow fades out as the red flame halo fades in. Additive, so opacity scales
            // a colour towards black, never towards the other hue.
            BlueHaloOpacity = 1f - t;
            FlameHaloOpacity = t;
            if (_blueHalo != null)
            {
                _blueHalo.gameObject.SetActive(BlueHaloOpacity > 1e-3f);
                _blueHalo.localScale = Vector3.one * (d * 1.35f);
                SetMaterial(_blueHalo, VfxMaterials.AdditiveTinted(DiscColor * (0.28f * BlueHaloOpacity)));
            }
            if (_redHalo != null)
            {
                _redHalo.gameObject.SetActive(FlameHaloOpacity > 1e-3f);
                SetMaterial(_redHalo, VfxMaterials.AdditiveTinted(FlameRed * (0.40f * FlameHaloOpacity)));
            }

            // Trail: longer with POWER; the front fraction t is fire (orange then red toward the tail),
            // the rest stays blue.
            if (_trail != null)
            {
                _trail.time = TrailSecondsForPower(PowerLevel);
                _trail.widthMultiplier = d * 0.6f;
                _trail.colorGradient = TrailGradient(t);
            }

            // Embers: 0 at POWER 1 up to 8 at POWER 8.
            EmberCount = EmberCountForPower(PowerLevel);
            Material emberMat = VfxMaterials.AdditiveTinted(FlameOrange);
            for (int i = 0; i < _embers.Length; i++)
            {
                Transform e = _embers[i];
                bool on = i < EmberCount;
                e.gameObject.SetActive(on);
                if (!on) continue;
                e.localScale = Vector3.one * 0.035f;
                SetMaterial(e, emberMat);
            }
            _flickerSeed = Random.value * 10f;
            Flicker(0f);
        }

        private static Gradient TrailGradient(float t)
        {
            var g = new Gradient();
            var alpha = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) };
            if (t <= 1e-4f)
            {
                g.SetKeys(new[] { new GradientColorKey(DiscColor, 0f), new GradientColorKey(DiscColor, 1f) }, alpha);
                return g;
            }
            // Fire (orange -> red) over [0, t], then a short step into the blue remainder.
            var keys = new List<GradientColorKey>
            {
                new GradientColorKey(FlameOrange, 0f),
                new GradientColorKey(FlameRed, t),
            };
            float step = Mathf.Min(1f, t + 0.04f);
            if (t < 1f)
            {
                keys.Add(new GradientColorKey(DiscColor, step));
                if (step < 1f) keys.Add(new GradientColorKey(DiscColor, 1f));
            }
            g.SetKeys(keys.ToArray(), alpha);
            return g;
        }

        /// <summary>The disc spins; the flame flickers slightly (red halo size) and the embers jitter
        /// behind it.</summary>
        private void Flicker(float dt)
        {
            _spinDeg = (_spinDeg + 360f * dt) % 360f;
            if (_bodyRenderer != null)
                _bodyRenderer.transform.localRotation = DiscPlaneRotation * Quaternion.Euler(0f, _spinDeg, 0f);
            if (_ringVisual != null) _ringVisual.localRotation = Quaternion.Euler(0f, 0f, _spinDeg);

            float time = _age + _flickerSeed;
            float f = 1f + 0.06f * Mathf.Sin(time * 31f) + 0.03f * Mathf.Sin(time * 53f);
            float d = VisualDiameter;
            float t = PowerT(PowerLevel);
            if (_redHalo != null && _redHalo.gameObject.activeSelf)
                _redHalo.localScale = Vector3.one * (d * Mathf.Lerp(1.3f, 2.0f, t) * f);
            if (_embers == null) return;
            for (int i = 0; i < EmberCount; i++)
            {
                float ph = time * 6f + i * 2.399f;
                float back = 0.18f + 0.07f * i + 0.05f * Mathf.Sin(ph);
                float side = d * 0.5f * Mathf.Sin(i * 2.399f + time * 3f);
                float up = d * 0.5f * Mathf.Cos(i * 1.7f + time * 2.3f);
                _embers[i].localPosition = new Vector3(side, up, -back);
            }
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
