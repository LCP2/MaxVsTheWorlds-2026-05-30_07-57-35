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
    /// are the joke: they come out, they bounce, they roll to a stop, and for several seconds
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
    /// One shared mesh and two shared materials (silver, onyx) across every spring — same shader,
    /// different `_BaseColor` — so they still SRP-batch into essentially one draw call no matter how
    /// many are on the lawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpringGuts : MonoBehaviour
    {
        // ---------------------------------------------------------------- tuning

        /// <summary>Coils per dead robot. Raised from 4 (YT-101) — a couple of springs read as
        /// "flicked a switch", ten reads as "it came apart". Still a handful, not a firework: killing
        /// three robots in a second must not carpet the lawn.</summary>
        public const int PerDeath = 10;

        /// <summary>Every spring that can exist at once. At ~4 s of life and 10 per kill that is about
        /// nine kills' worth of springs alive on screen together (roughly two kills a second
        /// sustained) before the oldest start getting recycled — comfortably past the rate the slice
        /// can actually produce.</summary>
        public const int Capacity = 90;

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

        /// <summary>Raised from 1.5-2.3s (YT-101) so the guts litter the lawn for a proper beat
        /// instead of clearing almost as fast as the particles they replaced.</summary>
        private const float LifeMin = 3.4f;
        private const float LifeMax = 4.6f;

        /// <summary>How long the shrink-out takes at the end of a spring's life. They leave by getting
        /// small, not by fading: a fade needs transparency, and a transparent spring is a sorted draw
        /// and its own material. Shrinking keeps every coil in the one opaque batch.</summary>
        private const float ShrinkTime = 0.4f;

        private const float SizeMin = 0.19f;
        private const float SizeMax = 0.27f;

        /// <summary>Metallic silver and near-black — swapped in for the old warm beige, which read as
        /// brown rather than steel. Two colours instead of one gives the burst some variety without
        /// adding a new hue to the yard: the robots are turquoise and violet
        /// (<see cref="CharacterSkin"/>), the sparks are gold, the ground is a low-saturation
        /// green-gold, and neutral metal is the one value left that pops off all of them.</summary>
        private static readonly Color Silver = new Color(0.86f, 0.88f, 0.90f, 1f);

        /// <summary>The darker half of the pair — mixed in with <see cref="Silver"/> so a burst reads
        /// as a handful of distinct coils rather than one flat-coloured clump.</summary>
        private static readonly Color Onyx = new Color(0.05f, 0.05f, 0.06f, 1f);

        /// <summary>Share of springs that come out silver rather than onyx. Silver majority, black for
        /// interest — not a 50/50 split, which would read as two separate explosions.</summary>
        private const float SilverChance = 0.65f;

        // ---------------------------------------------------------------- state

        /// <summary>One flying coil. A struct in a flat array — this is touched every frame for every
        /// live spring, and it is the one place in this file worth not making garbage in.</summary>
        private struct Spring
        {
            public Transform Xf;
            public MeshRenderer Renderer;
            public Vector3 Vel;
            public Vector3 SpinAxis;
            public float SpinSpeed;    // degrees/second
            public float Age;
            public float Life;
            public float Size;
            public bool Live;
        }

        private readonly List<Spring> _springs = new List<Spring>(Capacity);
        private Material _silverMat;
        private Material _onyxMat;
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
            _silverMat = BuildSpringMaterial(template, "SpringSilver", Silver);
            _onyxMat = BuildSpringMaterial(template, "SpringOnyx", Onyx);
        }

        // Same shader instance for both, just a different _BaseColor — that is what keeps a mixed
        // burst of silver and onyx coils SRP-batching as one draw call instead of two.
        private static Material BuildSpringMaterial(Material template, string name, Color color)
        {
            var m = template != null ? new Material(template) : new Material(MaterialLibrary.SurfaceShader);
            m.name = name;
            m.hideFlags = HideFlags.HideAndDontSave;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            return m;
        }

        private void OnEnable() => HudSignals.EnemyKilled += OnEnemyKilled;

        // HudSignals is static: a missed -= keeps this object and every spring transform alive
        // across a scene reload.
        private void OnDisable() => HudSignals.EnemyKilled -= OnEnemyKilled;

        private void OnDestroy()
        {
            if (_silverMat != null) Destroy(_silverMat);
            if (_onyxMat != null) Destroy(_onyxMat);
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

            // Re-rolled per launch, not fixed per slot — a recycled spring must not keep wearing
            // whichever colour it happened to spawn with the first time.
            if (s.Renderer != null) s.Renderer.sharedMaterial = Random.value < SilverChance ? _silverMat : _onyxMat;

            // Start at the robot's middle — that is where guts come from — with a little scatter so
            // ten coils from one kill do not leave as a single clump.
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
                var xf = NewSpringTransform();
                _springs.Add(new Spring { Xf = xf, Renderer = xf.GetComponent<MeshRenderer>() });
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
            r.sharedMaterial = _silverMat;
            // No shadows. Ninety coils casting shadow maps is a real cost for a mark nobody can
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
