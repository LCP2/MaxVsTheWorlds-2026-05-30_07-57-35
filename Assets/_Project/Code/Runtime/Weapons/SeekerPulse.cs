using System;
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// Max's own seeking projectile (MV-708) — the LPPE's discrete pulse, steered by the same
    /// <see cref="HomingSteering"/> the Launcher's <see cref="HomingMissile"/> already uses. Unlike that
    /// missile it carries no splash and never sputters/bounces on timeout: a pulse that never finds (or
    /// loses) a target just flies straight and quietly expires at its lifetime.
    ///
    /// Target acquisition happens once, at <see cref="Fire"/> time, against whatever <see cref="RobotEnemy"/>
    /// is nearest, awake and inside the lock cone — never re-evaluated mid-flight, so a locked pulse
    /// commits to the shot the player actually saw fire.
    /// </summary>
    public sealed class SeekerPulse : MonoBehaviour
    {
        private const float ContactRadius = 0.5f;

        /// <summary>The bolt's own resolved shape (MV-770) — was a 0.35m/0.08m-wide bolt, 3.8px across
        /// at the play camera and thinner than the nameplate text above the robot it hits. MV-844: no
        /// longer one shared struct — <see cref="CombatVfxTuning.LppeBolt"/> now takes POWER's own
        /// visual-strength fraction, so each pulse resolves and carries its own instance
        /// (<see cref="_tuning"/>), read once at <see cref="Fire"/> time.</summary>
        private CombatVfxTuning.LppeBoltTuning _tuning;

        /// <summary>MV-844: POWER's visual-strength fraction this pulse was fired at (0 at L1, 1 at
        /// World 2's cap) — stored so <see cref="Tick"/>'s own ground-glow refresh can keep reading the
        /// same resolved sheath tint <see cref="Fire"/> built the pulse with.</summary>
        private float _powerLevelFraction;

        /// <summary>MV-825: the core reads white-hot, not orange -- Lee: "make this look like a laser.
        /// Make it sleek, bright, crackling." The orange family (MV-805) now lives on the glow sheath
        /// around the core (<see cref="SheathTintOpaqueBase"/>), which is what keeps the weapon's
        /// overall silhouette reading orange at a glance.</summary>
        private static readonly Color CoreColor = new Color(1.00f, 0.97f, 0.90f);

        /// <summary>The sheath's own baked colour before alpha at POWER L1 (spec: "1.00 0.45 0.10 at
        /// 0.55 alpha"). Also doubles as the weapon's own "identity" orange -- the ground glow and
        /// ticket item 5's trail fade both key off this, not the white-hot core.</summary>
        private static readonly Color SheathTintOpaqueBase = new Color(1.00f, 0.45f, 0.10f);

        /// <summary>MV-844: the sheath's baked colour at POWER's L8 cap -- "more red" as POWER rises,
        /// <see cref="SheathTintFor"/> lerps between this and <see cref="SheathTintOpaqueBase"/>.</summary>
        private static readonly Color SheathTintOpaqueMax = new Color(1.00f, 0.08f, 0.04f);

        /// <summary>MV-825 item 4: the crackle filaments' own colour at POWER L1.</summary>
        private static readonly Color CrackleColorBase = new Color(1.00f, 0.80f, 0.45f);

        /// <summary>MV-844: the crackle filaments' own colour at POWER's L8 cap.</summary>
        private static readonly Color CrackleColorMax = new Color(1.00f, 0.35f, 0.20f);

        /// <summary>MV-844: the sheath/ground-glow/trail-fade tint at a given POWER visual-strength
        /// fraction (0 at L1, unchanged from today; 1 at World 2's L8 cap).</summary>
        private static Color SheathTintFor(float powerLevelFraction) =>
            Color.Lerp(SheathTintOpaqueBase, SheathTintOpaqueMax, Mathf.Clamp01(powerLevelFraction));

        /// <summary>MV-844: the crackle filaments' colour at a given POWER visual-strength fraction.</summary>
        private static Color CrackleColorFor(float powerLevelFraction) =>
            Color.Lerp(CrackleColorBase, CrackleColorMax, Mathf.Clamp01(powerLevelFraction));

        /// <summary>MV-844: the ground glow's colour at a given POWER visual-strength fraction -- keyed
        /// off the sheath's own tint at that same fraction, same "identity orange" rule
        /// <see cref="SheathTintOpaqueBase"/> always followed.</summary>
        private static Color GroundGlowColorFor(float powerLevelFraction)
        {
            Color tint = SheathTintFor(powerLevelFraction);
            return new Color(tint.r, tint.g, tint.b, 0.5f);
        }

        private RobotEnemy _target;
        private IDamageable _targetDamageable;
        private float _speed;
        private float _turnRateDegPerSec;
        private float _damage;
        private float _lifetime;
        private float _age;
        private Action<RobotEnemy, float> _onHit;
        private bool _spent;
        private GroundRing _groundGlow;

        // MV-825: the crackle filaments and the sheath's own flicker, both re-randomised on their own
        // timers (items 4/6) rather than every frame -- see RandomizeCrackle/FlickerSheath.
        private LineRenderer[] _crackleFilaments;
        private float _crackleTimer;
        private MeshRenderer _sheathRenderer;
        private MaterialPropertyBlock _sheathMpb;
        private Color _sheathBaseColor;
        private float _flickerTimer;

        // Reused every tick so the per-frame obstruction check (below) allocates nothing, the same
        // idiom WaterBlaster.FireTick's static s_buffer/s_hits use.
        private static readonly RaycastHit[] s_worldHits = new RaycastHit[8];

        /// <summary>The robot this pulse locked onto at fire time, or null if none qualified — the
        /// resolved value MV-708 AC1 asserts against.</summary>
        public RobotEnemy Target => _target;

        /// <summary>MV-825: the resolved SHEATH renderer, for a test to compare cross-section bounds —
        /// resolves the sheath specifically rather than whichever renderer happens to build first.</summary>
        public MeshRenderer BoltRendererForTests => transform.Find("Sheath")?.GetComponent<MeshRenderer>();

        /// <summary>True once this pulse has hit its target, been blocked, or expired — it takes no
        /// further action after this, so a test can keep ticking it without double-applying damage.</summary>
        public bool IsSpent => _spent;

        /// <summary>
        /// Fire one pulse from <paramref name="origin"/> along <paramref name="aimDir"/>. Locks at fire
        /// time onto the nearest awake <see cref="RobotEnemy"/> within <paramref name="lockRange"/> and
        /// <paramref name="lockHalfAngleDeg"/> of the aim direction; flies straight and dies at its
        /// lifetime if none qualifies. <paramref name="onHit"/> (optional) lets the weapon track its own
        /// Shock combo (and, MV-858, its own ARC release) per target without this projectile knowing
        /// anything about either mechanic. <paramref name="powerLevelFraction"/> (MV-844, default 0 --
        /// today's L1 look) is POWER's own resolved visual-strength fraction, passed in by
        /// <see cref="MaxWorlds.Combat.PulseLaser"/> rather than read from <see cref="RigState"/> here,
        /// same "caller resolves, projectile just draws" split <paramref name="damage"/> already follows.
        /// </summary>
        public static SeekerPulse Fire(Vector3 origin, Vector3 aimDir, float speed, float turnRateDegPerSec,
            float lifetime, float damage, float lockRange, float lockHalfAngleDeg,
            Action<RobotEnemy, float> onHit = null, float powerLevelFraction = 0f)
        {
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 1e-4f) aimDir = Vector3.forward;
            aimDir.Normalize();

            CombatVfxTuning.LppeBoltTuning tuning = CombatVfxTuning.LppeBolt(powerLevelFraction);

            var go = new GameObject("SeekerPulse (stand-in)");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            BuildVisual(go.transform, tuning, powerLevelFraction);

            RobotEnemy target = AcquireTarget(origin, aimDir, lockRange, lockHalfAngleDeg);
            LockBracketVfx.Show(target);   // MV-702: the reticle bracket MV-708 deferred as this ticket's own

            var pulse = go.AddComponent<SeekerPulse>();
            pulse._tuning = tuning;
            pulse._powerLevelFraction = powerLevelFraction;
            pulse.Init(target, speed, turnRateDegPerSec, lifetime, damage, onHit);
            pulse.BuildGroundGlow(origin);
            return pulse;
        }

        /// <summary>MV-770: a ground-hugging additive disc that tracks the bolt's XZ position — the
        /// weapon bolt has to be its own light source, and a light source spills onto the floor it
        /// passes over. Its own top-level object (not a child of <c>transform</c>) so it can stay flat
        /// on the ground while the bolt's own transform pitches/yaws under steering.</summary>
        private void BuildGroundGlow(Vector3 origin)
        {
            _groundGlow = GroundRing.Create("SeekerPulseGroundGlow", additive: true);
            _groundGlow.Show(new Vector3(origin.x, 0f, origin.z), _tuning.GroundGlowDiameter * 0.5f,
                GroundGlowColorFor(_powerLevelFraction));
        }

        /// <summary>Nearest awake, alive robot within range and the lock cone, ON THE SAME COMBAT LEVEL AS
        /// <paramref name="origin"/> (MV-944: floor and deck fight separately) — "awake" excludes a
        /// still-<see cref="RobotEnemy.IsDormant"/> robot by rule (spec: "dormant robots are invisible
        /// targets"). Reads the field-wide registry (any ROOM qualifies, not just this one), but never a
        /// robot standing on the other side of a floor/deck split.</summary>
        private static RobotEnemy AcquireTarget(Vector3 origin, Vector3 aimDir, float lockRange,
            float lockHalfAngleDeg)
        {
            var active = RobotEnemy.Active;
            MapData map = EnemyNavigation.Map;
            RobotEnemy best = null;
            float bestDistSq = float.MaxValue;
            float rangeSq = lockRange * lockRange;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (candidate == null || !candidate.IsAlive || candidate.IsDormant) continue;
                if (!CombatLevel.SameLevel(map, origin, candidate.transform.position)) continue;

                Vector3 to = candidate.transform.position - origin;
                to.y = 0f;
                float distSq = to.sqrMagnitude;
                if (distSq > rangeSq || distSq < 1e-6f) continue;

                float angle = Vector3.Angle(aimDir, to.normalized);
                if (angle > lockHalfAngleDeg) continue;

                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = candidate;
                }
            }

            return best;
        }

        private void Init(RobotEnemy target, float speed, float turnRateDegPerSec, float lifetime,
            float damage, Action<RobotEnemy, float> onHit)
        {
            _target = target;
            _targetDamageable = target;
            _speed = speed;
            _turnRateDegPerSec = turnRateDegPerSec;
            _lifetime = lifetime;
            _damage = damage;
            _onHit = onHit;

            // MV-825: hierarchy already built by BuildVisual (called from Fire before AddComponent),
            // so every child this reaches for already exists.
            _crackleFilaments = GetComponentsInChildren<LineRenderer>();
            RandomizeCrackle();

            Transform sheath = transform.Find("Sheath");
            _sheathRenderer = sheath != null ? sheath.GetComponent<MeshRenderer>() : null;
            _sheathMpb = new MaterialPropertyBlock();
            Color sheathTint = SheathTintFor(_powerLevelFraction);
            _sheathBaseColor = new Color(sheathTint.r, sheathTint.g, sheathTint.b, _tuning.SheathAlpha);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advance one step. Public — rather than the ordinary private-Update-only shape — so
        /// an EditMode test can step the flight deterministically without a live Unity frame loop, the
        /// same reason <see cref="HomingMissile.BlockedByGeometry"/> is its own callable static query.</summary>
        public void Tick(float dt)
        {
            if (_spent) return;
            _age += dt;

            // MV-825 items 4/6: the crackle filaments and the sheath's own flicker each re-randomise
            // on their own short timer rather than every frame -- cheap, and reads as an electrical
            // stutter rather than a smooth animation.
            _crackleTimer += dt;
            if (_crackleTimer >= _tuning.CrackleRerandomizeInterval)
            {
                _crackleTimer = 0f;
                RandomizeCrackle();
            }
            _flickerTimer += dt;
            if (_flickerTimer >= _tuning.FlickerInterval)
            {
                _flickerTimer = 0f;
                FlickerSheath();
            }

            bool targetLive = _target != null && _target.IsAlive;
            if (targetLive)
            {
                transform.rotation = HomingSteering.TurnToward(transform.rotation, transform.position,
                    _target.transform.position, _turnRateDegPerSec, dt);
            }

            Vector3 from = transform.position;
            Vector3 next = from + transform.forward * (_speed * dt);

            // MV-749: a pulse must damage ANY IDamageable its flight path reaches, not only the
            // RobotEnemy it locked onto at fire time -- gates, Replicators and bosses all carry their
            // own collider + IDamageable already; this is what was missing. Checked before the
            // Cover-layer obstruction below (and ahead of a locked robot's own arrival check further
            // down) so a gate standing in the way is what the pulse meets first, exactly like a player
            // aiming squarely at it with no robot in the lock cone at all.
            if (TryHitWorldDamageable(from, next, out IDamageable worldTarget, out Vector3 worldPoint))
            {
                transform.position = worldPoint;
                worldTarget.TakeDamage(new DamageInfo(_damage, worldPoint, transform.forward, Team.Player,
                    source: DamageSource.PrimaryWeapon));
                // No onHit callback here (Shock is a robot stun -- rule 4: meaningless on a gate or a
                // Replicator, so it's simply never invoked for one) and no error either way.
                Retire();
                return;
            }

            if (HomingSteering.BlockedByGeometry(from, next, out RaycastHit hit))
            {
                transform.position = hit.point;
                Retire();
                return;
            }

            transform.position = next;
            UpdateGroundGlow();

            if (targetLive)
            {
                Vector3 toTarget = _target.transform.position - transform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude <= ContactRadius * ContactRadius)
                {
                    ApplyHit();
                    return;
                }
            }

            if (_age >= _lifetime) Retire();
        }

        private void UpdateGroundGlow()
        {
            if (_groundGlow == null) return;
            Vector3 pos = transform.position;
            _groundGlow.Show(new Vector3(pos.x, 0f, pos.z), _tuning.GroundGlowDiameter * 0.5f,
                GroundGlowColorFor(_powerLevelFraction));
        }

        /// <summary>
        /// Any live, non-Player, non-<see cref="RobotEnemy"/> <see cref="IDamageable"/> whose collider
        /// the segment <paramref name="from"/>-&gt;<paramref name="to"/> actually crosses this tick
        /// (MV-749). Deliberately a multi-hit query (RaycastNonAlloc over the segment), not a single
        /// nearest-hit one: a closed <c>AreaGate</c>'s own leaf collider -- the one its
        /// <see cref="IDamageable"/> lives on -- sits exactly co-located with its Cover-layer
        /// <c>ThresholdObject</c> (MV-386's split), so a single-hit query could return either one
        /// non-deterministically and silently miss the gate. Robots are excluded on purpose -- MV-708's
        /// lock-on/arrival path (<see cref="ApplyHit"/>) is untouched by this ticket and must stay the
        /// only way a pulse ever damages a <see cref="RobotEnemy"/>.
        /// </summary>
        private static bool TryHitWorldDamageable(Vector3 from, Vector3 to, out IDamageable hit, out Vector3 point)
        {
            hit = null;
            point = to;
            Vector3 delta = to - from;
            float dist = delta.magnitude;
            if (dist < 1e-4f) return false;

            int count = Physics.RaycastNonAlloc(from, delta / dist, s_worldHits, dist, ~0,
                QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit rh = s_worldHits[i];
                if (rh.distance >= bestDist) continue;
                if (!rh.collider.TryGetComponent(out IDamageable d)) continue;
                if (d is RobotEnemy || !d.IsAlive || d.Team == Team.Player) continue;

                bestDist = rh.distance;
                hit = d;
                point = rh.point;
            }
            return hit != null;
        }

        private void ApplyHit()
        {
            if (_targetDamageable != null && _targetDamageable.IsAlive)
            {
                RobotEnemy hitTarget = _target;
                Vector3 point = transform.position;
                _targetDamageable.TakeDamage(new DamageInfo(_damage, point, transform.forward,
                    Team.Player, source: DamageSource.PrimaryWeapon));
                // MV-858: onHit fires on every hit, kill or not -- ARC (PulseLaser.TryArc) now reads
                // this same callback rather than a separate kill/near-death-only one.
                _onHit?.Invoke(hitTarget, _damage);
            }
            Retire();
        }

        /// <summary>MV-825 item 4: re-rolls each crackle filament's own 7 vertices, called both once at
        /// spawn (from <see cref="Init"/>) and every <see cref="CombatVfxTuning.LppeBoltTuning.CrackleRerandomizeInterval"/>
        /// thereafter. Each filament keeps its OWN fixed spoke direction (120 degrees apart, spread
        /// evenly around the core) with only a small angular jitter -- a vertex offset drawn from the
        /// full circle instead would let a single filament's own bounding box swing across the core's
        /// entire diameter, which is what AC1(b)'s 0.12m-per-renderer ceiling exists to catch.
        /// SetPosition per vertex, no array allocation, matching the ticket's own "reuse the
        /// LineRenderers... no per-frame allocation" rule.</summary>
        private void RandomizeCrackle()
        {
            if (_crackleFilaments == null || _crackleFilaments.Length == 0) return;
            float length = _tuning.CoreLength;
            int vertexCount = _tuning.CrackleVertexCount;

            for (int k = 0; k < _crackleFilaments.Length; k++)
            {
                LineRenderer lr = _crackleFilaments[k];
                if (lr == null) continue;
                float baseAngle = 360f / _crackleFilaments.Length * k;

                for (int i = 0; i < vertexCount; i++)
                {
                    float s = vertexCount <= 1 ? 0f : (float)i / (vertexCount - 1);
                    float z = -s * length;   // nose (0) to tail (-length), same span as the core
                    float angleDeg = baseAngle + UnityEngine.Random.Range(-10f, 10f);
                    float radius = UnityEngine.Random.Range(0f, _tuning.CrackleMaxOffset);
                    float rad = angleDeg * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(rad) * radius, Mathf.Sin(rad) * radius, 0f);
                    lr.SetPosition(i, new Vector3(0f, 0f, z) + offset);
                }
            }
        }

        /// <summary>MV-825 item 6: the sheath's alpha flickers +/-20% at random each
        /// <see cref="CombatVfxTuning.LppeBoltTuning.FlickerInterval"/> -- the core never flickers, so
        /// this only ever touches the sheath's own <see cref="MaterialPropertyBlock"/>, never the
        /// shared cached material every sheath of this kind uses (mutating that would flicker every
        /// LPPE bolt in the scene in lock-step and leave the material at a stale alpha for whichever
        /// bolt spawns next).</summary>
        private void FlickerSheath()
        {
            if (_sheathRenderer == null) return;
            float mult = 1f + UnityEngine.Random.Range(-_tuning.FlickerAmount, _tuning.FlickerAmount);
            Color c = _sheathBaseColor;
            c.a = Mathf.Clamp01(_sheathBaseColor.a * mult);
            _sheathMpb.SetColor("_BaseColor", c);
            _sheathRenderer.SetPropertyBlock(_sheathMpb);
        }

        private void Retire()
        {
            if (_spent) return;
            _spent = true;
            // Same Application.isPlaying guard as HomingMissile.Strip() — Destroy is illegal outside
            // Play mode, which an EditMode test driving Tick() directly hits every time.
            if (_groundGlow != null)
            {
                GameObject glowGo = _groundGlow.gameObject;
                if (Application.isPlaying) Destroy(glowGo); else DestroyImmediate(glowGo);
                _groundGlow = null;
            }
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        /// <summary>MV-825: the whole laser -- a white-hot core (drawn twice for intensity, item 2), a
        /// soft additive glow sheath around it (item 3), three crackling filaments (item 4), and a
        /// trail now emitted from the bolt's own TAIL (item 5), not its middle. Everything is built
        /// directly along local Z -- the parent's own forward, set to the travel direction once at
        /// <see cref="Fire"/> and re-applied every <see cref="Tick"/> by steering -- with local z=0 at
        /// the NOSE (the leading point <see cref="Tick"/> advances and tests collision against) and
        /// z=-<see cref="CombatVfxTuning.LppeBoltTuning.CoreLength"/> at the tail.
        /// <paramref name="tuning"/>/<paramref name="powerLevelFraction"/> (MV-844) are POWER's own
        /// resolved size/colour ramp for THIS pulse, threaded in from <see cref="Fire"/> rather than
        /// read off a shared static field.</summary>
        private static void BuildVisual(Transform parent, CombatVfxTuning.LppeBoltTuning tuning,
            float powerLevelFraction)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Material coreMat = VfxMaterials.AdditiveTinted(CoreColor);
            Mesh coreMesh = GetCoreMesh(tuning);

            // Item 2: "drawn twice for intensity" -- two renderers sharing the SAME cached mesh, not a
            // second Mesh instance, so this never trips AC1(e)'s no-new-mesh-after-first check.
            BuildBoltPart(parent, "Bolt", coreMesh, coreMat);
            BuildBoltPart(parent, "BoltGlow", coreMesh, coreMat);

            // Item 3: the sheath's own material is plain white+additive, not AdditiveTinted -- its
            // colour and alpha (including item 6's flicker) come entirely from a MaterialPropertyBlock
            // set below and refreshed by FlickerSheath, never from a shared cached material every
            // sheath of this kind would otherwise fight over.
            Mesh sheathMesh = GetSheathMesh(tuning);
            Material sheathMat = VfxMaterials.Additive(VfxMaterials.Solid());
            GameObject sheathGo = BuildBoltPart(parent, "Sheath", sheathMesh, sheathMat);
            Color sheathTint = SheathTintFor(powerLevelFraction);
            var sheathMpb = new MaterialPropertyBlock();
            sheathMpb.SetColor("_BaseColor", new Color(sheathTint.r, sheathTint.g, sheathTint.b, tuning.SheathAlpha));
            sheathGo.GetComponent<MeshRenderer>().SetPropertyBlock(sheathMpb);

            BuildCrackleFilaments(parent, tuning, CrackleColorFor(powerLevelFraction));

            // Item 5: a child anchored at the bolt's own TAIL, not this object's own transform (the
            // NOSE) -- so the trail streams from behind the core, the way a laser's own afterglow
            // would, instead of the old crescent's trail spilling out of its chord's midpoint (MV-815's
            // own "reads as an arrow's shaft" bug).
            var trailAnchor = new GameObject("TrailAnchor");
            trailAnchor.transform.SetParent(parent, false);
            trailAnchor.transform.localPosition = new Vector3(0f, 0f, -tuning.CoreLength);

            var trail = trailAnchor.AddComponent<TrailRenderer>();
            trail.time = tuning.TrailLifetime;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = tuning.TrailWidth;
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Solid());
            // Item 5: "core colour fading to the sheath colour" -- a plain white material (above) so
            // the gradient's own colours show through unmodified rather than being multiplied by a
            // second baked tint.
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(CoreColor, 0f), new GradientColorKey(sheathTint, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            trail.colorGradient = gradient;
            trail.Clear();
        }

        private static GameObject BuildBoltPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var meshRenderer = go.AddComponent<MeshRenderer>();
            if (material != null) meshRenderer.sharedMaterial = material;
            return go;
        }

        /// <summary>Item 4: builds the 3 filament <see cref="LineRenderer"/>s (positions set later, by
        /// <see cref="RandomizeCrackle"/>) -- unparented from any specific angle here since that's
        /// re-rolled on every randomise pass.</summary>
        private static void BuildCrackleFilaments(Transform parent, CombatVfxTuning.LppeBoltTuning tuning,
            Color crackleColor)
        {
            Material lineMat = VfxMaterials.Additive(VfxMaterials.Glow());
            for (int k = 0; k < tuning.CrackleFilamentCount; k++)
            {
                var go = new GameObject($"Crackle{k}");
                go.transform.SetParent(parent, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = tuning.CrackleVertexCount;
                lr.widthMultiplier = tuning.CrackleWidth;
                lr.useWorldSpace = false;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = lineMat;
                lr.startColor = crackleColor;
                lr.endColor = crackleColor;
            }
        }

        // MV-805: sampled resolution for the Sentinel's own straight lathed bolt -- see
        // BuildStraightBoltMesh's own doc comment.
        private const int BoltProfilePoints = 24;
        private const int BoltRadialSegments = 16;
        private const float BoltPeakFractionFromNose = 0.45f;

        // MV-825: sampled resolution for Max's own core/sheath tubes.
        private const int CoreRadialSegments = 8;

        /// <summary>MV-844: odd, not 12 -- the taper's own peak sits at s=0.5 (<see cref="BuildSheathMesh"/>),
        /// and only an odd sample count puts a sample exactly there. At 12 the nearest samples landed
        /// ~2.3% short of the authored diameter (harmless while every caller only checked an upper
        /// bound, but this ticket's own test resolves the sheath's rendered bounds against an exact
        /// authored value).</summary>
        private const int SheathSpineSamples = 13;
        private const int SheathRadialSegments = 10;

        /// <summary>MV-844: every ordinary bolt's core/sheath mesh shared by pulses at the SAME
        /// resolved diameter. Keyed by diameter rather than one shared instance now that POWER's own
        /// level scales core/sheath size (<see cref="CombatVfxTuning.LppeBolt"/>): the same level always
        /// resolves the same diameter, so this stays "built once per shape, never per shot" (MV-810)
        /// with as many entries as POWER has distinct levels in play (at most 8).</summary>
        private static readonly Dictionary<float, Mesh> s_coreMeshByDiameter = new Dictionary<float, Mesh>();

        private static readonly Dictionary<float, Mesh> s_sheathMeshByDiameter = new Dictionary<float, Mesh>();

        /// <summary>MV-810/844: the cached core mesh for <paramref name="tuning"/>'s own resolved
        /// diameter, built lazily on first use and shared by every LPPE pulse at that diameter -- never
        /// rebuilt per shot.</summary>
        public static Mesh GetCoreMesh(CombatVfxTuning.LppeBoltTuning tuning)
        {
            if (!s_coreMeshByDiameter.TryGetValue(tuning.CoreDiameter, out Mesh mesh))
            {
                mesh = BuildCoreMesh(tuning.CoreDiameter, tuning.CoreLength);
                s_coreMeshByDiameter[tuning.CoreDiameter] = mesh;
            }
            return mesh;
        }

        /// <summary>MV-810/844: the cached sheath mesh for <paramref name="tuning"/>'s own resolved
        /// diameter, built lazily on first use and shared by every LPPE pulse at that diameter -- never
        /// rebuilt per shot.</summary>
        public static Mesh GetSheathMesh(CombatVfxTuning.LppeBoltTuning tuning)
        {
            if (!s_sheathMeshByDiameter.TryGetValue(tuning.SheathDiameter, out Mesh mesh))
            {
                mesh = BuildSheathMesh(tuning.SheathDiameter, tuning.CoreLength, tuning.SheathExtension);
                s_sheathMeshByDiameter[tuning.SheathDiameter] = mesh;
            }
            return mesh;
        }

        /// <summary>Drop this class's own reference to the cached bolt meshes -- same idiom as
        /// <see cref="MaxWorlds.Weapons.RigFusionState.ResetForTests"/>, so a domain reload or a fresh
        /// test run never holds a pointer to a mesh a previous run already destroyed.</summary>
        public static void ResetForTests()
        {
            s_coreMeshByDiameter.Clear();
            s_sheathMeshByDiameter.Clear();
        }

        /// <summary>Item 2: a plain constant-radius tube along local Z, nose at z=0 down to the tail at
        /// z=-length -- "nothing wider than 0.12m across the travel direction except the glow sheath",
        /// so unlike the sheath this never tapers.</summary>
        private static Mesh BuildCoreMesh(float diameter, float length)
        {
            float radius = diameter * 0.5f;

            var verts = new List<Vector3>((CoreRadialSegments + 1) * 2);
            for (int ring = 0; ring < 2; ring++)
            {
                float z = ring == 0 ? 0f : -length;
                for (int j = 0; j <= CoreRadialSegments; j++)
                {
                    float phi = (float)j / CoreRadialSegments * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(phi) * radius, Mathf.Sin(phi) * radius, z));
                }
            }

            var tris = new List<int>();
            int w = CoreRadialSegments + 1;
            for (int j = 0; j < CoreRadialSegments; j++)
            {
                int a = j, b = a + 1, c = a + w, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            var mesh = new Mesh { name = "SeekerPulseCore" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Item 3: a tube along the SAME straight local-Z spine as the core, extending
        /// <see cref="CombatVfxTuning.LppeBoltTuning.SheathExtension"/> past the core at both ends and
        /// tapering (by radius, the same "peaks at the midpoint, vanishes at both tips" technique
        /// MV-815's crescent used for its own cross-section) to read as a soft glow with no hard
        /// edge, rather than a second solid tube.</summary>
        private static Mesh BuildSheathMesh(float diameter, float coreLength, float sheathExtension)
        {
            float peakRadius = diameter * 0.5f;
            float noseZ = sheathExtension;
            float tailZ = -(coreLength + sheathExtension);

            var verts = new List<Vector3>(SheathSpineSamples * (SheathRadialSegments + 1));
            var tris = new List<int>();

            for (int i = 0; i < SheathSpineSamples; i++)
            {
                float s = (float)i / (SheathSpineSamples - 1);   // 0 nose .. 1 tail
                float z = Mathf.Lerp(noseZ, tailZ, s);
                float radius = s <= 0.5f
                    ? Mathf.SmoothStep(0f, peakRadius, s * 2f)
                    : Mathf.SmoothStep(peakRadius, 0f, (s - 0.5f) * 2f);

                for (int j = 0; j <= SheathRadialSegments; j++)
                {
                    float phi = (float)j / SheathRadialSegments * Mathf.PI * 2f;
                    verts.Add(new Vector3(Mathf.Cos(phi) * radius, Mathf.Sin(phi) * radius, z));
                }
            }

            int w = SheathRadialSegments + 1;
            for (int i = 0; i < SheathSpineSamples - 1; i++)
                for (int j = 0; j < SheathRadialSegments; j++)
                {
                    int a = i * w + j, b = a + 1, c = a + w, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }

            var mesh = new Mesh { name = "SeekerPulseSheath" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>MV-806: the Sentinel's own straight bolt -- an ogive of revolution
        /// (<see cref="CharacterMeshes.Lathe"/>), small, red and straight-line-only precisely so it
        /// cannot be confused with Max's own. Takes <paramref name="length"/>/<paramref name="peakRadius"/>
        /// as explicit parameters (MV-825: sized by <see cref="MaxWorlds.Arena.SentinelBolt"/> off
        /// Max's own new <c>CoreLength</c>/<c>CoreDiameter</c>, at full scale -- the 0.7x
        /// "slightly smaller than Max's" coupling MV-806 established is applied separately via
        /// Transform.localScale, not baked into this mesh) rather than reading them off
        /// <see cref="CombatVfxTuning.LppeBolt"/> directly, since Max's own bolt is no longer a single
        /// capsule at all. Internal (not private) so <see cref="MaxWorlds.Arena.SentinelBolt"/>'s own
        /// cache can call it -- see that type's <c>GetBoltMesh</c>.</summary>
        internal static Mesh BuildStraightBoltMesh(float length, float peakRadius)
        {
            var profile = new Vector2[BoltProfilePoints];
            for (int i = 0; i < BoltProfilePoints; i++)
            {
                float tFromTail = (float)i / (BoltProfilePoints - 1);   // 0 = tail, 1 = nose
                float tFromNose = 1f - tFromTail;
                float radius = tFromNose <= BoltPeakFractionFromNose
                    ? Mathf.SmoothStep(0f, peakRadius, tFromNose / BoltPeakFractionFromNose)
                    : Mathf.SmoothStep(peakRadius, 0f,
                        (tFromNose - BoltPeakFractionFromNose) / (1f - BoltPeakFractionFromNose));
                profile[i] = new Vector2(radius, tFromTail * length);
            }
            return CharacterMeshes.Lathe(profile, BoltRadialSegments);
        }
    }
}
