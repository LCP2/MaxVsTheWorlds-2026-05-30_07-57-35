using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The factory's loading door and ramp (YT-108).
    ///
    /// The Hutch already ran (YT-78) and already emitted robots from its wall rather than from a ring
    /// around it (YT-70, YT-100) — but there was still nothing on the building to come out OF. A robot
    /// appeared against a blank orange wall and walked away from it. Every part of "this machine is
    /// producing them" was carried by inference.
    ///
    /// So the Hutch gets a real door: a shutter on one wall that hauls itself up when a robot is due,
    /// a ramp down from the sill to the lawn, and a robot walking down it. The cadence stops being
    /// something you work out from the rate robots appear and becomes something you watch.
    ///
    /// One per factory, and independent (YT-92): a yard with two Hutches has two doors, on whichever
    /// wall each one has room to open onto, opening on their own schedules.
    ///
    /// Art owns the door. Gameplay is touched at exactly one seam — <see cref="EnemySpawner"/> asks an
    /// <see cref="IFactoryDoor"/> whether it may emit, and a factory without one behaves as it always
    /// did. Everything else here is read-only.
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("factories")]
    public sealed class FactoryDoorway : MonoBehaviour, IFactoryDoor
    {
        // --- Shape. Metres, and chosen to be read from 30 m up at ~72 deg (the only angle we have). ---
        private const float DoorWidth = 1.7f;
        private const float DoorHeight = 1.5f;
        private const float SillHeight = 0.45f;   // how high the doorway floor sits above the lawn
        private const float RampRun = 2.8f;       // ~9 deg — walkable, and long enough to see a robot on
        private const float RampHalfWidth = 1.05f;
        private const float RampThickness = 0.12f;
        private const float FrameThickness = 0.16f;
        private const float Proud = 0.09f;        // how far the frame stands off the wall

        // --- Cadence. The door is shut by default and only moves when the factory has something to
        //     put through it, so an idle factory is not a shutter flapping at nothing. ---
        private const float TravelSeconds = 0.38f;
        private const float HoldSeconds = 0.85f;  // stays up this long after the last robot
        private const float OpenEnough = 0.75f;   // openness at which a robot fits through

        /// <summary>Every doorway in the level, so a robot can ask which ramp it is standing on
        /// without a scene search per frame. Registered on build, removed on teardown.</summary>
        private static readonly List<FactoryDoorway> All = new List<FactoryDoorway>(4);

        /// <summary>
        /// One per factory, built the same way <see cref="FactoryLife"/> is: inactive, bound, then
        /// switched on — Awake builds the door around its hutch, so a door built before it was told
        /// which factory it belonged to would build itself around whichever one it found first.
        ///
        /// Public so an EditMode test can install doors over a map it only just built by hand
        /// (<c>MapRuntime.Build</c> never fires <c>AfterSceneLoad</c>) — same reasoning as every other
        /// post-build entry point in this codebase.
        ///
        /// MV-1107: <see cref="FindObjectsSortMode.InstanceID"/>, not <c>.None</c> — <see cref="SuppressConflictingDressing"/>
        /// resolves a ramp-vs-ramp conflict between two adjacent factories (a packed row of Replicators
        /// all facing the same way, closer together than one ramp is wide) by letting whichever ramp
        /// builds LATER win, so which of the two actually keeps its door depends on build order. Instance
        /// IDs are assigned in creation order within a session, so this is the same order
        /// <see cref="MapRuntime.Build"/> itself authored these in — deterministic and reproducible,
        /// rather than <c>.None</c>'s explicitly-unspecified order silently flipping which door survives
        /// from one boot to the next.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void Install()
        {
            InstallFor(FindObjectsByType<MowerHutch>(FindObjectsSortMode.InstanceID));
            InstallFor(FindObjectsByType<Replicator>(FindObjectsSortMode.InstanceID));
        }

        /// <summary>MV-756: one door per factory BODY, whichever concrete type it is.</summary>
        private static void InstallFor<T>(T[] bodies) where T : Component, IFactoryBody
        {
            foreach (var body in bodies)
            {
                if (body == null || HasDoor(body)) continue;

                var go = new GameObject($"FactoryDoorway ({body.name})");
                go.SetActive(false);
                var door = go.AddComponent<FactoryDoorway>();
                door.Bind(body);
                go.SetActive(true);

                // MV-1107: SetActive(true) only reliably fires Awake synchronously in an interactive
                // Editor or Play session. Unity's batchmode EditMode test runner never pumps the frame
                // that normally follows, so a door built only through Awake here never actually builds
                // when an EditMode test calls this method directly — EnsureBuilt is idempotent (it
                // no-ops once _built is true), so this costs nothing on the path where Awake already ran.
                door.EnsureBuilt();
            }
        }

        private static bool HasDoor(Component body) => DoorFor(body) != null;

        /// <summary>
        /// How high the ramp is under <paramref name="worldPos"/>, across every factory in the level.
        ///
        /// This is what lets a robot walk DOWN the ramp rather than through it. The robot's controller
        /// stays on the flat plane the whole level navigates on — lifting that would mean teaching
        /// every chase, cover and pathing rule about slopes for the sake of one 45 cm wedge — so it is
        /// the visible body that rides the ramp, and only while it is emerging.
        /// </summary>
        public static float RampLiftAt(Vector3 worldPos)
        {
            float lift = 0f;
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d == null || !d._built) continue;
                lift = Mathf.Max(lift, FactoryDoorGeometry.RampLiftAt(
                    worldPos, d._doorway, d._outward, d._across, SillHeight, RampRun, RampHalfWidth));
            }
            return lift;
        }

        public void Bind(Component body)
        {
            _body = body;
            _alive = body as IFactoryBody;
        }

        private Component _body;       // MowerHutch or Replicator (MV-756)
        private IFactoryBody _alive;
        private EnemySpawner _spawner;

        private Transform _shutter;
        private Vector3 _shutterClosedScale;
        private Vector3 _shutterClosedCentre;

        private Vector3 _doorway;    // world point at the middle of the sill, in the plane of the wall
        private Vector3 _outward;    // unit, axis-aligned, flat
        private Vector3 _across;     // unit, flat, perpendicular to _outward
        private bool _built;

        private Transform _ramp;    // MV-1107: the ramp alone (not the kerbs/frame/shutter around it)

        /// <summary>MV-1107: the ramp's own resolved world bounds — what the fix-up pass and an
        /// EditMode test both measure against, rather than a hand-recomputed approximation.</summary>
        public Bounds RampBounds => _ramp != null ? _ramp.GetComponent<Renderer>().bounds : default;

        /// <summary>MV-1107: false once <see cref="SuppressConflictingDressing"/> has switched this
        /// ramp's own renderer off — a later-built neighbour's ramp landed in the same footprint and
        /// won the argument (see that method's own doc). A caller asking whether THIS door's ramp
        /// clears the level should ask this first: a disabled ramp draws nothing, so its stale bounds
        /// are no longer a visible thing for anything else to clear.</summary>
        public bool RampVisible => _ramp != null && _ramp.GetComponent<Renderer>().enabled;

        /// <summary>MV-1107: ground level at the bottom of the ramp run — where a robot (or a test)
        /// expects to find walkable floor once the ramp finishes descending.</summary>
        public Vector3 RampFootWorld => _doorway + _outward * RampRun;

        /// <summary>MV-1107: this door's own ground reference (the building's base, <c>_doorway.y</c>)
        /// — what <see cref="IsGroundPlane"/> measures a candidate floor against. NOT the ramp's own
        /// resolved min Y: the ramp slab's thickness is centred on its sloped top surface, so its lowest
        /// point dips slightly below true ground, and a floor sitting exactly at true ground would then
        /// wrongly read as rising above it.</summary>
        public float GroundY => _doorway.y;

        /// <summary>MV-1107: every door built so far, read-only — an EditMode test tears every one of
        /// these down itself (they are built as independent scene roots, not parented under the body
        /// they belong to) the same way it tears down everything else it spawned.</summary>
        public static System.Collections.Generic.IReadOnlyList<FactoryDoorway> AllDoors => All;

        /// <summary>MV-1107: the door already built for <paramref name="body"/>, or null — an EditMode
        /// test driving a real map build looks its door up this way rather than re-deriving which
        /// GameObject <see cref="InstallFor{T}"/> made for it.</summary>
        public static FactoryDoorway DoorFor(Component body)
        {
            foreach (var d in All) if (d != null && d._body == body) return d;
            return null;
        }

        private float _openness;
        private bool _opening;
        private float _travelTimer;
        private float _holdTimer;
        private int _lastEmitted;
        private bool _running = true;

        // --- IFactoryDoor. The spawner reads these and nothing else. ---
        public bool CanEmit => _running && _openness >= OpenEnough;
        public Vector3 OutwardDirection => _outward;

        /// <summary>Tight, because the opening is: 1.7 m of doorway cannot pour robots across the 110°
        /// arc the notional mouth used without walking them through their own wall.</summary>
        public float FanHalfAngleDeg => 22f;

        public float Openness => _openness;

        private void Awake()
        {
            if (_body == null)
            {
                var hutch = FindFirstObjectByType<MowerHutch>();
                if (hutch != null) Bind(hutch);
                else
                {
                    var replicator = FindFirstObjectByType<Replicator>();
                    if (replicator != null) Bind(replicator);
                }
            }
            EnsureBuilt();
        }

        /// <summary>
        /// Builds the door around <see cref="_body"/>. Idempotent (a no-op once already built), and
        /// public for exactly that reason: <see cref="InstallFor{T}"/> calls this directly right after
        /// <see cref="Bind"/> rather than trusting <c>SetActive(true)</c> alone to fire <see cref="Awake"/>
        /// — true in an interactive Editor or Play session, but MV-1107 found Unity's batchmode EditMode
        /// test runner never pumps the frame that normally follows, so a door built only through Awake
        /// never actually built there at all. Whichever of the two runs first wins; the other costs one
        /// early-return.
        /// </summary>
        public void EnsureBuilt()
        {
            if (_built || _body == null) return;

            _spawner = _body.GetComponent<EnemySpawner>();

            // Same trap FactoryLife documents: two scene-wide sweeps re-material anything they
            // classify by shape, and a ramp is exactly the flat slab they read as a stone floor.
            gameObject.AddComponent<KeepsOwnMaterial>();

            // Measured while the body is still visible — the factory switches its renderer off the
            // moment it dies, and bounds read after that are a zero-sized box at the origin.
            var rend = _body.GetComponent<Renderer>();
            Bounds b = rend != null
                ? rend.bounds
                : new Bounds(_body.transform.position + Vector3.up, new Vector3(3f, 2f, 3f));

            // MV-1107: one snapshot of every renderer already in the scene, taken before this door
            // builds anything of its own — used both to pick a face (sheds, below) and to find out
            // afterward whether dressing still sits where the ramp ended up (see
            // SuppressConflictingDressing). A single FindObjectsByType covers both; this door's own
            // new geometry is excluded by hierarchy, not by timing, so one snapshot is enough.
            Renderer[] sceneRenderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            ChooseFace(b, sceneRenderers);
            Build(b);
            SuppressConflictingDressing(sceneRenderers);
            CheckRampFootIsWalkable();
            _built = true;
            All.Add(this);

            if (_spawner != null) _spawner.UseDoor(this);
            _lastEmitted = _spawner != null ? _spawner.Emitted : 0;
        }

        private void OnDestroy() => All.Remove(this);

        /// <summary>
        /// Put the door on the wall with somewhere to walk out of.
        ///
        /// MV-1107: a Replicator already carries an authored facing (Lee's workbook names its IN
        /// side) — when the body exposes one, the door goes straight there, never probed for. Only a
        /// body with none (a shed) still probes: which way IT faces is a property of the map, not
        /// authored data, and the map is data that changes.
        /// </summary>
        private void ChooseFace(Bounds b, Renderer[] sceneRenderers)
        {
            if (_body is IFactoryDoorFacing authoredFacing && authoredFacing.AuthoredDoorOutward.HasValue)
            {
                _outward = authoredFacing.AuthoredDoorOutward.Value;
            }
            else
            {
                // The level's walls were built this same frame (MapRuntime runs in BackyardPath.Awake).
                // Physics only sees a collider once transforms are synced, and an unsynced probe reports
                // clear ground through every wall in the yard — which would put the door wherever the
                // fallback felt like.
                Physics.SyncTransforms();

                Vector3 centre = new Vector3(b.center.x, b.min.y + 0.6f, b.center.z);
                var clearances = new float[FactoryDoorGeometry.Faces.Length];
                var blocked = new bool[FactoryDoorGeometry.Faces.Length];

                for (int i = 0; i < FactoryDoorGeometry.Faces.Length; i++)
                {
                    Vector3 dir = FactoryDoorGeometry.Faces[i];
                    float fromCentre = Mathf.Abs(Vector3.Dot(b.extents, dir)) + 0.05f;
                    Vector3 origin = centre + dir * fromCentre;

                    // The factory's own collider is behind us; anything we hit is the world.
                    clearances[i] = Physics.Raycast(origin, dir, out RaycastHit hit, MaxProbe)
                        ? hit.distance
                        : MaxProbe;

                    // MV-1107: a raycast only sees a collider — decorative dressing (a pipe run, a
                    // kerb) never carries one, so a face with one sitting in the ramp's own footprint
                    // used to read as clear. Resolved renderer bounds see it regardless.
                    Vector3 doorway = b.center + dir * Mathf.Abs(Vector3.Dot(b.extents, dir));
                    doorway.y = b.min.y;
                    Bounds footprint = FactoryDoorGeometry.RampFootprint(doorway, dir, SillHeight, RampRun, RampHalfWidth);
                    blocked[i] = FootprintIsOccupied(footprint, sceneRenderers, OccupancyEpsilon, b.min.y);
                }

                int face = FactoryDoorGeometry.ChooseFace(clearances, blocked);
                _outward = FactoryDoorGeometry.Faces[face];
            }

            _across = Vector3.Cross(Vector3.up, _outward);

            float half = Mathf.Abs(Vector3.Dot(b.extents, _outward));
            _doorway = b.center + _outward * half;
            _doorway.y = b.min.y;
        }

        private const float MaxProbe = 14f;

        private void Build(Bounds b)
        {
            Quaternion facing = Quaternion.LookRotation(_outward, Vector3.up);

            // The ramp: a wedge from the sill down to the lawn. Built as a slab pitched about its
            // across-axis, so its top surface is the line RampHeightAt describes.
            float slope = Mathf.Atan2(SillHeight, RampRun) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(RampRun * RampRun + SillHeight * SillHeight);

            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "Ramp";
            Strip(ramp);
            ramp.transform.SetParent(transform, worldPositionStays: false);
            ramp.transform.rotation = facing * Quaternion.Euler(slope, 0f, 0f);
            ramp.transform.position =
                _doorway + _outward * (RampRun * 0.5f) + Vector3.up * (SillHeight * 0.5f)
                - ramp.transform.up * (RampThickness * 0.5f);
            // MV-779: a Bevelled box, not the flat primitive cube — it is walked on, so its shape
            // (a plain slab) is unchanged, only the edge is chamfered like every other kit box.
            Vector3 rampSize = new Vector3(RampHalfWidth * 2f, RampThickness, length);
            ramp.transform.localScale = Vector3.one;
            ramp.GetComponent<MeshFilter>().sharedMesh =
                CharacterMeshes.Bevelled(rampSize, CharacterMeshes.DefaultBevel(rampSize));
            Paint(ramp, SurfaceKind.Metal);
            _ramp = ramp.transform; // MV-1107: read back by RampBounds/RampFootWorld and the fix-up pass

            // Kerbs down each side of the ramp — they catch the light and make the slope read as a
            // slope from above, which a bare plate at 9 deg does not. MV-779: a 4-sided Prism, tapered
            // toward the room, rather than a flat-sided cube — the taper reads as cast concrete.
            const float diag = 1.41421356f;   // 1/cos(45deg): Prism's 4-sided case, see CharacterMeshes
            const float kerbWidth = 0.12f;
            for (int s = -1; s <= 1; s += 2)
            {
                var kerb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                kerb.name = s < 0 ? "KerbL" : "KerbR";
                Strip(kerb);
                kerb.transform.SetParent(transform, worldPositionStays: false);
                // The extra 90deg about X turns the Prism's own tapering axis (its local Y) onto the
                // ramp's length axis (the box convention's local Z, carried by ramp.transform.rotation)
                // — the Prism's TOP (the narrower end) lands on ramp local +Z, which is _outward: the
                // room the ramp leads into.
                kerb.transform.rotation = ramp.transform.rotation * Quaternion.Euler(90f, 0f, 0f);
                kerb.transform.position = ramp.transform.position
                    + _across * (s * (RampHalfWidth - 0.06f)) + ramp.transform.up * 0.06f;
                kerb.transform.localScale = Vector3.one;
                kerb.GetComponent<MeshFilter>().sharedMesh =
                    CharacterMeshes.Prism(4, diag * kerbWidth, diag * kerbWidth * 0.74f, length, 0.12f);
                Paint(kerb, SurfaceKind.Metal);
            }

            // The frame: two jambs and a lintel, standing proud of the wall so the opening reads as a
            // hole in the building rather than a decal on it. MV-779: the jambs are a tapered 6-sided
            // Prism instead of a cube, so the opening reads as framed rather than as a hole cut in a slab.
            float sillTop = _doorway.y + SillHeight;
            float jambHeight = DoorHeight + FrameThickness;
            for (int s = -1; s <= 1; s += 2)
            {
                var jamb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                jamb.name = s < 0 ? "JambL" : "JambR";
                Strip(jamb);
                jamb.transform.SetParent(transform, worldPositionStays: false);
                jamb.transform.rotation = facing;
                jamb.transform.position = _doorway
                    + _across * (s * (DoorWidth * 0.5f + FrameThickness * 0.5f))
                    + Vector3.up * (SillHeight + DoorHeight * 0.5f)
                    + _outward * Proud;
                jamb.transform.localScale = Vector3.one;
                jamb.GetComponent<MeshFilter>().sharedMesh = CharacterMeshes.Prism(
                    6, FrameThickness * 0.62f, FrameThickness * 0.48f, jambHeight, 0.08f);
                Paint(jamb, SurfaceKind.Metal);
            }

            // MV-779: the lintel is a Bevelled box (same shape as before, chamfered) with five bolts
            // along its front (outward-facing) face — the fabrication detail that reads as "built".
            var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lintel.name = "Lintel";
            Strip(lintel);
            lintel.transform.SetParent(transform, worldPositionStays: false);
            lintel.transform.rotation = facing;
            lintel.transform.position = _doorway
                + Vector3.up * (sillTop + DoorHeight + FrameThickness * 0.5f) + _outward * Proud;
            Vector3 lintelSize = new Vector3(DoorWidth + FrameThickness * 2f, FrameThickness, FrameThickness);
            lintel.transform.localScale = Vector3.one;
            lintel.GetComponent<MeshFilter>().sharedMesh =
                CharacterMeshes.Bevelled(lintelSize, CharacterMeshes.DefaultBevel(lintelSize));
            Paint(lintel, SurfaceKind.Metal);

            float lintelBoltScale = FrameThickness * 0.30f;
            for (int i = 0; i < 5; i++)
            {
                float t = (i + 0.5f) / 5f - 0.5f;
                var bolt = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bolt.name = $"LintelBolt{i}";
                Strip(bolt);
                bolt.transform.SetParent(lintel.transform, false);
                bolt.transform.localPosition = new Vector3(t * lintelSize.x, 0f, lintelSize.z * 0.5f);
                bolt.transform.localRotation = Quaternion.identity;
                bolt.transform.localScale = Vector3.one * lintelBoltScale;
                bolt.GetComponent<MeshFilter>().sharedMesh = CharacterMeshes.Sphere(8);
                Paint(bolt, SurfaceKind.Metal);
            }

            // The shutter. It ROLLS UP: the top edge stays pinned under the lintel and the panel
            // shortens, rather than a slab sliding up through the roof. MV-779: seven horizontal slats
            // under one empty "Shutter" parent, instead of one slab — ApplyOpenness still scales this
            // same parent on Y exactly as before, so the slats compress with it for free; the slats'
            // own sizes are fractions of the parent's [-0.5, 0.5] unit space, not metres, for exactly
            // that reason.
            var shutterParent = new GameObject("Shutter");
            shutterParent.transform.SetParent(transform, worldPositionStays: false);
            shutterParent.transform.rotation = facing;
            _shutterClosedCentre = _doorway
                + Vector3.up * (sillTop + DoorHeight * 0.5f) + _outward * (Proud * 0.5f);
            _shutterClosedScale = new Vector3(DoorWidth, DoorHeight, 0.08f);
            shutterParent.transform.position = _shutterClosedCentre;
            shutterParent.transform.localScale = _shutterClosedScale;
            _shutter = shutterParent.transform;

            const int slatCount = 7;
            const float slatGapFraction = 0.12f;
            float slotFrac = 1f / slatCount;
            float slatHeightFrac = slotFrac * (1f - slatGapFraction);
            Vector3 slatSize = new Vector3(1f, slatHeightFrac, 1f);
            Mesh slatMesh = CharacterMeshes.Bevelled(slatSize, CharacterMeshes.DefaultBevel(slatSize));
            for (int i = 0; i < slatCount; i++)
            {
                float centreY = -0.5f + slotFrac * (i + 0.5f);
                var slat = new GameObject($"Slat{i}");
                slat.transform.SetParent(shutterParent.transform, false);
                slat.transform.localPosition = new Vector3(0f, centreY, 0f);
                slat.transform.localScale = Vector3.one;
                slat.AddComponent<MeshFilter>().sharedMesh = slatMesh;
                slat.AddComponent<MeshRenderer>();
                Paint(slat, SurfaceKind.Metal);
            }

            ApplyOpenness();
        }

        private void Update()
        {
            if (_body == null) return;

            // The source is gone: the door drops and stays down. A shutter still cycling on a dead
            // factory would advertise production that has stopped — the exact opposite of the read
            // this whole ticket exists to create.
            if (_running && !_alive.IsAlive) Die();
            if (!_running) return;

            float dt = Time.deltaTime;

            // A robot came out — hold the door up a beat so it is not shutting on the robot's heels.
            if (_spawner != null && _spawner.Emitted > _lastEmitted)
            {
                _lastEmitted = _spawner.Emitted;
                _holdTimer = HoldSeconds;
            }

            bool wants = _spawner != null && _spawner.WantsToEmit;
            _holdTimer = Mathf.Max(0f, _holdTimer - dt);

            bool shouldBeOpen = wants || _holdTimer > 0f;
            if (shouldBeOpen != _opening)
            {
                _opening = shouldBeOpen;
                // Carry the current position into the new direction, so a door interrupted halfway
                // reverses from where it is instead of snapping to an end and starting over. The
                // inverse is taken as linear rather than un-easing the SmoothStep — the error is a
                // few hundredths of a second on a 0.38 s travel, and nobody can see it.
                float progress = _opening ? _openness : 1f - _openness;
                _travelTimer = Mathf.Clamp01(progress) * TravelSeconds;
            }

            _travelTimer = Mathf.Clamp(_travelTimer + dt, 0f, TravelSeconds);
            _openness = FactoryDoorGeometry.Openness(_travelTimer, TravelSeconds, _opening);

            ApplyOpenness();
        }

        /// <summary>Rolls the shutter to the current openness, top edge pinned.</summary>
        private void ApplyOpenness()
        {
            if (_shutter == null) return;

            float remaining = Mathf.Max(1f - _openness, 0.02f);   // never fully degenerate
            float height = _shutterClosedScale.y * remaining;

            _shutter.localScale =
                new Vector3(_shutterClosedScale.x, height, _shutterClosedScale.z);
            // Top edge stays where the closed panel's top edge was.
            float topY = _shutterClosedCentre.y + _shutterClosedScale.y * 0.5f;
            Vector3 p = _shutterClosedCentre;
            p.y = topY - height * 0.5f;
            _shutter.position = p;
        }

        private void Die()
        {
            _running = false;
            _opening = false;
            _openness = 0f;
            ApplyOpenness();

            // The Hutch keeps its GameObject alive so the robots it already made keep fighting, so
            // nothing here is destroyed with it — the door has to take itself away, exactly as
            // FactoryLife's impeller does.
            foreach (var r in GetComponentsInChildren<MeshRenderer>(includeInactive: true))
                r.enabled = false;
        }

        private static void Paint(GameObject go, SurfaceKind kind)
        {
            var r = go.GetComponent<MeshRenderer>();
            var mat = MaterialLibrary.Surface(kind);
            if (mat != null) r.sharedMaterial = mat;
        }

        /// <summary>Scenery. The ramp is something to look at, not something to collide with — the
        /// robots walk a flat plane and an extra collider here would only trip them at the doorway.
        /// MV-779: <c>Destroy</c> only in Play mode, else <c>DestroyImmediate</c> — same idiom
        /// <see cref="MaxWorlds.Rendering.StormdrainKit.Strip"/> already uses, needed the moment an
        /// EditMode test builds a door synchronously rather than only ever through a running frame.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        /// <summary>MV-1107, change 4: the ramp foot must land on walkable floor on the building's own
        /// level — logged, never corrected, since the ramp's own position is entirely derived from
        /// level-authored data (the body's own position plus the chosen/authored face) and this class
        /// never moves a factory to make its own door fit. A no-op with no map loaded (every EditMode
        /// test that builds a door directly, without a <see cref="MaxWorlds.Arena.BackyardPath"/> in the
        /// scene) — it is a diagnostic for the shipped game, not a behaviour this class enforces.</summary>
        private void CheckRampFootIsWalkable()
        {
            MapData map = EnemyNavigation.Map;
            if (map == null || _body == null) return;

            if (!map.IsWalkable(_body.transform.position, RampFootWorld))
            {
                Debug.LogError($"[FactoryDoorway] '{_body.name}' ramp foot at {RampFootWorld} is not over " +
                    "walkable floor on its own level — config error.");
            }
        }

        // --- MV-1107: ramp vs. dressing --------------------------------------------------------

        /// <summary>How far two boxes must actually interpenetrate, on every axis, to count as a real
        /// overlap rather than two faces resting flush against each other — the ramp's own sill sits
        /// exactly at floor height, so a bare <see cref="Bounds.Intersects"/> would read every ordinary
        /// floor as "blocking" purely from touching it at that one shared plane.</summary>
        private const float OccupancyEpsilon = 0.02f;

        /// <summary>MV-1107: how far past the ramp's own resolved bounds dressing still counts as "in
        /// the way" — the ticket's own number. Loose enough that a prop resting right at the ramp's
        /// edge, not actually crossing it, is left alone.</summary>
        private const float RampGrowMetres = 0.2f;

        private static bool FootprintIsOccupied(Bounds footprint, Renderer[] sceneRenderers, float epsilon, float groundY)
        {
            foreach (Renderer r in sceneRenderers)
            {
                if (r == null || !r.enabled) continue;
                if (IsGroundPlane(r, groundY)) continue;
                if (ReallyOverlaps(footprint, r.bounds, epsilon)) return true;
            }
            return false;
        }

        /// <summary>MV-1107: true for a renderer that never rises above <paramref name="groundY"/> (plus
        /// a hair of slack) — the floor itself, never an obstacle a door's own ramp needs to avoid or
        /// yield to. Geometric, not name-matched: MapRuntime's own single "Map Floor" slab qualifies, but
        /// so does any other flat ground plane under a factory — a bare <c>Physics.Raycast</c> origin
        /// 0.6 m up (see <see cref="ChooseFace"/>) never even reaches it, but a resolved-bounds scan
        /// would, and every floor in the game touches a ramp's own sill at exactly this one shared
        /// plane.</summary>
        private static bool IsGroundPlane(Renderer r, float groundY) => r.bounds.max.y <= groundY + 0.05f;

        /// <summary>True only once two boxes genuinely interpenetrate by more than <paramref name="epsilon"/>
        /// on every axis — a plain <see cref="Bounds.Intersects"/> also fires on a zero-depth touch
        /// (two slabs resting flush, like a ramp's sill meeting ordinary floor), which would make every
        /// face read as blocked.</summary>
        private static bool ReallyOverlaps(Bounds a, Bounds b, float epsilon)
        {
            return a.min.x < b.max.x - epsilon && a.max.x > b.min.x + epsilon
                && a.min.y < b.max.y - epsilon && a.max.y > b.min.y + epsilon
                && a.min.z < b.max.z - epsilon && a.max.z > b.min.z + epsilon;
        }

        /// <summary>
        /// MV-1107, change 3: once the ramp is built, anything still sitting in its (slightly grown)
        /// footprint is either structural/authored — a wall, an authored cover piece, another door's own
        /// ramp — or purely decorative dressing with no collider on it at all (a pipe run, a kerb, a
        /// lamp bracket; see <see cref="FactoryDoorGeometry"/>'s own doc on why a raycast never saw
        /// these). The second kind is switched off — the ramp was always going to win that argument,
        /// and the alternative is the exact defect this ticket exists to close (a ramp visibly cutting
        /// through a pipe nobody can move). The first kind is never removed, because that would be
        /// removing part of the level Lee drew — a config error is logged naming the body and what it
        /// hit, AND this ramp itself yields instead (see <see cref="RampVisible"/>): an authored facing
        /// that was never checked against the level's own structural geometry (MV-1148 is the follow-up
        /// tracking which replicator needs a different one) must not draw a door through a wall or a
        /// deck ramp either, and there is nothing on the other side of that conflict this class is
        /// allowed to move.
        /// </summary>
        private void SuppressConflictingDressing(Renderer[] sceneRenderers)
        {
            if (_ramp == null) return;
            Renderer rampRenderer = _ramp.GetComponent<Renderer>();
            if (rampRenderer == null) return;

            Bounds grown = rampRenderer.bounds;
            grown.Expand(RampGrowMetres * 2f); // Expand grows the TOTAL size, so this is 0.2 m per side.

            foreach (Renderer r in sceneRenderers)
            {
                if (r == null || !r.enabled) continue;
                if (IsGroundPlane(r, _doorway.y)) continue;
                if (_body != null && (r.transform == _body.transform || r.transform.IsChildOf(_body.transform))) continue;

                if (!ReallyOverlaps(grown, r.bounds, 0.05f)) continue;

                // MV-1107: a collider alone isn't "structural or authored" — MV-1087's channel kerbs
                // and crossing decks are decorative dressing that ALSO carry one, purely so something
                // can walk on top of them (StormdrainKit.MakeWalkable's own StructuralFloor marker).
                // A wall or an authored cover piece never carries that marker, so it is what tells the
                // two apart.
                bool isProtected = r.GetComponent<Collider>() != null && r.GetComponent<StructuralFloor>() == null;

                if (isProtected)
                {
                    // Structural or authored — a wall, an authored cover piece (including an authored
                    // "pipe barrier"), a deck-access ramp. Left standing; this is a level-data conflict,
                    // not a dressing one, and it is not this door's place to resolve it. This door's OWN
                    // ramp yields instead (rampRenderer.enabled below) — a decorative door that cuts
                    // through real structural geometry is exactly the defect this ticket closes, whether
                    // the thing on the other side of it is a pipe or a wall.
                    rampRenderer.enabled = false;
                    Debug.LogError($"[FactoryDoorway] '{_body.name}' ramp overlaps '{r.name}', which " +
                        "carries a collider (a wall or authored cover) — config error, not auto-resolved.");
                }
                else
                {
                    // No collider, or a walkable-but-decorative piece (a kerb, a crossing deck) —
                    // either way, built without knowing this ramp would ever land here. It yields; the
                    // ramp does not move.
                    r.enabled = false;
                }
            }
        }
    }
}
