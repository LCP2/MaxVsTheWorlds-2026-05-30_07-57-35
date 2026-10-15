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
        /// <summary>MV-1196 (design v5 §3.1): POWER's four disc marks — Mark I (Power 1-2, a plain
        /// blue disc) through Mark IV (Power 7-8, a violet curved-blade star). <see cref="MarkForPowerLevel"/>
        /// derives this straight from <c>p_dmg</c>'s own level (rig_board.city.json: maxLevel 8, two
        /// levels per mark) rather than a second authored table.</summary>
        public enum DiscMark { I, II, III, IV }

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

        // ---- MV-1196: the look (design v5 §3.1) — built once per pooled instance in BuildVisual,
        // re-applied every Spawn (the mark/fin count can differ from the previous shot this same
        // pooled GameObject carried) and again on each bounce (the stud count drops as bounces spend).
        private Transform _discVisual;
        private Transform[] _rimTeeth;
        private Transform[] _blades;
        private Transform _ivCore;
        private Transform[] _studs;
        private Transform[] _fins;

        private static readonly RaycastHit[] s_hits = new RaycastHit[16];

        /// <summary>Whether this pooled instance is currently a live, flying disc.</summary>
        public bool IsActiveDisc => _active;

        /// <summary>How many bounces this disc has already taken — what the bank bonus (design v5:
        /// "+20% damage per bounce already taken") is read against, and what an EditMode test asserts
        /// to prove the bounce count itself.</summary>
        public int BouncesTaken => _bouncesTaken;

        /// <summary>MV-1196: POWER's resolved disc mark for this shot (see <see cref="DiscMark"/>).</summary>
        public DiscMark Mark { get; private set; }

        /// <summary>MV-1196: this disc's on-screen diameter in metres, resolved from <see cref="Mark"/>
        /// (<see cref="DiameterForMark"/>) — what an EditMode test measures against the design's
        /// 19px-at-Mark-I/36px-at-Mark-IV range.</summary>
        public float VisualDiameter { get; private set; }

        /// <summary>MV-1196: AIMING's own fin count for this shot, <c>p_aim</c>'s level directly
        /// (0 to 4, rig_board.city.json's own cap).</summary>
        public int FinCount { get; private set; }

        /// <summary>MV-1196: "one white stud on the disc for every bounce it has left" — exactly
        /// <see cref="_bouncesRemaining"/>, re-read live rather than cached so it drops the instant a
        /// bounce spends one.</summary>
        public int StudCount => _bouncesRemaining;

        /// <summary>MV-1197: this disc's own first and second bounce CONTACT points (not the
        /// post-bounce offset position <see cref="Tick"/> moves to afterward) -- a resolved value an
        /// EditMode test reads to prove the aim preview's own prediction
        /// (<see cref="ComputePreviewLegs"/>) matches what this disc actually does, both running the
        /// exact same hit/bounce code.</summary>
        public Vector3 FirstBouncePoint { get; private set; }
        public Vector3 SecondBouncePoint { get; private set; }

        /// <summary>MV-1196: POWER's own disc marks, two levels per mark (rig_board.city.json's
        /// <c>p_dmg</c>: maxLevel 8) — Mark I (1-2) through Mark IV (7-8). Pure, so a test can assert it
        /// without a live disc instance.</summary>
        public static DiscMark MarkForPowerLevel(int powerLevel) =>
            (DiscMark)Mathf.Clamp((Mathf.Max(1, powerLevel) - 1) / 2, 0, 3);

        /// <summary>MV-1196: a mark's on-screen diameter in metres — 0.40 m at Mark I to 0.75 m at Mark
        /// IV (design v5: 19px/36px at the play camera's ~48px/m), evenly stepped across the four marks.</summary>
        public static float DiameterForMark(DiscMark mark) =>
            Mathf.Lerp(MinDiscDiameter, MaxDiscDiameter, (int)mark / 3f);

        /// <summary>Fire one disc. Reuses the first inactive pooled instance, or builds a new one while
        /// under <see cref="MaxDiscsInFlight"/> — returns null, spawning nothing, once the pool is
        /// already full of live discs (design v5: "24 discs in flight" is a hard cap, not a queue).</summary>
        public static RicochetDisc Spawn(Vector3 origin, Vector3 direction, float speed, int bounces,
            int powerLevel, int aimLevel,
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
                BuildVisual(go.transform, out TrailRenderer trail, out Transform discVisual,
                    out Transform[] rimTeeth, out Transform[] blades, out Transform ivCore,
                    out Transform[] studs, out Transform[] fins);
                disc = go.AddComponent<RicochetDisc>();
                disc._trail = trail;
                disc._discVisual = discVisual;
                disc._rimTeeth = rimTeeth;
                disc._blades = blades;
                disc._ivCore = ivCore;
                disc._studs = studs;
                disc._fins = fins;
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
            disc.FirstBouncePoint = Vector3.zero;
            disc.SecondBouncePoint = Vector3.zero;
            // Spec: "Trails are cleared on reuse" -- cleared here, at the NEXT spawn, not at retire.
            if (disc._trail != null) disc._trail.Clear();

            // MV-1196: the look — a pooled disc can carry a different mark/fin count on every reuse,
            // so this has to re-apply every Spawn rather than build once.
            disc.Mark = MarkForPowerLevel(powerLevel);
            disc.VisualDiameter = DiameterForMark(disc.Mark);
            disc.FinCount = Mathf.Clamp(aimLevel, 0, MaxFinSlots);
            disc.ApplyMarkVisual();
            disc.ApplyFinVisual();
            disc.ApplyStudVisual();
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
                    if (_bouncesTaken == 1) FirstBouncePoint = contact.point;
                    else if (_bouncesTaken == 2) SecondBouncePoint = contact.point;
                    ApplyStudVisual();   // MV-1196: "the count drops as it bounces"
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
                               d.IsAlive && d.Team == Team.Enemy;
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

        /// <summary>A flat, unlit disc in disc blue, with a short trail — the base look every mark
        /// starts from (spec item 7). Collider stripped, same idiom <see cref="BolterBolt"/> uses: this
        /// disc's own hit test is a manual <see cref="Physics.SphereCastNonAlloc"/>, never physics
        /// collision response.</summary>
        private static readonly Color DiscColor = new Color(0.35f, 0.65f, 1.0f);

        /// <summary>MV-1196: Mark I's diameter — the design's 19px at ~48px/m.</summary>
        private const float MinDiscDiameter = 0.40f;

        /// <summary>MV-1196: Mark IV's diameter — the design's 36px at ~48px/m.</summary>
        private const float MaxDiscDiameter = 0.75f;

        private const float DiscThickness = 0.06f;
        private const float TrailSeconds = 0.15f;

        /// <summary>MV-1196: Mark II's cog rim / Mark III's saw edge share one ring of slots — the two
        /// marks never show at once, so one set of transforms re-dressed by <see cref="ApplyMarkVisual"/>
        /// avoids building (and the test having to reason about) two separate rings.</summary>
        private const int RimTeethCount = 8;

        private const int BladeCount = 4;

        /// <summary>MV-1196: RICOCHETS' own level cap (rig_board.city.json's <c>p_ric</c>: maxLevel 6) —
        /// the most studs a disc can ever need to show.</summary>
        private const int MaxStudSlots = 6;

        /// <summary>MV-1196: AIMING's own level cap (rig_board.city.json's <c>p_aim</c>: maxLevel 4).</summary>
        private const int MaxFinSlots = 4;

        /// <summary>The disc's own flat face is perpendicular to its direction of travel (+Z) — every
        /// rim decoration shares this same local rotation so it sits flush in that plane.</summary>
        private static readonly Quaternion DiscPlaneRotation = Quaternion.Euler(90f, 0f, 0f);

        private static readonly Color CogColor = new Color(0.90f, 0.90f, 0.94f);
        private static readonly Color SawColor = new Color(0.80f, 0.88f, 1.0f);
        private static readonly Color VioletColor = new Color(0.55f, 0.20f, 0.85f);
        private static readonly Color StudColor = new Color(0.95f, 0.95f, 0.98f);
        private static readonly Color FinColor = new Color(0.55f, 0.75f, 1.0f);

        private static void BuildVisual(Transform parent, out TrailRenderer trail, out Transform discVisual,
            out Transform[] rimTeeth, out Transform[] blades, out Transform ivCore,
            out Transform[] studs, out Transform[] fins)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Disc";
            StripCollider(disc);
            disc.transform.SetParent(parent, false);
            disc.transform.localRotation = DiscPlaneRotation;
            // Unity's cylinder primitive is 2 units pole-to-pole and 1 unit across at scale 1.
            disc.transform.localScale = new Vector3(MinDiscDiameter, DiscThickness * 0.5f, MinDiscDiameter);
            Material discMat = VfxMaterials.AdditiveTinted(DiscColor);
            if (discMat != null) disc.GetComponent<MeshRenderer>().sharedMaterial = discMat;
            discVisual = disc.transform;

            trail = parent.gameObject.AddComponent<TrailRenderer>();
            trail.time = TrailSeconds;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = MinDiscDiameter * 0.6f;
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Solid());
            Color trailColor = DiscColor;
            trail.startColor = trailColor;
            trailColor.a = 0f;
            trail.endColor = trailColor;

            rimTeeth = BuildSlots(parent, "Tooth", RimTeethCount, PrimitiveType.Cube);
            blades = BuildSlots(parent, "Blade", BladeCount, PrimitiveType.Cube);
            ivCore = BuildSlot(parent, "IvCore", PrimitiveType.Sphere);
            studs = BuildSlots(parent, "Stud", MaxStudSlots, PrimitiveType.Sphere);
            fins = BuildSlots(parent, "Fin", MaxFinSlots, PrimitiveType.Cube);
        }

        private static Transform[] BuildSlots(Transform parent, string prefix, int count, PrimitiveType shape)
        {
            var arr = new Transform[count];
            for (int i = 0; i < count; i++) arr[i] = BuildSlot(parent, prefix + i, shape);
            return arr;
        }

        private static Transform BuildSlot(Transform parent, string name, PrimitiveType shape)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            return go.transform;
        }

        /// <summary>A point on the disc's own rim, in the disc root's local space — see
        /// <see cref="DiscPlaneRotation"/>'s doc for why this is the root's local X/Y plane (not X/Z):
        /// the "Disc" child's own rim circle, after its 90-degree local rotation, resolves to
        /// <c>(x, -z, 0)</c> in the root's frame, so a decoration built as a SIBLING of "Disc" (not a
        /// child of it, which would also inherit Disc's own non-uniform thickness scale) reaches the
        /// same visible ring with its own independent scale.</summary>
        private static Vector3 RimPoint(float diameter, float angleDeg, float radiusScale)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float r = 0.5f * diameter * radiusScale;
            return new Vector3(Mathf.Cos(rad) * r, -Mathf.Sin(rad) * r, 0f);
        }

        private static void SetMaterial(Transform t, Material mat)
        {
            if (t == null || mat == null) return;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        /// <summary>MV-1196: POWER's own look — the disc's diameter/colour and its mark-specific
        /// decoration (Mark II's cog rim, Mark III's saw edge, Mark IV's star blades + glow core).
        /// Re-applied every <see cref="Spawn"/> since a pooled disc can carry a different mark each
        /// reuse.</summary>
        private void ApplyMarkVisual()
        {
            float d = VisualDiameter;
            if (_discVisual != null)
            {
                _discVisual.localScale = new Vector3(d, DiscThickness * 0.5f, d);
                SetMaterial(_discVisual, VfxMaterials.AdditiveTinted(Mark == DiscMark.IV ? VioletColor : DiscColor));
            }
            if (_trail != null) _trail.widthMultiplier = d * 0.6f;

            bool showRim = Mark == DiscMark.II || Mark == DiscMark.III;
            Material rimMat = VfxMaterials.AdditiveTinted(Mark == DiscMark.III ? SawColor : CogColor);
            float toothSize = (Mark == DiscMark.III ? 0.09f : 0.07f) * d;
            for (int i = 0; i < _rimTeeth.Length; i++)
            {
                Transform t = _rimTeeth[i];
                t.gameObject.SetActive(showRim);
                if (!showRim) continue;
                float angle = i * (360f / _rimTeeth.Length);
                t.localPosition = RimPoint(d, angle, 1.0f);
                t.localRotation = DiscPlaneRotation;
                t.localScale = new Vector3(toothSize, toothSize, toothSize * 0.6f);
                SetMaterial(t, rimMat);
            }

            bool showBlades = Mark == DiscMark.IV;
            Material bladeMat = VfxMaterials.AdditiveTinted(VioletColor);
            for (int i = 0; i < _blades.Length; i++)
            {
                Transform t = _blades[i];
                t.gameObject.SetActive(showBlades);
                if (!showBlades) continue;
                float angle = i * (360f / _blades.Length) + 45f;
                t.localPosition = RimPoint(d, angle, 1.25f);
                t.localRotation = DiscPlaneRotation * Quaternion.Euler(0f, 0f, angle);
                t.localScale = new Vector3(d * 0.18f, d * 0.4f, 0.02f);
                SetMaterial(t, bladeMat);
            }
            if (_ivCore != null)
            {
                _ivCore.gameObject.SetActive(showBlades);
                if (showBlades)
                {
                    _ivCore.localScale = Vector3.one * (d * 0.22f);
                    SetMaterial(_ivCore, VfxMaterials.AdditiveTinted(VioletColor * 1.3f));
                }
            }
        }

        /// <summary>MV-1196: AIMING's own fins — one per <see cref="FinCount"/> level (0 to 4), fixed at
        /// spawn (the turn rate/lookahead this fin count corresponds to are also fixed for the whole
        /// shot — see <see cref="Ricochet.AimTurnRateDegPerSec"/>).</summary>
        private void ApplyFinVisual()
        {
            float d = VisualDiameter;
            Material finMat = VfxMaterials.AdditiveTinted(FinColor);
            for (int i = 0; i < _fins.Length; i++)
            {
                Transform t = _fins[i];
                bool active = i < FinCount;
                t.gameObject.SetActive(active);
                if (!active) continue;
                float angle = i * (360f / _fins.Length) + 45f;
                t.localPosition = RimPoint(d, angle, 1.15f);
                t.localRotation = DiscPlaneRotation * Quaternion.Euler(0f, 0f, angle);
                t.localScale = new Vector3(d * 0.12f, d * 0.3f, 0.015f);
                SetMaterial(t, finMat);
            }
        }

        /// <summary>MV-1196: RICOCHETS' own studs — "one white stud on the disc for every bounce it has
        /// left", read live off <see cref="StudCount"/> (<see cref="_bouncesRemaining"/>) so the count
        /// drops the instant a bounce spends one (see the bounce branch in <see cref="Tick"/>).</summary>
        private void ApplyStudVisual()
        {
            float d = VisualDiameter;
            Material studMat = VfxMaterials.AdditiveTinted(StudColor);
            for (int i = 0; i < _studs.Length; i++)
            {
                Transform t = _studs[i];
                bool active = i < StudCount;
                t.gameObject.SetActive(active);
                if (!active) continue;
                float angle = i * (360f / _studs.Length);
                t.localPosition = RimPoint(d, angle, 0.6f);
                t.localScale = Vector3.one * (d * 0.08f);
                SetMaterial(t, studMat);
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
