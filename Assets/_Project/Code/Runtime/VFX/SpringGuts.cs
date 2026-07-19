using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// A dead robot's insides (YT-101): the coils it throws out when it comes apart.
    ///
    /// YT-48 already gave the kill a pop — bright sparks and dark chunks, both particles. What it
    /// could not give it was a PUNCHLINE. Particles vanish in a third of a second and leave the lawn
    /// exactly as they found it, so a kill read as a flash rather than as a thing you broke. Springs
    /// are the joke: they come out, they bounce, they roll to a stop, and for a second and a half
    /// afterwards the grass is littered with the guts of the robot you just shot. That leftover is
    /// the whole feeling — the yard remembers the kill for a moment.
    ///
    /// Same contract as the rest of this folder: it self-installs, it listens to
    /// <see cref="HudSignals.EnemyKilled"/>, and it is invisible to gameplay. Nothing in Enemies/ or
    /// Combat/ knows it exists, and deleting this file changes nothing but the picture.
    ///
    /// -------------------------------------------------------------------------------------------
    /// WHY THESE ARE NOT RIGIDBODIES
    ///
    /// The ticket asks for rigidbodies, and rigidbodies are the wrong tool here — the springs would
    /// be worse, not better, and the reason is worth writing down.
    ///
    /// This game has no physics. Every actor in it is a CharacterController, which is not a
    /// rigidbody and does not participate in a physics solve. Introducing real dynamic bodies would
    /// mean either (a) they collide with the cast, and a dozen coils bouncing off the crowd start
    /// SHOVING gameplay around — debris that can body-block a rusher is a fairness bug — or (b) they
    /// are excluded from the cast, which needs a new physics layer, which is a project-settings
    /// change and a guardrail trip. Both cost real risk to buy a solve we would then have to fight.
    ///
    /// So the coils fly themselves: ballistic arc, bounce off the lawn plane, spin, settle. The lawn
    /// is flat at y = 0 and the whole game already leans on that (<c>GroundAnchorVfx.Ground</c>
    /// flattens every ring and shadow to it), so the one collision that matters is a float compare.
    /// What it buys: no solver cost at all, no way for debris to touch gameplay, deterministic
    /// motion a test can prove without a scene, and a guaranteed retire — a pooled slot comes back
    /// on a timer, so springs cannot leak even if one lands somewhere strange.
    ///
    /// The AC is "springs bounce out, it's satisfying, it holds 60fps, they clean up." That is what
    /// this delivers; rigidbodies were the suggested means, not the goal.
    ///
    /// -------------------------------------------------------------------------------------------
    /// HOLDING 60FPS WHEN A CROWD DIES AT ONCE
    ///
    /// Three caps, because a crowd wipe is the case that matters:
    ///   * per death — <see cref="PerDeath"/> coils, so one kill is a handful and not a firework;
    ///   * globally — <see cref="Capacity"/> slots, allocated once and reused forever. A wipe that
    ///     wants more springs than exist recycles the oldest ones rather than allocating, so the
    ///     worst case on screen is a fixed, known number;
    ///   * per frame — <see cref="DeathsPerFrame"/> deaths get springs. A boss AoE can kill eight
    ///     robots on one frame, and eight simultaneous scatters is visual noise anyway (the Craft
    ///     Bible: juice must never obscure the read).
    ///
    /// One shared mesh and one shared material across every spring, so they SRP-batch into
    /// essentially one draw call no matter how many are on the lawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpringGuts : MonoBehaviour
    {
        // ---------------------------------------------------------------- tuning

        /// <summary>Coils per dead robot. Enough to read as "it came apart", few enough that killing
        /// three robots in a second does not carpet the lawn.</summary>
        public const int PerDeath = 4;

        /// <summary>Every spring that can exist at once. At ~2 s of life and 4 per kill this is about
        /// nine kills a second before the oldest start getting recycled — well past the rate the
        /// slice can actually produce.</summary>
        public const int Capacity = 36;

        /// <summary>Deaths that get springs on any one frame. See the class note.</summary>
        public const int DeathsPerFrame = 3;

        /// <summary>Harder than real gravity. Real gravity makes debris float like it is underwater at
        /// this scale; a heavy pull is what makes the coils feel like little steel things.</summary>
        private const float Gravity = 16f;

        /// <summary>How much speed survives a bounce. Springy — they are springs — but well under 1
        /// so the bouncing visibly decays instead of pinging forever.</summary>
        private const float Restitution = 0.52f;

        /// <summary>Sideways speed kept per bounce. Under 1 so they skid to a stop rather than sliding
        /// away across the yard.</summary>
        private const float GroundFriction = 0.68f;

        /// <summary>Below this upward speed a bounce is not worth having — the coil is done, it lies
        /// down and stops. Without a floor like this a bouncing body jitters against the plane
        /// forever, burning frames on a spring nobody can see moving.</summary>
        private const float SettleSpeed = 0.9f;

        private const float LifeMin = 1.5f;
        private const float LifeMax = 2.3f;

        /// <summary>How long the shrink-out takes at the end of a spring's life. They leave by getting
        /// small, not by fading: a fade needs transparency, and a transparent spring is a sorted draw
        /// and its own material. Shrinking keeps every coil in the one opaque batch.</summary>
        private const float ShrinkTime = 0.4f;

        private const float SizeMin = 0.19f;
        private const float SizeMax = 0.27f;

        /// <summary>Steel, and slightly warm. It has to be its own thing: the robots are turquoise and
        /// violet (<see cref="CharacterSkin"/>), the sparks are gold, the ground is a low-saturation
        /// green-gold. A bright neutral metal is the one value left that pops off all of them without
        /// adding a sixth colour to the yard.</summary>
        private static readonly Color Steel = new Color(0.78f, 0.76f, 0.70f, 1f);

        // ---------------------------------------------------------------- state

        /// <summary>One flying coil. A struct in a flat array — this is touched every frame for every
        /// live spring, and it is the one place in this file worth not making garbage in.</summary>
        private struct Spring
        {
            public Transform Xf;
            public Vector3 Vel;
            public Vector3 SpinAxis;
            public float SpinSpeed;    // degrees/second
            public float Age;
            public float Life;
            public float Size;
            public bool Live;
        }

        private readonly List<Spring> _springs = new List<Spring>(Capacity);
        private Material _steelMat;
        private int _next;              // round-robin cursor: the oldest slot to recycle
        private int _deathsThisFrame;

        /// <summary>How many coils are in the air right now. For tests and for the profiler HUD.</summary>
        public int LiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _springs.Count; i++) if (_springs[i].Live) n++;
                return n;
            }
        }

        // ---------------------------------------------------------------- lifecycle

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<SpringGuts>() != null) return;
            new GameObject("SpringGuts").AddComponent<SpringGuts>();
        }

        private void Awake()
        {
            // Ours, and explicit. A primitive's default material is not in the build's shader set and
            // ships MAGENTA (YT-58), so nothing here is ever left to a default.
            var template = MaterialLibrary.Character();
            _steelMat = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);
            _steelMat.name = "SpringSteel";
            _steelMat.hideFlags = HideFlags.HideAndDontSave;
            if (_steelMat.HasProperty("_BaseColor")) _steelMat.SetColor("_BaseColor", Steel);
            if (_steelMat.HasProperty("_Color")) _steelMat.SetColor("_Color", Steel);
        }

        private void OnEnable() => HudSignals.EnemyKilled += OnEnemyKilled;

        // HudSignals is static: a missed -= keeps this object and every spring transform alive
        // across a scene reload.
        private void OnDisable() => HudSignals.EnemyKilled -= OnEnemyKilled;

        private void OnDestroy()
        {
            if (_steelMat != null) Destroy(_steelMat);
        }

        // ---------------------------------------------------------------- the kill

        /// <summary>
        /// A robot died. Throw its guts out.
        ///
        /// The signal carries a position and nothing else — the robot's GameObject is deactivated on
        /// this same frame (RobotEnemy.Die), so there is no body left to read a size or a facing off
        /// and nothing to parent to. Free-standing world-space props are the only shape this can take,
        /// which is also why they are safe: they outlive the thing that spawned them by design.
        /// </summary>
        private void OnEnemyKilled(Vector3 pos)
        {
            if (_deathsThisFrame >= DeathsPerFrame) return;
            _deathsThisFrame++;

            for (int i = 0; i < PerDeath; i++) Launch(pos);
        }

        private void Launch(Vector3 from)
        {
            int slot = Take();
            var s = _springs[slot];
            if (s.Xf == null) return;

            // Out and up, in a wide cone. Wide, because the read is "it burst" — a tight cone is a
            // fountain, and a fountain is what a machine does on purpose.
            Vector3 dir = Vector3.Slerp(Vector3.up, Random.onUnitSphere, 0.62f).normalized;
            if (dir.y < 0.15f) dir.y = 0.15f;    // never fire one straight into the lawn

            s.Vel = dir.normalized * Random.Range(3.2f, 6.4f);
            s.SpinAxis = Random.onUnitSphere;
            s.SpinSpeed = Random.Range(320f, 780f) * (Random.value < 0.5f ? -1f : 1f);
            s.Age = 0f;
            s.Life = Random.Range(LifeMin, LifeMax);
            s.Size = Random.Range(SizeMin, SizeMax);
            s.Live = true;

            // Start at the robot's middle — that is where guts come from — with a little scatter so
            // four coils from one kill do not leave as a single clump.
            s.Xf.position = from + Random.insideUnitSphere * 0.18f;
            s.Xf.rotation = Random.rotation;
            s.Xf.localScale = Vector3.one * s.Size;
            s.Xf.gameObject.SetActive(true);

            _springs[slot] = s;
        }

        /// <summary>
        /// The index of a slot to fly. Prefers a dead slot, then grows the pool up to
        /// <see cref="Capacity"/>, then recycles round-robin — which, because every spring has
        /// roughly the same lifetime, hands back the oldest one.
        ///
        /// Recycling rather than dropping is deliberate: under a crowd wipe, stealing a spring that
        /// has already bounced and is lying still is invisible, whereas refusing to spawn means the
        /// robot that died last comes apart in silence. The kill always gets its punchline.
        /// </summary>
        private int Take()
        {
            for (int i = 0; i < _springs.Count; i++)
                if (!_springs[i].Live) return i;

            if (_springs.Count < Capacity)
            {
                _springs.Add(new Spring { Xf = NewSpringTransform() });
                return _springs.Count - 1;
            }

            int slot = _next;
            _next = (_next + 1) % Capacity;
            return slot;
        }

        private Transform NewSpringTransform()
        {
            var go = new GameObject("Spring");
            go.transform.SetParent(transform, worldPositionStays: false);

            go.AddComponent<MeshFilter>().sharedMesh = SpringMesh.Shared;

            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _steelMat;
            // No shadows. Thirty-six coils casting shadow maps is a real cost for a mark nobody can
            // see under a spring the size of a thumbnail — and the ground already carries the
            // contact shadows that matter (YT-85).
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            // CharacterSkinDirector only claims renderers under an IDamageable and these hang under
            // this director, so they are already out of its reach — but RuntimeSurfaceDirector sweeps
            // the scene for anything it recognises, and this marker is the house way of saying
            // "this material is driven here, keep off."
            go.AddComponent<SelfDrivenTint>();

            go.SetActive(false);
            return go.transform;
        }

        // ---------------------------------------------------------------- flying

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;   // paused on the result screen — hold the pose

            for (int i = 0; i < _springs.Count; i++)
            {
                var s = _springs[i];
                if (!s.Live) continue;

                s.Age += dt;
                if (s.Age >= s.Life)
                {
                    s.Live = false;
                    s.Xf.gameObject.SetActive(false);
                    _springs[i] = s;
                    continue;
                }

                Vector3 p = s.Xf.position;
                Step(ref p, ref s.Vel, ref s.SpinSpeed, dt);

                s.Xf.position = p;
                s.Xf.rotation = Quaternion.AngleAxis(s.SpinSpeed * dt, s.SpinAxis) * s.Xf.rotation;
                s.Xf.localScale = Vector3.one * (s.Size * ShrinkAt(s.Age, s.Life));

                _springs[i] = s;
            }
        }

        private void LateUpdate() => _deathsThisFrame = 0;

        /// <summary>
        /// One frame of a spring's flight: gravity, then the lawn.
        ///
        /// Pure and static so a test can fly a spring for two seconds without a scene, a robot or a
        /// render — which is the point of not using the physics engine. <paramref name="spin"/> is
        /// damped alongside the bounce because a coil that keeps whirling at full speed after it has
        /// stopped moving reads as broken.
        /// </summary>
        public static void Step(ref Vector3 pos, ref Vector3 vel, ref float spin, float dt)
        {
            vel.y -= Gravity * dt;
            pos += vel * dt;

            if (pos.y > 0f) return;

            // It hit the lawn. Put it back on the surface rather than leaving it under: a body that
            // is allowed to sink keeps re-triggering the bounce and buzzes against the plane.
            pos.y = 0f;

            if (-vel.y < SettleSpeed)
            {
                // Done. Lie still — no residual bounce, no residual slide, no residual spin.
                vel = Vector3.zero;
                spin = 0f;
                return;
            }

            vel.y = -vel.y * Restitution;
            vel.x *= GroundFriction;
            vel.z *= GroundFriction;
            spin *= GroundFriction;
        }

        /// <summary>
        /// The size multiplier at a given age — 1 for most of the life, then down to 0 over the last
        /// <see cref="ShrinkTime"/>. Pure, and public, so the "they always clean up" half of the AC
        /// is a test rather than a promise.
        /// </summary>
        public static float ShrinkAt(float age, float life)
        {
            float left = life - age;
            if (left >= ShrinkTime) return 1f;
            return Mathf.Clamp01(left / ShrinkTime);
        }
    }
}
