using System;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Feel;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.Arena
{
    /// <summary>
    /// A gated room boundary with its own HP, broken by sustained PRIMARY-weapon fire (v0.5 recut spec
    /// §1, WV-222). Unlike the scene-adopted <see cref="SubZoneGate"/> — which opens on a FACTORY's
    /// death and never takes damage itself — an area gate is a structure in its own right, the same
    /// shape of thing a <see cref="MowerHutch"/> is: it owns a <see cref="DestructibleHealth"/> and
    /// opens on ITS OWN destruction.
    ///
    /// Only <see cref="DamageSource.PrimaryWeapon"/> chips it — a Water Balloon or a future ability
    /// cannot skip the gate's pacing beat. Max HP is derived from <c>gateBreakSeconds</c> so continuous
    /// primary fire empties it in ~that many seconds (spec default 4 s); moving the Settings panel's
    /// "Gate break secs" slider changes how long a FRESHLY BUILT gate takes to break, not just a
    /// description of one (matches <c>gateBreakSeconds</c>/<c>gateRequiresClear</c> being read here,
    /// not just stored — WV-234 landed the settings, this ticket is what spends them).
    /// </summary>
    public sealed class AreaGate : MonoBehaviour, IDamageable, IHealthReadout
    {
        // Assumed sustained primary DPS used to size gate HP from gateBreakSeconds — the primary's own
        // authored base tick rate (damagePerTick 4 / fireInterval 0.1 s = 40 dps, WaterBlaster.cs). A
        // fixed reference number rather than one read off a live WaterBlaster instance: an area gate
        // can exist in a map with no player in it at all (the map editor, a test), and the promise is
        // about FOCUSED fire at the weapon's base rate, not whatever upgrades a given run happens to
        // carry.
        public const float AssumedPrimaryDps = 40f;

        // --- Health bar (MV-265): the gate is deliberately narrower than its own body — like the
        // Mower Hutch's bar (BarWorldWidth vs. a 3 m body), it's a readout of the damage you're
        // doing, not a piece of architecture. Fixed rather than derived from the gate's own (very
        // variable, 1-11 m) width, so a wide boss gate doesn't drown the room in bar.
        private const float BarWorldWidth = 1.8f;
        // Clearance above the gate's own top edge, in metres — same margin MowerHutch gives its bar
        // (BarHeightAboveCentre - halfHeight = 0.7) so the two structure bars read consistently.
        private const float BarHeightClearance = 0.7f;

        // --- Hinge-open animation (MV-265): a destroyed gate swings on one vertical edge like a
        // door, the visual half of "the gate is passable" (the threshold collider drops the instant
        // it dies — see Open() — same split as SubZoneGate's sink; MV-386 gave the leaf itself a
        // second, always-solid collider that rides along with this swing). Past 90 degrees so the
        // slab reads as deliberately flung open rather than merely ajar.
        private const float HingeSwingDegrees = 100f;
        private const float HingeDuration = 0.5f;

        private DestructibleHealth _health;
        private MaterialPropertyBlock _reefMpb;

        // --- MV-386: the closed slab used to be ONE collider that Open() disabled outright, so the
        // instant a gate broke, the visibly still-swinging (then fully open) door leaf had zero
        // collision forever — you could walk straight through the panel itself, not just the gap it
        // used to block. Split in two: _thresholdCollider stands in for "the doorway while shut" and
        // is what actually drops on Open() (so the gap reads passable the instant the gate breaks,
        // same UX as before); _leafCollider is the gate's own collider and is now never disabled — it
        // sits on the same transform the hinge swing animates, so it just keeps being solid wherever
        // that swing leaves the panel.
        private Collider _leafCollider;
        private Collider _thresholdCollider;

        private bool _hinging;
        private float _hingeT;
        private Vector3 _hingePivot;
        private Vector3 _closedPosition;
        private Quaternion _closedRotation;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // the primary (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

        // --- IHealthReadout (YT-111): what the gate's floating bar reads. ---
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health?.Current ?? 0f;

        private const string GateReadoutName = "GATE";

        private int _lockDestroyed;
        private int _lockTotal;

        /// <summary>How many of the sheds this gate is waiting on are down (MV-571). Pushed in by
        /// <see cref="MaxWorlds.Arena.WorldRunner"/>, which already polls the condition every tick —
        /// the gate does not reach into the supply line itself.</summary>
        public void SetLockProgress(int destroyed, int total)
        {
            _lockDestroyed = destroyed;
            _lockTotal = total;
        }

        /// <summary>What the gate's floating label reads (MV-571). A condition-locked gate has no HP
        /// worth showing, but it still owes the player a reason it won't open — the shed count while
        /// locked, its ordinary name once the condition resolves.</summary>
        public string ReadoutName =>
            Locked && _lockTotal > 0 ? $"SHEDS  {_lockDestroyed} / {_lockTotal}"
            : Locked                 ? "LOCKED"
                                     : GateReadoutName;

        /// <summary>The gate's current HP ceiling — <c>gateBreakSeconds</c> (at build time) times
        /// <see cref="AssumedPrimaryDps"/>. Exposed so a test (or a future HUD bar) can read it without
        /// reverse-engineering the constant.</summary>
        public float MaxHp => _health?.Max ?? 0f;

        /// <summary>The way is open — Max can walk (and shoot) through. True the instant HP hits zero.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>MV-713: re-skins this gate for World 3's Reef biome ("power hatch") — a
        /// MaterialPropertyBlock tint on the leaf's own renderer, the same non-destructive idiom
        /// <see cref="MaxWorlds.Factories.MowerHutch.ApplyReefSkin"/> uses. Touches no health, no
        /// <c>_leafCollider</c>/<c>_thresholdCollider</c>, and no hinge geometry — nothing here can move
        /// the numbers AC2's "collider and tuning unchanged" guard checks.</summary>
        public void ApplyReefSkin()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) return;
            if (_reefMpb == null) _reefMpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(_reefMpb);
            _reefMpb.SetColor("_BaseColor", WorldMaterials.ReefHazard);
            rend.SetPropertyBlock(_reefMpb);
        }

        // --- MV-759: World 2's gate art — a round portal ring with two doors that slide apart,
        // built entirely from this gate's own closed-pose geometry, exactly the way ApplyReefSkin
        // re-skins the leaf above. The mechanic (health, threshold drop timing, the hinge-swing
        // collision leaf itself) is completely untouched: this only hides the plain slab's renderer
        // and adds art riding on top, wired off the gate's existing Opened/Closed/LockedChanged
        // events so it never needs a field of its own to track state Locked/IsOpen already carry.

        private const float StormdrainDressingProud = 0.04f; // same AntiZFightMargin idiom as MapRuntime.BuildAreaGate
        private const float DoorSlideDuration = 0.45f;

        private static readonly Color StormdrainLampLocked = new Color(0.90f, 0.15f, 0.10f);
        // Deliberately brighter/more saturated than StormdrainKit.Hazard (the ring's own rust-adjacent
        // warm tone) — the QA capture pass caught the Hazard shade blending into the surrounding rust
        // ring under this world's dim lighting, so a "closed and unlockable" gate read as unlit rather
        // than amber (readability beats palette-matching here, per the Craft Bible's tie-breaker order).
        private static readonly Color StormdrainLampAmber = new Color(1.0f, 0.8f, 0.25f);
        private static readonly Color StormdrainLampOpen = new Color(0.25f, 0.90f, 0.35f);

        private GameObject _dressingRoot;
        private GameObject _leafL, _leafR;
        private GameObject _lampGlow;
        private GameObject _hazardStripe;
        private Vector3 _leafClosedLocalPosL, _leafClosedLocalPosR;
        private float _doorSlideDistance;
        private AnimSequence _doorSlide;
        private bool _doorSlideOpening;

        /// <summary>Builds World 2's portal ring + sliding double doors around this gate's doorway and
        /// hides the plain slab CreatePrimitive gave it — called once, from <see cref="BackyardPath"/>'s
        /// per-world dressing pass, only for a World 2 load (mirrors how <see cref="ApplyReefSkin"/> is
        /// called only for World 3). Idempotent: a second call is a no-op, matching every other
        /// dressing pass in this codebase.</summary>
        public void ApplyStormdrainGateSkin()
        {
            if (_dressingRoot != null) return;

            var rend = GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;

            float width = transform.localScale.x;
            float height = transform.localScale.y;
            float depth = transform.localScale.z;

            _dressingRoot = new GameObject(gameObject.name + " (Stormdrain Gate Dressing)");
            _dressingRoot.transform.SetParent(transform.parent, worldPositionStays: false);
            _dressingRoot.transform.SetPositionAndRotation(transform.position, transform.rotation);
            // Same marker StormdrainDressing.Dress puts on its own root: WorldMaterials' generic
            // shape-classified sweep (KeepsOwnMaterial's own doc: "the runtime sweep re-dresses anything
            // that appears mid-run") otherwise repaints every box built here, lamp included, with its
            // own flat biome tint the instant it appears -- which is exactly why the lamp read as a
            // neutral grey prop instead of amber/red/green in this ticket's own QA capture, no matter
            // what colour RefreshStormdrainLamp had just set (caught by diagnosing MV-759-gate-closed.png
            // down to the actual material name and colour on the built lamp, not by eye).
            _dressingRoot.AddComponent<KeepsOwnMaterial>();

            BuildStormdrainPortalRing(_dressingRoot.transform, width, height, depth);
            BuildStormdrainDoorLeaves(_dressingRoot.transform, width, height, depth);
            BuildStormdrainLampAndHazard(_dressingRoot.transform, width, height, depth);

            Opened += OnStormdrainOpened;
            Closed += OnStormdrainClosed;
            LockedChanged += OnStormdrainLockedChanged;

            RefreshStormdrainLamp();
        }

        /// <summary>The arch: a segmented rib stepped around the top of the opening (a true torus is
        /// not worth a mesh generator here — ticket's own call), plus a rust inner lip so the ring
        /// reads as one built object rather than a row of loose boxes.
        ///
        /// An ELLIPTICAL arc, not a circular one: a real World 2 doorway is wide and short (the shipped
        /// map's gates run ~3.8 x 1.5 x 0.64), so a single circular radius either overshoots the opening
        /// (too tall) or, worse, drives the spring line negative once the radius exceeds the doorway's
        /// own height — every jamb and rib collapsed to a sliver against the floor and the whole ring
        /// read as one flat slab (caught opening MV-759-gate-closed.png during this ticket's own QA
        /// pass). Decoupling the horizontal and vertical extents keeps the arc inside the doorway at
        /// any aspect ratio: it always spans the full width and rises through a fixed FRACTION of the
        /// height, never more.</summary>
        private void BuildStormdrainPortalRing(Transform parent, float width, float height, float depth)
        {
            var ring = new GameObject("Portal Ring");
            ring.transform.SetParent(parent, false);

            // MapRuntime.BuildAreaGate centres the gate's own cube on its doorway (GroundedCenter-style
            // Y), so THIS transform's local Y=0 is the doorway's VERTICAL CENTRE, not its floor — every
            // position below is built relative to that centre (floor = -halfHeight, top = +halfHeight),
            // not relative to 0 = floor as an early pass here assumed. That earlier assumption put the
            // whole ring, both leaves and the lamp roughly one half-height too high, largely into the
            // wall above the doorway instead of around it (caught opening MV-759-gate-closed.png during
            // this ticket's own QA pass — the doorway gap below the visible mass was the REAL opening).
            float halfHeight = height * 0.5f;

            float ribDepth = depth + StormdrainDressingProud * 2f;
            float horizontalRadius = width * 0.5f + 0.15f;
            float archRise = height * 0.6f;                 // the top 60% of the doorway curves; the rest is jamb
            float springY = height - archRise;               // always > 0 -- never depends on horizontalRadius
            float ribSize = Mathf.Clamp(Mathf.Min(width, height) * 0.16f, 0.12f, 0.4f);

            const int segments = 8;
            for (int i = 0; i <= segments; i++)
            {
                // Steps across the arch (180 deg at the left jamb to 0 deg at the right jamb) — the
                // sides below the spring line are plain jambs, not curved.
                float angle = Mathf.Lerp(180f, 0f, i / (float)segments) * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Cos(angle) * horizontalRadius,
                    springY - halfHeight + Mathf.Sin(angle) * archRise, 0f);
                StormdrainKit.Box(ring.transform, $"Rib{i}", pos, new Vector3(ribSize, ribSize, ribDepth),
                    StormdrainKit.Rust, SurfaceKind.Metal);
            }

            // Jambs: the straight run of the ring below the spring line, on each side of the opening.
            StormdrainKit.Box(ring.transform, "Jamb L", new Vector3(-horizontalRadius, springY * 0.5f - halfHeight, 0f),
                new Vector3(ribSize, springY, ribDepth), StormdrainKit.Rust, SurfaceKind.Metal);
            StormdrainKit.Box(ring.transform, "Jamb R", new Vector3(horizontalRadius, springY * 0.5f - halfHeight, 0f),
                new Vector3(ribSize, springY, ribDepth), StormdrainKit.Rust, SurfaceKind.Metal);

            // Inner lip: a darker rust FRAME sitting proud of the wall, right against the doorway edge —
            // four thin strips around the opening's perimeter (same hollow-border idiom as
            // StormdrainKit.DressSludgeTile's own Lip N/S/E/W), never a filled slab: a filled box here
            // would sit in front of the ring and both leaves and hide the whole feature behind a plain
            // rectangle (the same MV-759-gate-closed.png QA pass caught this too, before the ellipse fix).
            float lipThickness = ribSize * 0.5f;
            float lipDepth = ribDepth * 0.5f;
            StormdrainKit.Box(ring.transform, "Inner Lip Top", new Vector3(0f, halfHeight + lipThickness * 0.5f, 0f),
                new Vector3(width + lipThickness * 2f, lipThickness, lipDepth), StormdrainKit.RustDark, SurfaceKind.Metal);
            StormdrainKit.Box(ring.transform, "Inner Lip L", new Vector3(-width * 0.5f - lipThickness * 0.5f, 0f, 0f),
                new Vector3(lipThickness, height, lipDepth), StormdrainKit.RustDark, SurfaceKind.Metal);
            StormdrainKit.Box(ring.transform, "Inner Lip R", new Vector3(width * 0.5f + lipThickness * 0.5f, 0f, 0f),
                new Vector3(lipThickness, height, lipDepth), StormdrainKit.RustDark, SurfaceKind.Metal);
        }

        /// <summary>Two half-leaves meeting at the doorway's centre line, each chamfered on its inner
        /// edge so the closed pair reads as one round plug in the ring. Closed local positions are
        /// captured here — <see cref="AdvanceStormdrainSlide"/> always lerps from/to these, never a
        /// live, possibly-mid-slide position, so a second open/close cycle can never drift.</summary>
        private void BuildStormdrainDoorLeaves(Transform parent, float width, float height, float depth)
        {
            float leafWidth = width * 0.5f;
            Vector3 leafSize = new Vector3(leafWidth, height, depth);

            _leafL = BuildStormdrainLeaf(parent, "Leaf L", -leafWidth * 0.5f, leafSize, chamferSign: 1f);
            _leafR = BuildStormdrainLeaf(parent, "Leaf R", leafWidth * 0.5f, leafSize, chamferSign: -1f);

            _leafClosedLocalPosL = _leafL.transform.localPosition;
            _leafClosedLocalPosR = _leafR.transform.localPosition;

            // MV-759 fix comment: a slide of only each leaf's own half-width left it straddling the
            // span AreaGate.OpenLeafSpan still reports as covered by the (untouched, still hinge-
            // swinging) collision leaf — that leaf's swing pivots on the doorway's own left jamb, so
            // its footprint sits close against the doorway rather than clear of it. A full width's
            // worth of slide tucks each leaf comfortably behind its own side of the ring instead.
            _doorSlideDistance = width;
        }

        private static GameObject BuildStormdrainLeaf(Transform parent, string name, float localX,
                                                       Vector3 size, float chamferSign)
        {
            // Y=0, not size.y*0.5 -- this transform's local origin is the doorway's vertical CENTRE
            // (see BuildStormdrainPortalRing's halfHeight note), and a leaf spans the full height
            // centred on that same origin.
            GameObject leaf = StormdrainKit.Box(parent, name, new Vector3(localX, 0f, 0f),
                size, StormdrainKit.KerbConcrete, SurfaceKind.Metal);

            // Chamfer: a thin rust strip along the inner edge, angled slightly, so the closed pair
            // reads as one bevelled, round plug rather than two flat slabs butted together. Built under
            // PARENT first, in the same unscaled space as the leaf itself, then reparented onto the leaf
            // with worldPositionStays -- built directly as a child of the leaf, Unity composes its
            // localScale with the leaf's own (which IS the leaf's full width/height/depth), so a "thin
            // strip" came out scaled up by the leaf's own size, dwarfing the whole ring (caught opening
            // MV-759-gate-closed.png during this ticket's own QA pass). Reparenting with
            // worldPositionStays recomputes the local transform to compensate, and the chamfer still
            // slides with its leaf afterwards since it is genuinely that leaf's child from here on.
            var chamfer = StormdrainKit.Box(parent, "Inner Chamfer",
                new Vector3(localX + chamferSign * size.x * 0.42f, 0f, 0f),
                new Vector3(Mathf.Min(size.x * 0.18f, 0.35f), size.y * 0.96f, size.z * 1.08f),
                StormdrainKit.Rust, SurfaceKind.Metal);
            chamfer.transform.rotation *= Quaternion.Euler(0f, chamferSign * 8f, 0f);
            chamfer.transform.SetParent(leaf.transform, worldPositionStays: true);

            return leaf;
        }

        /// <summary>The status lamp (red/amber/green off <see cref="Locked"/>/<see cref="IsOpen"/>,
        /// never a field of its own — <see cref="RefreshStormdrainLamp"/> recomputes it every time) and
        /// the hazard-stripe band across the seam, shown only while locked.</summary>
        private void BuildStormdrainLampAndHazard(Transform parent, float width, float height, float depth)
        {
            // A small unlit CUBE, not StormdrainKit.Glow's Quad: a Quad is single-sided, and this
            // ticket's own QA capture pass caught it reading invisible from the front on the specific
            // gate the capture preset shoots regardless of which way it was rotated to face (the "lamp"
            // that looked lit in that screenshot was the pre-existing health pill floating above the
            // gate, not this object at all). A cube has no facing to get wrong.
            _lampGlow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _lampGlow.name = "Gate Lamp";
            _lampGlow.transform.SetParent(parent, false);
            // height*0.5 (the doorway's top, relative to this transform's centre-origin) + a small rise
            // above it, not height+0.22 against a floor origin this transform doesn't have. Well proud
            // of the wall's own coping (depth*0.5 + 0.3, not + StormdrainDressingProud) so a thick wall
            // crest running across the top of the doorway can never sit in front of it.
            _lampGlow.transform.localPosition = new Vector3(0f, height * 0.5f + 0.22f, depth * 0.5f + 0.3f);
            _lampGlow.transform.localScale = new Vector3(0.6f, 0.6f, 0.3f);
            StormdrainKit.Strip(_lampGlow);
            _lampGlow.GetComponent<Renderer>().sharedMaterial = StormdrainKit.Unlit(StormdrainLampAmber, "GateLamp");

            _hazardStripe = StormdrainKit.Box(parent, "Hazard Stripe", Vector3.zero,
                new Vector3(width * 1.02f, 0.2f, depth + StormdrainDressingProud * 2f),
                StormdrainKit.Hazard, SurfaceKind.Metal);
        }

        private void OnStormdrainLockedChanged(bool locked) => RefreshStormdrainLamp();

        private void OnStormdrainOpened()
        {
            RefreshStormdrainLamp();
            StartStormdrainSlide(opening: true);
        }

        private void OnStormdrainClosed()
        {
            RefreshStormdrainLamp();
            StartStormdrainSlide(opening: false);
        }

        private void RefreshStormdrainLamp()
        {
            if (_lampGlow == null) return;

            Color color = Locked ? StormdrainLampLocked : IsOpen ? StormdrainLampOpen : StormdrainLampAmber;
            Material mat = StormdrainKit.Unlit(color, "GateLamp");
            foreach (var rend in _lampGlow.GetComponentsInChildren<Renderer>())
                rend.sharedMaterial = mat;

            if (_hazardStripe != null) _hazardStripe.SetActive(Locked);
        }

        private void StartStormdrainSlide(bool opening)
        {
            _doorSlideOpening = opening;
            _doorSlide = new AnimSequence(new[] { new AnimStep(0f, DoorSlideDuration, AnimEase.OutQuad) });
        }

        /// <summary>Ticks the door slide by an explicit <paramref name="dt"/> — called from
        /// <see cref="Update"/> with <c>Time.deltaTime</c>, exactly like the pre-existing hinge swing
        /// above, and invoked directly (by reflection) from an EditMode test with a controlled step,
        /// since neither this project's synchronous EditMode harness nor <c>Time.deltaTime</c> ticks a
        /// frame there.</summary>
        private void AdvanceStormdrainSlide(float dt)
        {
            if (_doorSlide == null || _leafL == null || _leafR == null) return;

            _doorSlide.Tick(dt);
            float u = _doorSlide.Progress(0);
            float offset = _doorSlideOpening ? Mathf.Lerp(0f, _doorSlideDistance, u) : Mathf.Lerp(_doorSlideDistance, 0f, u);

            _leafL.transform.localPosition = _leafClosedLocalPosL + Vector3.left * offset;
            _leafR.transform.localPosition = _leafClosedLocalPosR + Vector3.right * offset;
        }

        /// <summary>The world-fixed stand-in for the doorway while this gate is shut (MV-386) — the
        /// object <see cref="CoverLayer.Assign"/> should be pointed at instead of the gate itself, since
        /// this is the collider that actually drops on <see cref="Open"/> (the gate's own collider does
        /// not any more; it stays solid and keeps following the hinge swing). Null only if Awake has not
        /// run yet.</summary>
        public GameObject ThresholdObject => _thresholdCollider != null ? _thresholdCollider.gameObject : null;

        /// <summary>Whether this gate refuses primary damage until its room reads clear of robots
        /// (<c>gateRequiresClear</c>). Robot/room integration is WV-223's, not this ticket's — until
        /// something wires <see cref="RoomClear"/>, a gate with this on is treated as already clear
        /// (there is nothing to be blocked on), so the mechanic stays fully testable in isolation.</summary>
        public bool RequiresClear { get; private set; }

        /// <summary>Reports whether this gate's room is clear of robots. Left null (always "clear")
        /// until a robot-room system (WV-223) has something real to wire in.</summary>
        public Func<bool> RoomClear;

        /// <summary>Fired once, the instant the gate opens.</summary>
        public event Action Opened;

        /// <summary>Fired the instant this gate is re-shut (MV-448) — <see cref="Reclose"/> is the
        /// only place <see cref="IsOpen"/> ever goes back to false. Mirrors <see cref="Opened"/> so
        /// <see cref="MaxWorlds.Enemies.EnemyNavigation.RegisterGate(string, AreaGate, MapData)"/> can
        /// subscribe the same <see cref="MapRoutes.Forget"/> to it — without this, a route solved
        /// through this gate while it was open stayed cached forever after an arena reset shut it
        /// again (MV-427), and every robot kept walking at a doorway that was no longer there.</summary>
        public event Action Closed;

        /// <summary>Refuses ALL damage regardless of source while true — the boss gate's
        /// <c>opensWith: all-sheds-destroyed</c> lock (World &amp; Difficulty Framework, MV-270). Unlike
        /// <see cref="RequiresClear"/>, which only pauses a normal gate until its room clears, a locked
        /// gate cannot be chipped down by fire at all; only <see cref="ForceOpen"/> opens it, once
        /// whatever external condition it is waiting on (here, <c>SupplyLineNetwork.AllShedsDestroyed</c>)
        /// is met. Every other gate leaves this false and behaves exactly as before.</summary>
        public bool Locked
        {
            get => _locked;
            set
            {
                if (_locked == value) return;
                _locked = value;
                // MV-569: a locked gate takes no damage, so a full, never-moving health bar reads as
                // a broken game rather than as a condition. Hide the bar while locked and show it
                // again the moment the condition resolves and the gate becomes breakable. Set by
                // WorldRunner after Awake, which is why this is a property and not a constructor arg.
                // MV-571: hide only the bar strip, not the label — the label is what tells the player
                // why the gate is shut (SetLockProgress/ReadoutName above).
                if (_healthBar != null) _healthBar.SetBarHiddenKeepLabel(_locked);
                LockedChanged?.Invoke(_locked);
            }
        }
        private bool _locked;

        /// <summary>Fired whenever <see cref="Locked"/> flips — the HUD's cue that this doorway is
        /// waiting on a condition rather than on fire.</summary>
        public event Action<bool> LockedChanged;

        private WorldHealthBar _healthBar;

        /// <summary>World-space direction from the room the player approaches this gate from toward
        /// the room beyond it (MV-320) — set by <see cref="MapRuntime"/> from the link's from/to zone
        /// centres, since a gate itself has no notion of "which side Max is standing on". Left
        /// <see cref="Vector3.zero"/> for a gate built without map context (e.g. a bare EditMode/
        /// PlayMode test fixture), in which case <see cref="SwingSign"/> keeps the old fixed swing.</summary>
        public Vector3 AwayFromPlayerDirection { get; set; }

        // Sign applied to the hinge angle each swing — captured once in StartHingeSwing rather than
        // recomputed every frame in Update, since transform.forward there is mid-swing and no longer
        // reads as "closed".
        private float _hingeSign = 1f;

        private void Awake()
        {
            float breakSeconds = Mathf.Max(0.1f,
                DevTuning.Or(DevTuning.GateBreakSeconds, ArenaTuning.DefaultGateBreakSeconds));
            _health = new DestructibleHealth(breakSeconds * AssumedPrimaryDps);
            _health.Destroyed += Open;

            RequiresClear =
                DevTuning.Or(DevTuning.GateRequiresClear, ArenaTuning.DefaultGateRequiresClear) >= 0.5f;

            _leafCollider = GetComponent<Collider>();

            // MV-378: an area gate exists to physically block Max and robots until it breaks -- a
            // trigger collider would let a CharacterController pass straight through it, so this
            // makes the solid contract explicit rather than relying on CreatePrimitive's default.
            if (_leafCollider != null) _leafCollider.isTrigger = false;

            _thresholdCollider = BuildThresholdCollider();

            // Always shown, not earned by a hit (unlike a robot's) — the player needs to discover
            // the gate is a breakable target in the first place, not just watch it deplete once they
            // already found it (MV-265: Lee couldn't tell the gate was damageable at all). Captured so
            // the Locked property (MV-569) can hide it again for a condition-locked gate, whose bar
            // would otherwise sit full forever under fire it is designed to ignore.
            // MV-740: "GATE 74" — the HP figure WorldHealthBar prints under every unit's name reads
            // fine on a robot but leaks a raw internal number here that a player cannot interpret
            // ("74 means nothing" — Lee). The pill keeps the name and its open/locked state only.
            float halfHeight = transform.localScale.y * 0.5f;
            _healthBar = WorldHealthBar.Attach(gameObject, this, halfHeight + BarHeightClearance,
                                               BarWorldWidth, alwaysShow: true, showNumber: false);
            if (_locked && _healthBar != null) _healthBar.SetBarHiddenKeepLabel(true);
        }

        /// <summary>Builds the world-fixed threshold collider (MV-386) on its own GameObject, sized and
        /// posed to match the gate's CLOSED footprint exactly — read from <c>transform</c> here in
        /// Awake, before any hinge swing has touched it. Parented as a sibling under this gate's own
        /// parent (not under the gate itself), so <see cref="StartHingeSwing"/> rotating THIS transform
        /// later never drags the threshold along with it.</summary>
        private Collider BuildThresholdCollider()
        {
            var threshold = new GameObject(gameObject.name + " (Threshold)");
            threshold.transform.SetParent(transform.parent, worldPositionStays: false);
            threshold.transform.SetPositionAndRotation(transform.position, transform.rotation);
            threshold.transform.localScale = transform.localScale;

            var box = threshold.AddComponent<BoxCollider>();
            box.isTrigger = false;
            return box;
        }

        private void OnDestroy()
        {
            if (_thresholdCollider != null)
            {
                GameObject thresholdObject = _thresholdCollider.gameObject;
                if (Application.isPlaying) Destroy(thresholdObject);
                else DestroyImmediate(thresholdObject);
            }

            // MV-759: the Stormdrain dressing is a sibling (see ApplyStormdrainGateSkin), not a child —
            // same reason the threshold above is parented outside this transform, and the same reason
            // it needs its own explicit teardown here rather than dying with this GameObject for free.
            if (_dressingRoot != null)
            {
                if (Application.isPlaying) Destroy(_dressingRoot);
                else DestroyImmediate(_dressingRoot);
            }
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive || Locked) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;
            if (info.Source != DamageSource.PrimaryWeapon) return; // only sustained primary fire counts
            if (RequiresClear && RoomClear != null && !RoomClear()) return;

            _health.TakeDamage(info.Amount);
        }

        /// <summary>Opens the gate immediately regardless of remaining HP or <see cref="Locked"/> — how
        /// an <c>opensWith</c> condition other than sustained primary fire (currently just
        /// <c>all-sheds-destroyed</c>) actually resolves (MV-270). Goes through the same
        /// <see cref="DestructibleHealth.Destroyed"/> → <see cref="Open"/> path a normal break does, so
        /// the hinge swing and every other break-time effect fire exactly as they would from fire.</summary>
        public void ForceOpen()
        {
            if (!IsAlive) return;
            _health.TakeDamage(_health.Max);
        }

        /// <summary>Restore this gate to intact — a fresh <see cref="DestructibleHealth"/> (never
        /// revives an existing one; that class's one-shot <c>Destroyed</c> contract stays untouched
        /// for every other consumer, e.g. <see cref="MowerHutch"/>), the threshold collider
        /// re-enabled, and the leaf snapped back to its authored closed pose. Used when Max dies and
        /// the arena he died in resets (MV-427) — the gate he broke to get in must block again.
        ///
        /// Only ever called on a gate that has already opened at least once (re-closing an
        /// already-shut gate is a no-op the caller shouldn't need, but this guards it anyway) — never
        /// on a gate whose <c>opensWith</c> is a condition rather than combat (the boss gate); that
        /// distinction is the caller's to make (<see cref="WorldRunner"/>), not this class's.</summary>
        public void Reclose()
        {
            if (!IsOpen) return;

            float breakSeconds = Mathf.Max(0.1f,
                DevTuning.Or(DevTuning.GateBreakSeconds, ArenaTuning.DefaultGateBreakSeconds));
            _health = new DestructibleHealth(breakSeconds * AssumedPrimaryDps);
            _health.Destroyed += Open;

            IsOpen = false;
            _hinging = false;
            if (_thresholdCollider != null) _thresholdCollider.enabled = true;

            // Snap the leaf back to the closed pose StartHingeSwing captured before it ever swung —
            // _leafCollider rides this same transform and was deliberately left solid through the
            // whole swing (MV-386), so restoring the transform is what clears the stale open leaf the
            // ticket calls out, not just re-enabling a collider.
            transform.SetPositionAndRotation(_closedPosition, _closedRotation);

            Closed?.Invoke();
        }

        private void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            // The doorway is passable immediately — same UX as before MV-386, just narrowed to the
            // THRESHOLD alone now: "destroyed" and "the vacated gap is walkable" are never out of sync,
            // whatever the swing is doing visually. _leafCollider is deliberately left alone here: MV-386
            // found that disabling the gate's own collider here made the swinging (then fully open) door
            // panel itself walk-through-able forever, not just the gap it used to seal. It stays enabled
            // and rides the same transform StartHingeSwing animates, so the physical leaf keeps blocking
            // wherever the swing leaves it.
            if (_thresholdCollider != null) _thresholdCollider.enabled = false;

            StartHingeSwing();

            Opened?.Invoke();
        }

        /// <summary>Begin swinging the gate open on its left edge (local -X) — the pivot side never
        /// changes, only <see cref="SwingSign"/> (which room the free edge sweeps toward) does. Reads
        /// the CURRENT transform as "closed" rather than caching it in Awake — Update re-derives the
        /// swing from this baseline every frame (never cumulative), so there is no drift to accumulate
        /// no matter how long the gate sits open.</summary>
        private void StartHingeSwing()
        {
            _closedPosition = transform.position;
            _closedRotation = transform.rotation;

            float halfWidth = transform.localScale.x * 0.5f;
            _hingePivot = _closedPosition - transform.right * halfWidth;

            _hingeSign = SwingSign(AwayFromPlayerDirection, transform.forward);

            _hingeT = 0f;
            _hinging = true;
        }

        /// <summary>+1 or -1 for the hinge angle in <see cref="Update"/>: a positive-angle
        /// <c>RotateAround(pivot, Vector3.up, angle)</c> always sweeps the free edge toward
        /// <c>-forward</c> (Unity's Y-rotation turns +right into -forward), so whenever the room beyond
        /// the gate sits on the <c>+forward</c> side the sign must flip to -1, or the door swings back
        /// into the room the player is standing in instead of the one ahead (MV-320). <c>awayFromPlayer
        /// == Vector3.zero</c> means no map context was wired in, so this keeps the untouched +1 every
        /// gate used before this ticket.</summary>
        public static float SwingSign(Vector3 awayFromPlayer, Vector3 forward)
        {
            if (awayFromPlayer == Vector3.zero) return 1f;
            return Vector3.Dot(awayFromPlayer, forward) > 0f ? -1f : 1f;
        }

        /// <summary>The leaf's own footprint along the doorway's hole axis once its swing settles at
        /// its fixed <see cref="HingeSwingDegrees"/> target (MV-448) — an open gate's leaf is
        /// deliberately never made passable (MV-386), so a routed waypoint has to aim at whatever part
        /// of the doorway the leaf does NOT cover, and this is what tells it where that is.
        ///
        /// Computed from the swing's own known geometry (<see cref="_closedRotation"/>,
        /// <see cref="_hingePivot"/>, <see cref="_hingeSign"/> — all set by <see cref="StartHingeSwing"/>
        /// before <see cref="Open"/> ever fires <see cref="Opened"/>), not read off the live, possibly
        /// still-animating collider — a caller asking the instant the gate opens must get the same
        /// answer a caller asking half a second later would, since the router re-solves once per open
        /// and cannot afford to cache a mid-swing snapshot.
        ///
        /// World X if <paramref name="alongX"/> (the doorway's hole is a span of X), world Z otherwise
        /// — the same convention <see cref="MapGeometry.Doorway"/> and <see cref="MapRoutes"/> use
        /// throughout. Meaningless before the gate has ever opened; callers only ask once
        /// <see cref="IsOpen"/> is true.</summary>
        public Span OpenLeafSpan(bool alongX)
        {
            float width = transform.localScale.x;
            float halfDepth = transform.localScale.z * 0.5f;

            Vector3 right = _closedRotation * Vector3.right;
            Vector3 forwardAxis = _closedRotation * Vector3.forward;
            Quaternion swing = Quaternion.AngleAxis(_hingeSign * HingeSwingDegrees, Vector3.up);

            float min = float.MaxValue, max = float.MinValue;
            for (int w = 0; w <= 1; w++)
            {
                for (int d = -1; d <= 1; d += 2)
                {
                    Vector3 corner = _hingePivot + swing * (right * (w * width) + forwardAxis * (d * halfDepth));
                    float coord = alongX ? corner.x : corner.z;
                    if (coord < min) min = coord;
                    if (coord > max) max = coord;
                }
            }

            return new Span(min, max);
        }

        private void Update()
        {
            AdvanceStormdrainSlide(Time.deltaTime);

            if (!_hinging) return;

            _hingeT += Time.deltaTime;
            float k = HingeDuration > 0f ? Mathf.Clamp01(_hingeT / HingeDuration) : 1f;
            // Ease out — fast off the latch, settling into the open position, so the swing reads as
            // a snap rather than a slow architectural drift.
            float eased = 1f - (1f - k) * (1f - k);
            float angle = _hingeSign * eased * HingeSwingDegrees;

            transform.position = _closedPosition;
            transform.rotation = _closedRotation;
            transform.RotateAround(_hingePivot, Vector3.up, angle);

            if (k >= 1f) _hinging = false;
        }
    }
}
