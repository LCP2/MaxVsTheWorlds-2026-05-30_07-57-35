using System;
using System.Collections.Generic;
using UnityEngine;
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
        /// at the play camera and thinner than the nameplate text above the robot it hits. Read once;
        /// every renderer <see cref="BuildVisual"/> builds sizes off this same struct.</summary>
        private static readonly CombatVfxTuning.LppeBoltTuning BoltTuning = CombatVfxTuning.LppeBolt();

        /// <summary>MV-825: the core reads white-hot, not orange -- Lee: "make this look like a laser.
        /// Make it sleek, bright, crackling." The orange family (MV-805) now lives on the glow sheath
        /// around the core (<see cref="SheathTintOpaque"/>), which is what keeps the weapon's overall
        /// silhouette reading orange at a glance.</summary>
        private static readonly Color CoreColor = new Color(1.00f, 0.97f, 0.90f);

        /// <summary>MV-825 item 8: a forked bolt's own tell -- electric blue-white, not a brighter
        /// version of the same orange family (MV-814's old approach) -- "so a fork is recognisable".</summary>
        private static readonly Color ForkCoreColor = new Color(0.75f, 0.95f, 1.00f);

        /// <summary>The sheath's own baked colour before alpha (spec: "1.00 0.45 0.10 at 0.55 alpha").
        /// Also doubles as the weapon's own "identity" orange -- the ground glow and ticket item 5's
        /// trail fade both key off this, not the white-hot core.</summary>
        private static readonly Color SheathTintOpaque = new Color(1.00f, 0.45f, 0.10f);

        /// <summary>MV-825 item 4: the crackle filaments' own colour.</summary>
        private static readonly Color CrackleColor = new Color(1.00f, 0.80f, 0.45f);

        /// <summary>MV-814: a pulse whose hit didn't kill but left its target under this fraction of
        /// max health also releases a FORK, widening the old kill-only trigger -- against World 2's
        /// health pools an outright kill was rare enough that FORK read as doing nothing (measured:
        /// see PulseLaser.RegisterKill's own doc comment).</summary>
        private const float NearDeathForkThreshold = 0.15f;

        private RobotEnemy _target;
        private IDamageable _targetDamageable;
        private float _speed;
        private float _turnRateDegPerSec;
        private float _damage;
        private float _lifetime;
        private float _age;
        private Action<RobotEnemy, float> _onHit;
        private Action<RobotEnemy, Vector3> _onKill;
        private bool _canFork;
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

        /// <summary>Alpha the ground glow renders at — dim enough it reads as a light spilling onto
        /// the floor under the bolt, not a second bolt lying flat (MV-770). MV-825: keyed off the
        /// sheath's own orange, the bolt's "identity" colour now that the core itself is white-hot.</summary>
        private static readonly Color GroundGlowColor =
            new Color(SheathTintOpaque.r, SheathTintOpaque.g, SheathTintOpaque.b, 0.5f);

        // Reused every tick so the per-frame obstruction check (below) allocates nothing, the same
        // idiom WaterBlaster.FireTick's static s_buffer/s_hits use.
        private static readonly RaycastHit[] s_worldHits = new RaycastHit[8];

        /// <summary>The robot this pulse locked onto at fire time, or null if none qualified — the
        /// resolved value MV-708 AC1 asserts against.</summary>
        public RobotEnemy Target => _target;

        /// <summary>MV-814/825: the resolved SHEATH renderer, for a test to compare cross-section
        /// bounds between an ordinary pulse and a forked one — same "public accessor for a test" idiom
        /// as <see cref="PulseLaser.LastForkedPulseForTests"/>. MV-825: FORK's own wider cross-section
        /// now scales the sheath, not the core (the core only changes colour, item 8), so this must
        /// resolve the sheath specifically rather than whichever renderer happens to build first.</summary>
        public MeshRenderer BoltRendererForTests => transform.Find("Sheath")?.GetComponent<MeshRenderer>();

        /// <summary>True once this pulse has hit its target, been blocked, or expired — it takes no
        /// further action after this, so a test can keep ticking it without double-applying damage.</summary>
        public bool IsSpent => _spent;

        /// <summary>
        /// Fire one pulse from <paramref name="origin"/> along <paramref name="aimDir"/>. Locks at fire
        /// time onto the nearest awake <see cref="RobotEnemy"/> within <paramref name="lockRange"/> and
        /// <paramref name="lockHalfAngleDeg"/> of the aim direction (unless <paramref name="forcedTarget"/>
        /// is given — MV-768 FORK's own release at a specific robot, no cone check); flies straight and
        /// dies at its lifetime if none qualifies. <paramref name="onHit"/> (optional) lets the weapon
        /// track its own Shock combo per target without this projectile knowing anything about that
        /// mechanic. <paramref name="onKill"/> (optional, MV-768 FORK) fires once, in addition to
        /// <paramref name="onHit"/>, the instant a hit this pulse lands actually kills its target — but
        /// only while <paramref name="canFork"/> is true; a pulse fired with it false (a fork's own
        /// release) can still land a kill, it just never reports one, so FORK can never chain off its
        /// own forked pulse.
        /// </summary>
        public static SeekerPulse Fire(Vector3 origin, Vector3 aimDir, float speed, float turnRateDegPerSec,
            float lifetime, float damage, float lockRange, float lockHalfAngleDeg,
            Action<RobotEnemy, float> onHit = null, RobotEnemy forcedTarget = null, bool canFork = true,
            Action<RobotEnemy, Vector3> onKill = null, bool isFork = false)
        {
            aimDir.y = 0f;
            if (aimDir.sqrMagnitude < 1e-4f) aimDir = Vector3.forward;
            aimDir.Normalize();

            var go = new GameObject("SeekerPulse (stand-in)");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
            BuildVisual(go.transform, isFork);

            RobotEnemy target = forcedTarget != null
                ? forcedTarget
                : AcquireTarget(origin, aimDir, lockRange, lockHalfAngleDeg);
            LockBracketVfx.Show(target);   // MV-702: the reticle bracket MV-708 deferred as this ticket's own

            var pulse = go.AddComponent<SeekerPulse>();
            pulse.Init(target, speed, turnRateDegPerSec, lifetime, damage, onHit, onKill, canFork);
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
            _groundGlow.Show(new Vector3(origin.x, 0f, origin.z), BoltTuning.GroundGlowDiameter * 0.5f,
                GroundGlowColor);
        }

        /// <summary>Nearest awake, alive robot within range and the lock cone — "awake" excludes a
        /// still-<see cref="RobotEnemy.IsDormant"/> robot by rule (spec: "dormant robots are invisible
        /// targets"). Reads the field-wide registry, so any level/deck qualifies, not just this room.</summary>
        private static RobotEnemy AcquireTarget(Vector3 origin, Vector3 aimDir, float lockRange,
            float lockHalfAngleDeg)
        {
            var active = RobotEnemy.Active;
            RobotEnemy best = null;
            float bestDistSq = float.MaxValue;
            float rangeSq = lockRange * lockRange;

            for (int i = 0; i < active.Count; i++)
            {
                RobotEnemy candidate = active[i];
                if (candidate == null || !candidate.IsAlive || candidate.IsDormant) continue;

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
            float damage, Action<RobotEnemy, float> onHit, Action<RobotEnemy, Vector3> onKill, bool canFork)
        {
            _target = target;
            _targetDamageable = target;
            _speed = speed;
            _turnRateDegPerSec = turnRateDegPerSec;
            _lifetime = lifetime;
            _damage = damage;
            _onHit = onHit;
            _onKill = onKill;
            _canFork = canFork;

            // MV-825: hierarchy already built by BuildVisual (called from Fire before AddComponent),
            // so every child this reaches for already exists.
            _crackleFilaments = GetComponentsInChildren<LineRenderer>();
            RandomizeCrackle();

            Transform sheath = transform.Find("Sheath");
            _sheathRenderer = sheath != null ? sheath.GetComponent<MeshRenderer>() : null;
            _sheathMpb = new MaterialPropertyBlock();
            _sheathBaseColor = new Color(SheathTintOpaque.r, SheathTintOpaque.g, SheathTintOpaque.b,
                BoltTuning.SheathAlpha);
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
            if (_crackleTimer >= BoltTuning.CrackleRerandomizeInterval)
            {
                _crackleTimer = 0f;
                RandomizeCrackle();
            }
            _flickerTimer += dt;
            if (_flickerTimer >= BoltTuning.FlickerInterval)
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
            _groundGlow.Show(new Vector3(pos.x, 0f, pos.z), BoltTuning.GroundGlowDiameter * 0.5f, GroundGlowColor);
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
                RobotEnemy killedTarget = _target;
                Vector3 point = transform.position;
                _targetDamageable.TakeDamage(new DamageInfo(_damage, point, transform.forward,
                    Team.Player, source: DamageSource.PrimaryWeapon));
                _onHit?.Invoke(killedTarget, _damage);

                // MV-768 FORK: TakeDamage above is synchronous, so a kill is already reflected in
                // IsAlive by the time we check it here -- see RobotEnemy.TakeDamage/Die. _canFork is
                // false for a pulse FORK itself released (see Fire's own doc), so a forked pulse's own
                // kill never reports one -- the "must not chain" rule.
                // MV-814: widened past kill-only -- a hit that leaves the target under
                // NearDeathForkThreshold also releases a fork, measured to trigger far more often than
                // an outright kill against World 2's health pools (see PulseLaser.RegisterKill's doc).
                bool nearDeath = killedTarget.IsAlive && killedTarget.HealthNormalized < NearDeathForkThreshold;
                if (_canFork && (!killedTarget.IsAlive || nearDeath)) _onKill?.Invoke(killedTarget, point);
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
            float length = BoltTuning.CoreLength;
            int vertexCount = BoltTuning.CrackleVertexCount;

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
                    float radius = UnityEngine.Random.Range(0f, BoltTuning.CrackleMaxOffset);
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
            float mult = 1f + UnityEngine.Random.Range(-BoltTuning.FlickerAmount, BoltTuning.FlickerAmount);
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
        /// z=-<see cref="CombatVfxTuning.LppeBoltTuning.CoreLength"/> at the tail. <paramref name="isFork"/>
        /// gives a FORK-released bolt its own tell (item 8): an electric blue-white core colour and a
        /// 1.25x-wider sheath, so one extra bolt appearing out of a kill reads as deliberate.</summary>
        private static void BuildVisual(Transform parent, bool isFork)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            Color coreColor = isFork ? ForkCoreColor : CoreColor;
            Material coreMat = VfxMaterials.AdditiveTinted(coreColor);
            Mesh coreMesh = GetCoreMesh();

            // Item 2: "drawn twice for intensity" -- two renderers sharing the SAME cached mesh, not a
            // second Mesh instance, so this never trips AC1(e)'s no-new-mesh-after-first check.
            BuildBoltPart(parent, "Bolt", coreMesh, coreMat);
            BuildBoltPart(parent, "BoltGlow", coreMesh, coreMat);

            // Item 3: the sheath's own material is plain white+additive, not AdditiveTinted -- its
            // colour and alpha (including item 6's flicker) come entirely from a MaterialPropertyBlock
            // set below and refreshed by FlickerSheath, never from a shared cached material every
            // sheath of this kind would otherwise fight over.
            Mesh sheathMesh = GetSheathMesh(isFork);
            Material sheathMat = VfxMaterials.Additive(VfxMaterials.Solid());
            GameObject sheathGo = BuildBoltPart(parent, "Sheath", sheathMesh, sheathMat);
            var sheathMpb = new MaterialPropertyBlock();
            sheathMpb.SetColor("_BaseColor",
                new Color(SheathTintOpaque.r, SheathTintOpaque.g, SheathTintOpaque.b, BoltTuning.SheathAlpha));
            sheathGo.GetComponent<MeshRenderer>().SetPropertyBlock(sheathMpb);

            BuildCrackleFilaments(parent);

            // Item 5: a child anchored at the bolt's own TAIL, not this object's own transform (the
            // NOSE) -- so the trail streams from behind the core, the way a laser's own afterglow
            // would, instead of the old crescent's trail spilling out of its chord's midpoint (MV-815's
            // own "reads as an arrow's shaft" bug).
            var trailAnchor = new GameObject("TrailAnchor");
            trailAnchor.transform.SetParent(parent, false);
            trailAnchor.transform.localPosition = new Vector3(0f, 0f, -BoltTuning.CoreLength);

            var trail = trailAnchor.AddComponent<TrailRenderer>();
            trail.time = BoltTuning.TrailLifetime;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.widthMultiplier = BoltTuning.TrailWidth;
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Solid());
            // Item 5: "core colour fading to the sheath colour" -- a plain white material (above) so
            // the gradient's own colours show through unmodified rather than being multiplied by a
            // second baked tint.
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(coreColor, 0f), new GradientColorKey(SheathTintOpaque, 1f) },
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
        private static void BuildCrackleFilaments(Transform parent)
        {
            Material lineMat = VfxMaterials.Additive(VfxMaterials.Glow());
            for (int k = 0; k < BoltTuning.CrackleFilamentCount; k++)
            {
                var go = new GameObject($"Crackle{k}");
                go.transform.SetParent(parent, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = BoltTuning.CrackleVertexCount;
                lr.widthMultiplier = BoltTuning.CrackleWidth;
                lr.useWorldSpace = false;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.sharedMaterial = lineMat;
                lr.startColor = CrackleColor;
                lr.endColor = CrackleColor;
            }
        }

        // MV-805: sampled resolution for the Sentinel's own straight lathed bolt -- see
        // BuildStraightBoltMesh's own doc comment.
        private const int BoltProfilePoints = 24;
        private const int BoltRadialSegments = 16;
        private const float BoltPeakFractionFromNose = 0.45f;

        // MV-825: sampled resolution for Max's own core/sheath tubes.
        private const int CoreRadialSegments = 8;
        private const int SheathSpineSamples = 12;
        private const int SheathRadialSegments = 10;

        /// <summary>The one instance every ordinary bolt's core shares -- fork-invariant (item 8 only
        /// changes the core's COLOUR, not its size), so unlike the sheath there is no separate forked
        /// version of this mesh.</summary>
        private static Mesh s_coreMesh;

        private static Mesh s_sheathMesh;

        /// <summary>The one instance every FORK-released bolt's sheath shares -- its own cached mesh
        /// (1.25x the diameter baked in, item 8) rather than a runtime Transform scale.</summary>
        private static Mesh s_forkSheathMesh;

        /// <summary>MV-810: the single cached core mesh, built lazily on first use and shared by every
        /// LPPE pulse -- never rebuilt per shot.</summary>
        public static Mesh GetCoreMesh()
        {
            if (s_coreMesh == null) s_coreMesh = BuildCoreMesh(BoltTuning.CoreDiameter);
            return s_coreMesh;
        }

        /// <summary>MV-810: the single cached sheath mesh for the given fork-ness, built lazily on
        /// first use and shared by every LPPE pulse of that kind -- never rebuilt per shot.</summary>
        public static Mesh GetSheathMesh(bool isFork = false)
        {
            if (!isFork)
            {
                if (s_sheathMesh == null) s_sheathMesh = BuildSheathMesh(BoltTuning.SheathDiameter);
                return s_sheathMesh;
            }
            if (s_forkSheathMesh == null)
                s_forkSheathMesh = BuildSheathMesh(BoltTuning.SheathDiameter * BoltTuning.ForkSheathScale);
            return s_forkSheathMesh;
        }

        /// <summary>Drop this class's own reference to the cached bolt meshes -- same idiom as
        /// <see cref="MaxWorlds.Weapons.RigFusionState.ResetForTests"/>, so a domain reload or a fresh
        /// test run never holds a pointer to a mesh a previous run already destroyed.</summary>
        public static void ResetForTests()
        {
            s_coreMesh = null;
            s_sheathMesh = null;
            s_forkSheathMesh = null;
        }

        /// <summary>Item 2: a plain constant-radius tube along local Z, nose at z=0 down to the tail at
        /// z=-length -- "nothing wider than 0.12m across the travel direction except the glow sheath",
        /// so unlike the sheath this never tapers.</summary>
        private static Mesh BuildCoreMesh(float diameter)
        {
            float radius = diameter * 0.5f;
            float length = BoltTuning.CoreLength;

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
        private static Mesh BuildSheathMesh(float diameter)
        {
            float peakRadius = diameter * 0.5f;
            float noseZ = BoltTuning.SheathExtension;
            float tailZ = -(BoltTuning.CoreLength + BoltTuning.SheathExtension);

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
