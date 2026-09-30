using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// MV-1035: the TRAP ability's physical object — dropped at Max's feet by
    /// <see cref="PlayerAbilities.TryDropTrap"/>, it catches any eligible enemy robot
    /// (<see cref="RobotEnemy.IsTrapCatchable"/>) that walks inside its radius, holds it frozen and
    /// untargetable (<see cref="RobotEnemy.BeginTrapHold"/>), then converts everything it holds into an
    /// ally once full or <see cref="FillTimeoutSeconds"/> after its first catch. Reuses
    /// <see cref="RobotEnemy.TryConvert(bool)"/> (MV-716) and the existing ally-combat AI (MV-1015)
    /// rather than building a second conversion path. One trap at a time — enforced by
    /// <see cref="PlayerAbilities"/>, not here.
    /// </summary>
    [MaxWorlds.Core.PerfSection("weapons")]
    public sealed class RobotTrap : MonoBehaviour
    {
        private const float FillTimeoutSeconds = 8f;
        private const float NoCatchDespawnSeconds = 30f;
        private const float DragDuration = 0.5f;
        private const float HoldDistance = 1f;

        /// <summary>Cooldown after a successful conversion (spec: 20s) — read by <see cref="PlayerAbilities"/>.</summary>
        public const float ConversionCooldownSeconds = 20f;

        private static readonly Color CoreColor = new Color(0.427f, 1f, 0.416f);            // #6DFF6A
        private static readonly Color RimColor = new Color(0.85f, 0.45f, 0.1f);             // orange
        private static readonly Color BodyColor = new Color(0.169f, 0.200f, 0.251f);        // #2B3340
        private static readonly Color RingColor = new Color(0.427f, 1f, 0.416f, 0.55f);

        private int _capacity;
        private float _radius;
        private System.Action<bool> _onDespawn;

        private readonly List<RobotEnemy> _held = new List<RobotEnemy>(4);
        private readonly Dictionary<RobotEnemy, Vector3> _dragFrom = new Dictionary<RobotEnemy, Vector3>(4);
        private readonly Dictionary<RobotEnemy, float> _dragElapsed = new Dictionary<RobotEnemy, float>(4);
        private readonly Dictionary<RobotEnemy, LineRenderer> _tethers = new Dictionary<RobotEnemy, LineRenderer>(4);

        private float _sinceDrop;
        private float _sinceFirstCatch = -1f;
        private bool _converting;
        private bool _despawned;

        /// <summary>How many robots this trap is holding right now, and its capacity — the "n / capacity"
        /// HUD readout (spec).</summary>
        public int HeldCount => _held.Count;
        public int Capacity => _capacity;
        public float Radius => _radius;
        public bool IsConverting => _converting;

        public static RobotTrap Spawn(Vector3 position, int capacity, float radius, System.Action<bool> onDespawn)
        {
            var go = new GameObject("Robot Trap");
            go.transform.position = position;
            var trap = go.AddComponent<RobotTrap>();
            trap._capacity = Mathf.Max(0, capacity); // 0 is valid: already at the ally cap, so it never catches
            trap._radius = Mathf.Max(0.1f, radius);
            trap._onDespawn = onDespawn;
            trap.BuildVisuals();
            return trap;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The per-frame body, split out from <see cref="Update"/> (same seam
        /// <see cref="RobotEnemy.Tick"/> uses) so an EditMode test can drive an exact, controlled
        /// sequence of ticks — no reflection, no private calls.</summary>
        public void Tick(float dt)
        {
            _sinceDrop += dt;

            if (_converting)
            {
                TickConversion(dt);
                return;
            }

            TickCatch();
            TickDrag(dt);
            TickTethers();

            if (_held.Count == 0 && _sinceDrop >= NoCatchDespawnSeconds)
            {
                Despawn(false);
                return;
            }

            bool full = _held.Count >= _capacity;
            bool timedOut = _sinceFirstCatch >= 0f && (_sinceDrop - _sinceFirstCatch) >= FillTimeoutSeconds;
            if (_held.Count > 0 && (full || timedOut)) BeginConversion();
        }

        /// <summary>Scans every live, catchable robot in the field (not a physics query — matches the
        /// "read straight off RobotEnemy.Active" convention <see cref="PlayerAbilities.Land"/> and
        /// <see cref="PlayerAbilities.ApplyForceFieldPop"/> already use for the same reason: a robot
        /// carries more than one collider, and a flat XZ range check can't be starved by a cluttered
        /// room the way a fixed-size overlap buffer can).</summary>
        private void TickCatch()
        {
            if (_held.Count >= _capacity) return;

            float radiusSq = _radius * _radius;
            Vector3 origin = transform.position;
            IReadOnlyList<RobotEnemy> active = RobotEnemy.Active;
            for (int i = 0; i < active.Count && _held.Count < _capacity; i++)
            {
                RobotEnemy robot = active[i];
                if (robot == null || !robot.IsTrapCatchable) continue;

                Vector3 rp = robot.transform.position;
                float dx = rp.x - origin.x, dz = rp.z - origin.z;
                if (dx * dx + dz * dz > radiusSq) continue;

                robot.BeginTrapHold();
                _held.Add(robot);
                _dragFrom[robot] = rp;
                _dragElapsed[robot] = 0f;
                if (_sinceFirstCatch < 0f) _sinceFirstCatch = _sinceDrop;
            }
        }

        /// <summary>Cosmetic drag-in over <see cref="DragDuration"/> to within <see cref="HoldDistance"/>
        /// of the core (spec) — purely a position tween; the robot is already frozen/untargetable from
        /// the instant <see cref="RobotEnemy.BeginTrapHold"/> ran in <see cref="TickCatch"/>.</summary>
        private void TickDrag(float dt)
        {
            Vector3 origin = transform.position;
            for (int i = 0; i < _held.Count; i++)
            {
                RobotEnemy robot = _held[i];
                if (robot == null) continue;

                float t = Mathf.Clamp01((_dragElapsed[robot] += dt) / DragDuration);
                Vector3 from = _dragFrom[robot];
                Vector3 toOrigin = from - origin; toOrigin.y = 0f;
                Vector3 target = toOrigin.sqrMagnitude > 0.0001f
                    ? origin + toOrigin.normalized * HoldDistance
                    : origin + Vector3.forward * HoldDistance;
                target.y = from.y;
                robot.transform.position = Vector3.Lerp(from, target, t);
            }
        }

        private void BeginConversion()
        {
            _converting = true;
            for (int i = 0; i < _held.Count; i++) _held[i]?.BeginTrapConversion();
        }

        private void TickConversion(float dt)
        {
            bool allDone = true;
            for (int i = 0; i < _held.Count; i++)
            {
                RobotEnemy robot = _held[i];
                if (robot == null) continue;
                if (!robot.TickTrapConversion(dt)) allDone = false;
            }
            TickTethers();

            if (allDone) Despawn(true);
        }

        private void Despawn(bool converted)
        {
            if (_despawned) return;
            _despawned = true;
            _onDespawn?.Invoke(converted);
            // Application.isPlaying guard: see DestroyCollider's own doc comment — an EditMode test
            // drives this same despawn path for real.
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        // --- Visuals (INDICATIVE — greybox, per the ticket's own build-mode line) --------------------

        private void BuildVisuals()
        {
            gameObject.AddComponent<KeepsOwnMaterial>();

            var ringGo = new GameObject("Trap Ring", typeof(MeshFilter), typeof(MeshRenderer));
            ringGo.transform.SetParent(transform, false);
            ringGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            ringGo.AddComponent<KeepsOwnMaterial>();
            var ringMesh = new Mesh { name = "TrapRing" };
            BuildFlatQuad(ringMesh, _radius * 2f);
            ringGo.GetComponent<MeshFilter>().sharedMesh = ringMesh;
            var ringRenderer = ringGo.GetComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = VfxMaterials.AlphaBlendTinted(VfxMaterials.Ring(128), RingColor);
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Trap Body";
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.6f, 0.15f, 0.6f);
            body.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            DestroyCollider(body);
            body.AddComponent<KeepsOwnMaterial>();
            body.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, BodyColor);

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "Trap Rim";
            rim.transform.SetParent(transform, false);
            rim.transform.localScale = new Vector3(0.65f, 0.03f, 0.65f);
            rim.transform.localPosition = new Vector3(0f, 0.31f, 0f);
            DestroyCollider(rim);
            rim.AddComponent<KeepsOwnMaterial>();
            rim.GetComponent<MeshRenderer>().sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, RimColor);

            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Trap Core";
            core.transform.SetParent(transform, false);
            core.transform.localScale = new Vector3(0.22f, 0.22f, 0.22f);
            core.transform.localPosition = new Vector3(0f, 0.34f, 0f);
            DestroyCollider(core);
            core.AddComponent<KeepsOwnMaterial>();
            core.GetComponent<MeshRenderer>().sharedMaterial = VfxMaterials.AdditiveTinted(CoreColor);
        }

        /// <summary>Strips the default collider <see cref="GameObject.CreatePrimitive"/> attaches — this
        /// is a visual, not a physical obstacle. <see cref="Object.Destroy(Object)"/> logs an error when
        /// called outside Play mode (an EditMode test builds this same trap for real, per this class's
        /// own doc comment), so this picks the right call for whichever mode is actually running, the
        /// same guard <see cref="PlayerAbilities.SpawnBlinkguardBubble"/> already uses.</summary>
        private static void DestroyCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Destroy(collider);
            else DestroyImmediate(collider);
        }

        private void TickTethers()
        {
            Vector3 corePos = transform.position + Vector3.up * 0.34f;
            for (int i = 0; i < _held.Count; i++)
            {
                RobotEnemy robot = _held[i];
                if (robot == null) continue;

                if (!_tethers.TryGetValue(robot, out LineRenderer lr) || lr == null)
                {
                    var go = new GameObject("Trap Tether");
                    go.transform.SetParent(transform, false);
                    go.AddComponent<KeepsOwnMaterial>();
                    lr = go.AddComponent<LineRenderer>();
                    lr.positionCount = 2;
                    lr.startWidth = lr.endWidth = 0.05f;
                    lr.material = VfxMaterials.AdditiveTinted(CoreColor);
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    _tethers[robot] = lr;
                }

                lr.SetPosition(0, corePos);
                lr.SetPosition(1, robot.transform.position + Vector3.up * 0.3f);
            }
        }

        private static void BuildFlatQuad(Mesh mesh, float size)
        {
            float h = size * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h),
                new Vector3(h, 0f, h), new Vector3(h, 0f, -h),
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
        }
    }
}
