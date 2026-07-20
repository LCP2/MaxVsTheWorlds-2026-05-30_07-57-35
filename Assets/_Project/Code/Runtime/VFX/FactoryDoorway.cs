using System.Collections.Generic;
using UnityEngine;
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
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            foreach (var hutch in FindObjectsByType<MowerHutch>(FindObjectsSortMode.None))
            {
                if (hutch == null || HasDoor(hutch)) continue;

                var go = new GameObject($"FactoryDoorway ({hutch.name})");
                go.SetActive(false);
                go.AddComponent<FactoryDoorway>().Bind(hutch);
                go.SetActive(true);
            }
        }

        private static bool HasDoor(MowerHutch hutch)
        {
            foreach (var d in All) if (d != null && d._hutch == hutch) return true;
            return false;
        }

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

        public void Bind(MowerHutch hutch) => _hutch = hutch;

        private MowerHutch _hutch;
        private EnemySpawner _spawner;

        private Transform _shutter;
        private Vector3 _shutterClosedScale;
        private Vector3 _shutterClosedCentre;

        private Vector3 _doorway;    // world point at the middle of the sill, in the plane of the wall
        private Vector3 _outward;    // unit, axis-aligned, flat
        private Vector3 _across;     // unit, flat, perpendicular to _outward
        private bool _built;

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
            if (_hutch == null) _hutch = FindFirstObjectByType<MowerHutch>();
            if (_hutch == null) return;

            _spawner = _hutch.GetComponent<EnemySpawner>();

            // Same trap FactoryLife documents: two scene-wide sweeps re-material anything they
            // classify by shape, and a ramp is exactly the flat slab they read as a stone floor.
            gameObject.AddComponent<KeepsOwnMaterial>();

            // Measured while the body is still visible — MowerHutch switches its renderer off the
            // moment it dies, and bounds read after that are a zero-sized box at the origin.
            var body = _hutch.GetComponent<Renderer>();
            Bounds b = body != null
                ? body.bounds
                : new Bounds(_hutch.transform.position + Vector3.up, new Vector3(3f, 2f, 3f));

            ChooseFace(b);
            Build(b);
            _built = true;
            All.Add(this);

            if (_spawner != null) _spawner.UseDoor(this);
            _lastEmitted = _spawner != null ? _spawner.Emitted : 0;
        }

        private void OnDestroy() => All.Remove(this);

        /// <summary>
        /// Put the door on the wall with somewhere to walk out of. Probing rather than asserting:
        /// which way a given factory faces is a property of the map, and the map is data that changes.
        /// </summary>
        private void ChooseFace(Bounds b)
        {
            // The level's walls were built this same frame (MapRuntime runs in BackyardPath.Awake).
            // Physics only sees a collider once transforms are synced, and an unsynced probe reports
            // clear ground through every wall in the yard — which would put the door wherever the
            // tie-break felt like.
            Physics.SyncTransforms();

            Vector3 centre = new Vector3(b.center.x, b.min.y + 0.6f, b.center.z);
            var clearances = new float[FactoryDoorGeometry.Faces.Length];

            for (int i = 0; i < FactoryDoorGeometry.Faces.Length; i++)
            {
                Vector3 dir = FactoryDoorGeometry.Faces[i];
                float fromCentre = Mathf.Abs(Vector3.Dot(b.extents, dir)) + 0.05f;
                Vector3 origin = centre + dir * fromCentre;

                // The factory's own collider is behind us; anything we hit is the world.
                clearances[i] = Physics.Raycast(origin, dir, out RaycastHit hit, MaxProbe)
                    ? hit.distance
                    : MaxProbe;
            }

            var player = GameObject.FindGameObjectWithTag("Player");
            Vector3 toPlayer = player != null ? player.transform.position - b.center : Vector3.zero;

            int face = FactoryDoorGeometry.ChooseFace(clearances, toPlayer);
            _outward = FactoryDoorGeometry.Faces[face];
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
            ramp.transform.localScale = new Vector3(RampHalfWidth * 2f, RampThickness, length);
            Paint(ramp, SurfaceKind.Metal);

            // Kerbs down each side of the ramp — they catch the light and make the slope read as a
            // slope from above, which a bare plate at 9 deg does not.
            for (int s = -1; s <= 1; s += 2)
            {
                var kerb = GameObject.CreatePrimitive(PrimitiveType.Cube);
                kerb.name = s < 0 ? "KerbL" : "KerbR";
                Strip(kerb);
                kerb.transform.SetParent(transform, worldPositionStays: false);
                kerb.transform.rotation = ramp.transform.rotation;
                kerb.transform.position = ramp.transform.position
                    + _across * (s * (RampHalfWidth - 0.06f)) + ramp.transform.up * 0.06f;
                kerb.transform.localScale = new Vector3(0.12f, 0.16f, length);
                Paint(kerb, SurfaceKind.Metal);
            }

            // The frame: two jambs and a lintel, standing proud of the wall so the opening reads as a
            // hole in the building rather than a decal on it.
            float sillTop = _doorway.y + SillHeight;
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
                jamb.transform.localScale =
                    new Vector3(FrameThickness, DoorHeight + FrameThickness, FrameThickness);
                Paint(jamb, SurfaceKind.Metal);
            }

            var lintel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lintel.name = "Lintel";
            Strip(lintel);
            lintel.transform.SetParent(transform, worldPositionStays: false);
            lintel.transform.rotation = facing;
            lintel.transform.position = _doorway
                + Vector3.up * (sillTop + DoorHeight + FrameThickness * 0.5f) + _outward * Proud;
            lintel.transform.localScale =
                new Vector3(DoorWidth + FrameThickness * 2f, FrameThickness, FrameThickness);
            Paint(lintel, SurfaceKind.Metal);

            // The shutter. It ROLLS UP: the top edge stays pinned under the lintel and the panel
            // shortens, rather than a slab sliding up through the roof.
            var shutter = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shutter.name = "Shutter";
            Strip(shutter);
            shutter.transform.SetParent(transform, worldPositionStays: false);
            shutter.transform.rotation = facing;
            _shutterClosedCentre = _doorway
                + Vector3.up * (sillTop + DoorHeight * 0.5f) + _outward * (Proud * 0.5f);
            _shutterClosedScale = new Vector3(DoorWidth, DoorHeight, 0.08f);
            shutter.transform.position = _shutterClosedCentre;
            shutter.transform.localScale = _shutterClosedScale;
            Paint(shutter, SurfaceKind.Metal);
            _shutter = shutter.transform;

            ApplyOpenness();
        }

        private void Update()
        {
            if (_hutch == null) return;

            // The source is gone: the door drops and stays down. A shutter still cycling on a dead
            // factory would advertise production that has stopped — the exact opposite of the read
            // this whole ticket exists to create.
            if (_running && !_hutch.IsAlive) Die();
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
        /// robots walk a flat plane and an extra collider here would only trip them at the doorway.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
    }
}
