using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A Mothership airlock panel (MV-1305) — rule AIRLOCKS (Lee, 27 Oct 2026). A PLAYER-triggered
    /// hazard, same footing as <see cref="Canister"/>: any player-side hit while READY blows it, and the
    /// resulting pull is explicitly licensed to remove robots too (<c>docs/DECISIONS.md</c> §4, "Hazards:
    /// world-run vs player-triggered", Lee 27 Oct 2026).
    ///
    /// Three states on ONE timer, counted from the instant it blows: BLOWING for
    /// <see cref="BlowSeconds"/>, then RECHARGING until <see cref="ReadySeconds"/> TOTAL has elapsed
    /// since the hit (the ticket's own "READY again 20 s after it blew" — not 20 s after the reseal), then
    /// READY again. A hit while not READY is simply ignored — there is no queued "blow again the instant
    /// it reseals".
    ///
    /// While BLOWING, every mover within <see cref="PullRadius"/> is pulled toward this panel — a robot
    /// via <see cref="RobotPullAt"/> (consulted from <c>RobotEnemy.ApplyAirlockPull</c>) and Max via
    /// <see cref="MaxPullAt"/> (consulted from <c>PlayerController.TickAirlockPull</c>), the same
    /// "mover queries a static source, caller integrates a velocity" contract
    /// <see cref="MaxWorlds.Arena.MapCurrents.DriftAt"/>/<see cref="MaxWorlds.Bosses.AnchorheadBoss.WhirlpoolDriftAt"/>
    /// already use — at different speeds per mover, which is why this is its OWN query rather than one
    /// more source summed into <see cref="MaxWorlds.Arena.MapCurrents"/> (that query returns a single
    /// drift every mover reads identically). A robot that closes within <see cref="KillRadius"/> is blown
    /// out entirely (<see cref="AnyKillZoneContains"/>, read by <c>RobotEnemy.ApplyAirlockPull</c>, which
    /// carries the MV-1293 ruling's own citation). Max is pulled, never killed.
    /// </summary>
    [PerfSection("factories")]
    public sealed class AirlockPanel : MonoBehaviour, IDamageable
    {
        public enum PanelState { Ready, Blowing, Recharging }

        // --- Ticket's own numbers ---
        private const float BlowSeconds = 4f;
        private const float ReadySeconds = 20f; // TOTAL since the hit, not 20s after the reseal
        private const float PullRadius = 12f;
        private const float KillRadius = 1.5f;
        private const float RobotPullSpeed = 4f;
        private const float MaxPullSpeed = 3f;
        private const float FlashesPerSecond = 3f; // ticket: "<= 3 flashes/s"

        private static readonly Color HullColor = new Color(0.30f, 0.32f, 0.35f);     // greybox hull steel
        private static readonly Color HazardColor = new Color(0.95f, 0.45f, 0.08f);   // orange/black edging
        private static readonly Color ReadyLightColor = Color.white;
        private static readonly Color BlowingLightColor = new Color(0.95f, 0.45f, 0.08f);
        private static readonly Color RechargingLightColor = new Color(0.10f, 0.10f, 0.12f);
        private static readonly Color SuckVfxColor = new Color(1f, 1f, 1f, 0.85f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Renderer _lightRenderer;
        private MaterialPropertyBlock _lightMpb;

        /// <summary>Seconds since this panel last blew. Starts at +infinity — "never blown", which
        /// reads as READY (same as any value &gt;= <see cref="ReadySeconds"/>) without a separate bool to
        /// keep in sync.</summary>
        private float _elapsedSinceBlow = float.PositiveInfinity;

        /// <summary>A panel is never destroyed, only cycled — always a valid <see cref="IDamageable"/>
        /// hit target.</summary>
        public bool IsAlive => true;

        /// <summary>Team.Enemy, not Neutral: the Water Blaster (Team.Player) can hit it, a robot's own
        /// attack cannot — ticket's own "any PLAYER-side hit" — same reasoning <see cref="Pylon.Team"/>
        /// already documents for the same player-side-only gate.</summary>
        public Team Team => Team.Enemy;

        /// <summary>This panel's stable id — same "a checkpoint could persist/restore by it" footing as
        /// <see cref="Pylon.Id"/>/<see cref="Canister.Id"/>, even though nothing reads it back yet.</summary>
        public string Id { get; private set; } = string.Empty;

        public void SetId(string id) => Id = id ?? string.Empty;

        /// <summary>Resolved, not stored — the one place the three states are told apart, so
        /// <see cref="Hit"/>/<see cref="Tick"/>/the pull queries below can never disagree about which
        /// one is current.</summary>
        public PanelState State =>
            _elapsedSinceBlow >= ReadySeconds ? PanelState.Ready :
            _elapsedSinceBlow < BlowSeconds ? PanelState.Blowing : PanelState.Recharging;

        public bool IsBlowing => State == PanelState.Blowing;

        // --- Static registry — same "the map builds them, Build() registers them, a fresh level wipes
        // them" shape as Pylon.Registered/Canister.Registered. ---
        private static readonly List<AirlockPanel> Registered = new List<AirlockPanel>(4);

        /// <summary>Wipe the registry. Called by <see cref="MaxWorlds.Arena.Map.MapRuntime.Build"/> when
        /// a level starts building — see <see cref="Pylon.ResetRegistry"/>'s own doc comment for why
        /// this can't be named <c>Reset</c>.</summary>
        public static void ResetRegistry() => Registered.Clear();

        private void Awake() => Build();

        /// <summary>Construct the greybox body. Exposed publicly, same reasoning as
        /// <see cref="Pylon.Build"/>/<see cref="Canister.Build"/> — Awake never runs as a side effect of
        /// AddComponent outside Play mode, so an EditMode test calls this directly.</summary>
        public void Build()
        {
            if (_lightRenderer != null) return; // already built

            BuildBody();

            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDestroy() => Registered.Remove(this);

        private void BuildBody()
        {
            var bodyRenderer = GetComponent<Renderer>();
            if (bodyRenderer != null)
            {
                Material hull = MaterialLibrary.Tinted(SurfaceKind.Metal, HullColor);
                if (hull != null) bodyRenderer.sharedMaterial = hull;
            }

            Transform bodyRoot = ParentScale.MakeMetreSpace(new GameObject("Body").transform, transform);

            // Hazard edging — a thin orange/black-read stripe along the door's top and bottom edge
            // (the image's own "orange/black hazard edging"). Cosmetic only: no collider.
            BuildStripe(bodyRoot, new Vector3(0f, 0.46f, 0f));
            BuildStripe(bodyRoot, new Vector3(0f, -0.46f, 0f));

            // Status light — the one element whose colour actually encodes state (ticket: "white steady
            // = READY; flashing orange = BLOWING; dark ... = RECHARGING").
            var lightGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lightGo.name = "StatusLight";
            var lightCol = lightGo.GetComponent<Collider>();
            if (lightCol != null) Object.DestroyImmediate(lightCol); // cosmetic only — the door's own collider is the real one
            lightGo.transform.SetParent(bodyRoot, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.52f);
            lightGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lightGo.transform.localScale = new Vector3(0.3f, 0.04f, 0.3f);
            _lightRenderer = lightGo.GetComponent<MeshRenderer>();
            Material light = MaterialLibrary.Tinted(SurfaceKind.Metal, ReadyLightColor);
            if (light != null) _lightRenderer.sharedMaterial = light;
            _lightMpb = new MaterialPropertyBlock();

            UpdateLight();
        }

        private static void BuildStripe(Transform bodyRoot, Vector3 localPos)
        {
            var stripeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripeGo.name = "HazardStripe";
            var stripeCol = stripeGo.GetComponent<Collider>();
            if (stripeCol != null) Object.DestroyImmediate(stripeCol);
            stripeGo.transform.SetParent(bodyRoot, false);
            stripeGo.transform.localPosition = localPos;
            stripeGo.transform.localScale = new Vector3(1.02f, 0.08f, 1.04f);
            Material hazard = MaterialLibrary.Tinted(SurfaceKind.Metal, HazardColor);
            if (hazard != null) stripeGo.GetComponent<MeshRenderer>().sharedMaterial = hazard;
        }

        /// <summary><see cref="IDamageable.TakeDamage"/> — the amount/direction/source carried by
        /// <paramref name="info"/> are irrelevant (ticket: "ANY player-side hit"); only that a hit landed
        /// at all. <see cref="Team"/> already restricts this to the player's own weapons.</summary>
        public void TakeDamage(in DamageInfo info) => Hit();

        /// <summary>Blows the panel if (and only if) it is currently READY — a hit while BLOWING or
        /// RECHARGING is simply ignored (ticket AC: "a second hit 5 s later does nothing"). Public so an
        /// EditMode test can drive it directly, same convention as every other post-build trigger in this
        /// codebase.</summary>
        public void Hit()
        {
            if (State != PanelState.Ready) return;
            _elapsedSinceBlow = 0f;
        }

        /// <summary>Advances the state timer and the light's look — dt-parameterised, called from this
        /// object's own <c>Update</c> in Play mode and directly by an EditMode test, same "Update calls it
        /// with Time.deltaTime, a test calls it directly" idiom <see cref="Canister.Tick"/> already uses.</summary>
        public void Tick(float dt)
        {
            if (_elapsedSinceBlow < ReadySeconds) _elapsedSinceBlow += dt;
            UpdateLight();
        }

        private void UpdateLight()
        {
            if (_lightRenderer == null) return;

            Color c;
            switch (State)
            {
                case PanelState.Blowing:
                    bool flashOn = Mathf.FloorToInt(_elapsedSinceBlow * FlashesPerSecond) % 2 == 0;
                    c = flashOn ? BlowingLightColor : Color.black;
                    break;
                case PanelState.Recharging:
                    c = RechargingLightColor;
                    break;
                default:
                    c = ReadyLightColor;
                    break;
            }

            _lightRenderer.GetPropertyBlock(_lightMpb);
            _lightMpb.SetColor(BaseColorId, c);
            _lightMpb.SetColor(ColorId, c);
            _lightRenderer.SetPropertyBlock(_lightMpb);
        }

        private void Update() => Tick(Time.deltaTime);

        // ---------------------------------------------------------------- Pull/kill (ticket Change 4)

        /// <summary>This frame's additive airlock pull on a ROBOT at <paramref name="worldPosition"/> — a
        /// velocity (m/s), for <c>RobotEnemy.ApplyAirlockPull</c> to scale by dt and feed through its own
        /// SafeMove. Summed across however many panels are BLOWING and within range at once (several
        /// sources add — same rule <see cref="MaxWorlds.Arena.MapGeometry.CurrentDriftAt"/> already
        /// follows), though in practice this project never authors two airlocks close enough to overlap.</summary>
        public static Vector3 RobotPullAt(Vector3 worldPosition) => PullAt(worldPosition, RobotPullSpeed);

        /// <summary>Same as <see cref="RobotPullAt"/>, at Max's own (slower) pull speed — consulted by
        /// <c>PlayerController.TickAirlockPull</c>.</summary>
        public static Vector3 MaxPullAt(Vector3 worldPosition) => PullAt(worldPosition, MaxPullSpeed);

        private static Vector3 PullAt(Vector3 worldPosition, float speed)
        {
            Vector3 pull = Vector3.zero;
            for (int i = 0; i < Registered.Count; i++)
            {
                AirlockPanel p = Registered[i];
                if (p == null || !p.IsBlowing) continue;

                Vector3 toPanel = p.transform.position - worldPosition;
                toPanel.y = 0f;
                float dist = toPanel.magnitude;
                if (dist <= 0.001f || dist > PullRadius) continue;

                pull += (toPanel / dist) * speed;
            }
            return pull;
        }

        /// <summary>True the instant <paramref name="worldPosition"/> is within ANY currently-BLOWING
        /// panel's <see cref="KillRadius"/> — read by <c>RobotEnemy.ApplyAirlockPull</c>, which is the
        /// actual MV-1293-cited kill call; this method only answers the distance question.</summary>
        public static bool AnyKillZoneContains(Vector3 worldPosition)
        {
            for (int i = 0; i < Registered.Count; i++)
            {
                AirlockPanel p = Registered[i];
                if (p == null || !p.IsBlowing) continue;

                Vector3 toPanel = p.transform.position - worldPosition;
                toPanel.y = 0f;
                if (toPanel.magnitude <= KillRadius) return true;
            }
            return false;
        }

        /// <summary>The short suck-out VFX the ticket calls for, played at the point a robot was blown
        /// out — a ring collapsing inward (the inverse of <see cref="Canister"/>'s own expanding blast
        /// ring), self-contained and not owned by the robot it played for (which is already Dead/disabled
        /// by the time this runs). Called once by <c>RobotEnemy.ApplyAirlockPull</c>, never from this
        /// class — it has no idea when a robot dies, only where the panels are.</summary>
        public static void PlaySuckVfx(Vector3 point)
        {
            GroundRing ring = GroundRing.Create("AirlockSuckVfx");
            ring.gameObject.AddComponent<SuckVfxLifetime>().Begin(ring, point);
        }

        private const float SuckVfxStartRadius = 1.2f;
        private const float SuckVfxLife = 0.25f;

        /// <summary>Shrinks a ring from <see cref="SuckVfxStartRadius"/> to nothing over
        /// <see cref="SuckVfxLife"/>, fading it out, then destroys its own GameObject — same
        /// self-contained shape as <see cref="Canister"/>'s own blast-ring lifetime component, run in
        /// reverse (collapsing inward reads as "sucked in", not "blown outward").</summary>
        [PerfSection("factories")]
        private sealed class SuckVfxLifetime : MonoBehaviour
        {
            private GroundRing _ring;
            private Vector3 _origin;
            private float _t;

            public void Begin(GroundRing ring, Vector3 origin)
            {
                _ring = ring;
                _origin = origin;
                _ring.Show(_origin, SuckVfxStartRadius, SuckVfxColor);
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float f = Mathf.Clamp01(_t / SuckVfxLife);
                _ring.Show(_origin, Mathf.Lerp(SuckVfxStartRadius, 0f, f),
                    new Color(SuckVfxColor.r, SuckVfxColor.g, SuckVfxColor.b, SuckVfxColor.a * (1f - f)));
                if (f >= 1f)
                {
                    _ring.Hide();
                    Destroy(gameObject);
                }
            }
        }
    }
}
