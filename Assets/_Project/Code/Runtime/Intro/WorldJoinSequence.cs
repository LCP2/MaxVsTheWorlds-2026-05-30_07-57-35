using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Intro
{
    /// <summary>
    /// MV-964: the one door-and-corridor sequence every world's finale exit uses, generalised off a
    /// <see cref="WorldTransitionEntry"/> instead of a hardcoded World 1 -> World 2 door/world-x table
    /// (MV-845/849's own hardcodes). Two independent installations of this same class run the two halves
    /// of one transition, in two different scene loads:
    ///
    ///  * <see cref="OpenExitDoor"/> — called by <see cref="MaxWorlds.VFX.WorldFinaleGate"/> the instant
    ///    the world's own last boss dies. MV-997: the door itself is real map geometry <c>MapRuntime</c>
    ///    already built closed at boot, so this just forces THAT gate open and builds the corridor
    ///    behind it immediately, but leaves Max in full control -- <see cref="Tick"/>'s own
    ///    <c>AwaitingCrossing</c> phase just watches for him to actually walk through, at which point
    ///    control is taken away for the scripted walk to the corridor's end and the fade that follows.
    ///  * <see cref="TryPlayArrival"/> — installed on the destination world's own boot when
    ///    <see cref="WorldTransitions.PendingArrivalFrom"/> is set. Builds the arrival shell outside the
    ///    destination stub's wall and walks Max in through it.
    ///
    /// <see cref="Initialize"/> is the third, test-facing entry point (MV-845's own idiom): builds the
    /// exit geometry and skips straight to the suspended walk, as if Max had already crossed -- a test
    /// drives it with <see cref="Tick"/> exactly as before this ticket, it just no longer has to walk him
    /// to the door first (that leg is ordinary, un-scripted gameplay now, not this class's job).
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("intro")]
    public sealed class WorldJoinSequence : MonoBehaviour
    {
        private const float FadeDuration = 0.4f;
        private const float HoldAtEndSeconds = 0.3f;
        private const float WalkTimeoutSeconds = 8f;
        private const float ArrivalEpsilon = 0.2f;
        private const float RotationSpeedDegPerSec = 720f;

        /// <summary>MV-849: the corridor's lighting/fog reach the destination world's look this fraction
        /// of the way along it — arriving a little early reads better than the blend still finishing
        /// right as the fade-to-black starts.</summary>
        private const float LightingBlendFraction = 0.73f;

        /// <summary>AC4 (MV-845): the scripted walk stops this far short of the corridor's own far end —
        /// the far end itself is the closed, re-skinned gate into the next world, never reached in this
        /// session.</summary>
        private const float WalkEndClearance = 3f;

        // ------------------------------------------------------------------ static installation

        private enum Mode { Exit, Arrival }
        private enum Phase { AwaitingCrossing, WalkToEnd, HoldAtEnd, FadeOut, FadeIn, ArrivalWalk, Done }

        /// <summary>MV-964: the world's finale door just opened -- cut the real gap, build the corridor
        /// behind it, and leave Max in full control until he actually walks through (see
        /// <see cref="TickAwaitingCrossing"/>). A no-op if the sequence is already running (defensive;
        /// <see cref="MaxWorlds.VFX.WorldFinaleGate.IsOpen"/> already guards against a second call) or if
        /// there is no live player to walk through it.</summary>
        public static void OpenExitDoor(WorldConfig fromCfg, MapData fromMap, WorldTransitionEntry entry, int fromWorldIndex,
            AreaGate exitGate = null)
        {
            if (FindFirstObjectByType<WorldJoinSequence>() != null) return;
            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return;

            var seq = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            seq._mode = Mode.Exit;
            seq.BuildExit(fromCfg, fromMap, entry, fromWorldIndex, player, exitGate);
            seq._phase = Phase.AwaitingCrossing;
        }

        /// <summary>The destination world's own boot: if a corridor walk is pending
        /// (<see cref="WorldTransitions.PendingArrivalFrom"/>), build the arrival shell outside its stub
        /// and walk Max in through it. Consumes the flag whether or not it actually starts a sequence, so
        /// a boot that can't resolve one (no player/map yet) never leaves it dangling for a later,
        /// unrelated boot to misinterpret.</summary>
        public static bool TryPlayArrival()
        {
            int? from = WorldTransitions.PendingArrivalFrom;
            if (from == null) return false;
            WorldTransitions.PendingArrivalFrom = null;

            WorldTransitionEntry entry = WorldTransitions.For(from.Value);
            if (entry == null) return false;

            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null || path.Cfg == null || path.Map == null) return false;
            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return false;
            if (FindFirstObjectByType<WorldJoinSequence>() != null) return false;

            var seq = new GameObject("WorldJoinSequence (Arrival)").AddComponent<WorldJoinSequence>();
            seq.InitializeArrival(path.Cfg, path.Map, entry, from.Value, player, null);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallArrival() => TryPlayArrival();

        // ------------------------------------------------------------------ state

        private Mode _mode;
        private Phase _phase;
        private float _phaseElapsed;

        private WorldTransitionEntry _entry;
        private int _fromWorldIndex;
        private Vector2 _doorMouth;
        private Wall _wall;

        private System.Action _onFinished;
        private PlayerController _player;
        private CharacterController _cc;
        private Transform _playerT;

        private bool _restored;
        private HudController _hud;
        private readonly List<RobotEnemy> _frozenRobots = new List<RobotEnemy>(16);

        private AreaGate _doorGate;
        private Transform _segmentARoot;
        private Transform _segmentBRoot;
        private Transform _segmentCRoot;
        private Transform _arrivalRoot;

        private Transform _fade;
        private MaterialPropertyBlock _fadeMpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private BackyardLighting _lighting;

        /// <summary>One of the three fixed-distance corridor segments the exit side builds (MV-964 §5),
        /// exposed so the two corridor-dressing tickets can parent their art to the right one. Null for
        /// an arrival-side instance, and null for a segment before it's built.</summary>
        public Transform CorridorSegmentRoot(char segment) => segment switch
        {
            'A' => _segmentARoot,
            'B' => _segmentBRoot,
            'C' => _segmentCRoot,
            _ => null,
        };

        /// <summary>The arrival shell's root, for the same dressing purpose. Null for an exit-side
        /// instance.</summary>
        public Transform ArrivalRoot => _arrivalRoot;

        /// <summary>Running until it hands off. A test reads this to prove it reaches the end.</summary>
        public bool IsPlaying => _phase != Phase.Done;

        // ------------------------------------------------------------------ build (exit)

        /// <summary>The test-facing entry point (MV-845's own idiom: a MonoBehaviour without
        /// <c>[ExecuteAlways]</c> only ever receives <c>Awake</c> once Unity is actually in Play Mode,
        /// which this EditMode-only suite never enters, MV-299/311/330). Builds the door and corridor and
        /// skips straight to the suspended walk, as if Max had already crossed the threshold — the real
        /// game's own "walk up to the door under full control" leg is ordinary, un-scripted gameplay now,
        /// not something a test needs to simulate.</summary>
        public void Initialize(WorldConfig fromCfg, MapData fromMap, WorldTransitionEntry entry, int fromWorldIndex,
            PlayerController player, System.Action onFinished = null, AreaGate exitGate = null)
        {
            _mode = Mode.Exit;
            _onFinished = onFinished;
            BuildExit(fromCfg, fromMap, entry, fromWorldIndex, player, exitGate);
            BeginSuspendedWalk();
        }

        private void BuildExit(WorldConfig fromCfg, MapData fromMap, WorldTransitionEntry entry, int fromWorldIndex,
            PlayerController player, AreaGate exitGate)
        {
            _entry = entry;
            _fromWorldIndex = fromWorldIndex;
            _wall = entry.ExitWall;
            _player = player;
            _playerT = player.transform;
            _cc = player.GetComponent<CharacterController>();

            float wallHeight = ResolveWallHeight(fromMap);
            float wallThickness = ResolveWallThickness(fromMap);

            WorldArea exitArea = entry.ExitArea(fromCfg);
            _doorMouth = exitArea != null ? entry.ExitDoorMouth(fromCfg)
                : new Vector2(_playerT.position.x, _playerT.position.z);

            _lighting = FindFirstObjectByType<BackyardLighting>();
            if (_lighting == null) _lighting = new GameObject("BackyardLighting").AddComponent<BackyardLighting>();

            // MV-997: the door is real map geometry now, built closed by MapRuntime at boot -- open THAT
            // gate rather than cutting a fresh gap at runtime. CutWallGap stays for the arrival side only
            // (InitializeArrival, below), whose shell is still built outside the destination stub's own
            // wall. exitGate is null only for a fixture/test with no real map geometry at all, in which
            // case there is nothing here to open and the sequence just has no visible door.
            _doorGate = exitGate;
            if (_doorGate != null) _doorGate.ForceOpen();

            BuildExitCorridor(wallHeight, wallThickness);

            int toWorld = fromWorldIndex + 1;
            Vector2 farMouth = PointAtXZ(_doorMouth, _wall, entry.CorridorLength);
            AreaGate farGate = BuildDoor(farMouth, _wall, wallHeight, wallThickness, null, transform, "World Join Far Gate");
            ApplyDestinationSkin(farGate, toWorld);

            BuildFade();
        }

        /// <summary>Floor + both side walls, built as three fixed-distance segments so the corridor reads
        /// right (World-from's colours giving way to World-to's) before the dressing tickets land any
        /// real art (MV-964 §5). Static per-segment materials — no per-frame property-block driving —
        /// since colour now changes by PLACE, not by how far Max has personally walked.</summary>
        private void BuildExitCorridor(float wallHeight, float wallThickness)
        {
            Transform root = new GameObject("World Join Corridor").transform;
            root.SetParent(transform, worldPositionStays: false);

            int toWorld = _fromWorldIndex + 1;
            BiomePalette from = BiomePalette.ForWorld(_fromWorldIndex);
            BiomePalette to = BiomePalette.ForWorld(toWorld);
            string keyPrefix = $"join{_fromWorldIndex}";

            Material floorA = IntroBuild.Lit($"{keyPrefix}_A_floor", from.ColorFor(SurfaceKind.Ground));
            Material floorB = IntroBuild.Lit($"{keyPrefix}_B_floor", Color.Lerp(from.ColorFor(SurfaceKind.Ground), to.ColorFor(SurfaceKind.Ground), 0.5f));
            Material floorC = IntroBuild.Lit($"{keyPrefix}_C_floor", to.ColorFor(SurfaceKind.Ground));

            _segmentARoot = BuildSegment(root, "A", wallHeight, wallThickness, 0f, _entry.SegmentAEnd,
                floorA, IntroBuild.Lit($"{keyPrefix}_A_wall", from.ColorFor(SurfaceKind.Wall)));

            _segmentBRoot = BuildSegment(root, "B", wallHeight, wallThickness, _entry.SegmentAEnd, _entry.SegmentBEnd,
                floorB, IntroBuild.Lit($"{keyPrefix}_B_wall", Color.Lerp(from.ColorFor(SurfaceKind.Wall), to.ColorFor(SurfaceKind.Wall), 0.5f)));

            _segmentCRoot = BuildSegment(root, "C", wallHeight, wallThickness, _entry.SegmentBEnd, _entry.CorridorLength,
                floorC, IntroBuild.Lit($"{keyPrefix}_C_wall", to.ColorFor(SurfaceKind.Wall)));

            // MV-1076: ground to stand beside the corridor and past its far end -- without this, the
            // camera's own clear colour shows the instant Max is far enough along that the walls no
            // longer fill his view (void here; a flat destination-world colour on the arrival side,
            // below).
            BuildGroundApron(root, wallThickness,
                (0f, _entry.SegmentAEnd, floorA),
                (_entry.SegmentAEnd, _entry.SegmentBEnd, floorB),
                (_entry.SegmentBEnd, _entry.CorridorLength, floorC));

            // MV-965: garden/kerb+grate/culvert set-dressing on top of the three flat-coloured shells
            // above — a separate pass, same reason StormdrainDressing/BackyardDressing are separate from
            // MapRuntime.Build, only ever relevant to the World 1 -> World 2 row this ticket covers.
            if (_fromWorldIndex == 0)
                WorldJoinDressing.DressExit(_segmentARoot, _segmentBRoot, _segmentCRoot, _doorMouth, _wall, wallHeight, _entry);
            // MV-967: outfall/breach/hull set-dressing for the World 2 -> World 3 row.
            else if (_fromWorldIndex == 1)
                WorldJoinDressing.DressExitReef(_segmentARoot, _segmentBRoot, _segmentCRoot, _doorMouth, _wall, wallHeight, _entry);
        }

        // ------------------------------------------------------------------ build (arrival)

        /// <summary>Builds the arrival shell outside the destination stub's own wall, cuts the real gap
        /// into the stub, and walks Max in through it (MV-964 §4.7). Suspends gameplay for the whole
        /// beat -- there is nothing for Max to control yet, the world has only just booted.</summary>
        public void InitializeArrival(WorldConfig toCfg, MapData toMap, WorldTransitionEntry entry, int fromWorldIndex,
            PlayerController player, System.Action onFinished)
        {
            _mode = Mode.Arrival;
            _entry = entry;
            _fromWorldIndex = fromWorldIndex;
            _wall = entry.ArrivalWall;
            _onFinished = onFinished;
            _player = player;
            _playerT = player.transform;
            _cc = player.GetComponent<CharacterController>();

            float wallHeight = ResolveWallHeight(toMap);
            float wallThickness = ResolveWallThickness(toMap);
            int toWorld = fromWorldIndex + 1;

            WorldArea stub = entry.ArrivalArea(toCfg);
            _doorMouth = stub != null ? entry.ArrivalDoorMouth(toCfg)
                : new Vector2(_playerT.position.x, _playerT.position.z);

            _lighting = FindFirstObjectByType<BackyardLighting>();
            if (_lighting == null) _lighting = new GameObject("BackyardLighting").AddComponent<BackyardLighting>();

            SuspendGameplay();

            CutWallGap(_doorMouth, _wall, wallHeight, WorldTransitionEntry.DoorWidth, out Material wallMaterial);
            ClearOverheadDressingNearDoor(_doorMouth, _wall, wallHeight, WorldTransitionEntry.DoorWidth);
            _doorGate = BuildDoor(_doorMouth, _wall, wallHeight, wallThickness, wallMaterial, transform, "World Arrival Door");
            ApplyDestinationSkin(_doorGate, toWorld);
            _doorGate.ForceOpen();

            BuildArrivalShell(wallHeight, wallThickness, toWorld);
            BuildFade();
            ApplyFadeAlpha(1f);   // fully black -- the world has just booted, nothing to see yet

            float startAlong = entry.ArrivalShellLength - 1f;   // AC (MV-964 §4.7): "1 m from its far end"
            Vector3 start = PointAt(_doorMouth, _wall, startAlong, _playerT.position.y);
            TeleportPlayer(start);
            _playerT.rotation = Quaternion.LookRotation(-OutwardDir(_wall), Vector3.up);   // facing the stub

            _phase = Phase.FadeIn;
            _phaseElapsed = 0f;
        }

        private void BuildArrivalShell(float wallHeight, float wallThickness, int toWorld)
        {
            BiomePalette palette = BiomePalette.ForWorld(toWorld);
            Material floorMat = IntroBuild.Lit($"arrival{toWorld}_floor", palette.ColorFor(SurfaceKind.Ground));
            _arrivalRoot = BuildSegment(transform, "Arrival", wallHeight, wallThickness, 0f, _entry.ArrivalShellLength,
                floorMat, IntroBuild.Lit($"arrival{toWorld}_wall", palette.ColorFor(SurfaceKind.Wall)));

            // MV-1076: same reasoning as the exit corridor's own apron -- the shell's far end (the end
            // away from the destination stub, where Max first appears) has nothing beyond it either.
            BuildGroundApron(_arrivalRoot, wallThickness, (0f, _entry.ArrivalShellLength, floorMat));

            // MV-965: same dressing pass as the exit side's segment C, continued into the arrival shell.
            if (toWorld == 1)
                WorldJoinDressing.DressArrival(_arrivalRoot, _doorMouth, _wall, wallHeight, _entry.ArrivalShellLength);
            // MV-967: same idea for the World 3 arrival shell — segment C's hull dressing continued.
            else if (toWorld == 2)
                WorldJoinDressing.DressArrivalReef(_arrivalRoot, _doorMouth, _wall, wallHeight, _entry.ArrivalShellLength);
        }

        // ------------------------------------------------------------------ shared geometry

        private static float ResolveWallHeight(MapData map) =>
            map != null && map.wallHeight > 0f ? map.wallHeight : MapData.DefaultWallHeight;

        private static float ResolveWallThickness(MapData map) =>
            map != null ? map.wallThickness : MapData.DefaultWallThickness;

        private static void ApplyDestinationSkin(AreaGate gate, int toWorld)
        {
            if (toWorld == 1) gate.ApplyStormdrainGateSkin();
            else if (toWorld >= 2) gate.ApplyReefSkin();
        }

        /// <summary>The world-outward direction for a wall — N/E extend toward +Z/+X, S/W toward -Z/-X
        /// (matches <see cref="WorldArea.WallCoord"/>'s own "which side is out" convention).</summary>
        private static Vector3 OutwardDir(Wall wall) => wall switch
        {
            Wall.N => Vector3.forward,
            Wall.S => Vector3.back,
            Wall.E => Vector3.right,
            Wall.W => Vector3.left,
            _ => Vector3.forward,
        };

        private static Vector3 AcrossDir(Wall wall)
        {
            Vector3 d = OutwardDir(wall);
            return new Vector3(-d.z, 0f, d.x);
        }

        /// <summary>The point <paramref name="along"/> metres outward from <paramref name="doorMouth"/>,
        /// along <paramref name="wall"/>'s own outward axis (XZ only).</summary>
        private static Vector2 PointAtXZ(Vector2 doorMouth, Wall wall, float along)
        {
            Vector3 dir = OutwardDir(wall);
            bool travelAlongZ = dir.x == 0f;
            float delta = along * (travelAlongZ ? dir.z : dir.x);
            return travelAlongZ ? new Vector2(doorMouth.x, doorMouth.y + delta) : new Vector2(doorMouth.x + delta, doorMouth.y);
        }

        private static Vector3 PointAt(Vector2 doorMouth, Wall wall, float along, float y)
        {
            Vector2 xz = PointAtXZ(doorMouth, wall, along);
            return new Vector3(xz.x, y, xz.y);
        }

        /// <summary>How far outward (metres) <paramref name="pos"/> has travelled past
        /// <paramref name="doorMouth"/>'s own wall line — negative means still short of it.</summary>
        private static float AlongDistance(Vector3 pos, Vector2 doorMouth, Wall wall)
        {
            Vector3 dir = OutwardDir(wall);
            bool travelAlongZ = dir.x == 0f;
            float raw = travelAlongZ ? pos.z - doorMouth.y : pos.x - doorMouth.x;
            return raw * (travelAlongZ ? dir.z : dir.x);
        }

        /// <summary>Finds the live structural wall segment covering the door's own probe point and splits
        /// it around the door span, so the wall stays solid everywhere else and Max's own
        /// CharacterController can actually walk through the gap. Axis-generic (MV-964): splits along X
        /// for an N/S wall, along Z for an E/W one. A no-op if no such wall is found (e.g. an EditMode
        /// test that never built real map geometry).</summary>
        private static void CutWallGap(Vector2 doorMouth, Wall wall, float wallHeight, float doorWidth, out Material wallMaterial)
        {
            wallMaterial = null;
            bool alongX = wall == Wall.N || wall == Wall.S;
            var probe = new Vector3(doorMouth.x, wallHeight * 0.5f, doorMouth.y);

            StructuralWall found = null;
            foreach (StructuralWall w in FindObjectsByType<StructuralWall>(FindObjectsSortMode.None))
            {
                var col = w.GetComponent<Collider>();
                if (col != null && col.bounds.Contains(probe)) { found = w; break; }
            }
            if (found == null) return;

            GameObject wallGo = found.gameObject;
            var wallCollider = wallGo.GetComponent<BoxCollider>();
            var wallRenderer = wallGo.GetComponent<MeshRenderer>();
            if (wallCollider == null) return;

            wallMaterial = wallRenderer != null ? wallRenderer.sharedMaterial : null;

            Bounds b = wallCollider.bounds;
            float coord = alongX ? doorMouth.x : doorMouth.y;
            float doorMin = coord - doorWidth * 0.5f;
            float doorMax = coord + doorWidth * 0.5f;
            float bMin = alongX ? b.min.x : b.min.z;
            float bMax = alongX ? b.max.x : b.max.z;

            ResizeWallAxis(wallGo, alongX, bMin, doorMin);

            if (bMax - doorMax > 0.05f)
            {
                GameObject clone = Instantiate(wallGo, wallGo.transform.parent);
                clone.name = wallGo.name + " (MV-964 far side of door)";
                ResizeWallAxis(clone, alongX, doorMax, bMax);
            }
        }

        /// <summary>MV-1077: the destination stub's own wall was dressed (e.g.
        /// <c>StormdrainDressing.Dress</c>'s wall-hugging overhead main/collars/junction boxes, built
        /// inset into the room at head height along the wall's full original length) before this gap
        /// ever existed — <see cref="CutWallGap"/> only resizes the STRUCTURAL wall's own collider/mesh,
        /// never the decorative overhead kit crossing the same span. Disables (never destroys -- these
        /// carry no collider, see <c>StormdrainKit.Strip</c>) every enabled renderer sitting anywhere
        /// under the one host every overhead piece is built under (<c>StormdrainDressing.Dress</c>'s own
        /// <c>new GameObject("Overhead")</c> — a leaf part's own name, e.g. a junction box's "Pool" light
        /// decal, never carries "Overhead" itself, only its root does) whose bounds fall inside a
        /// generous box around the new door: reach 4 m into the room, the door's own width plus margin
        /// across it, floor to a metre above the wall.</summary>
        private const float OverheadClearanceReach = 4f;
        private const float OverheadClearanceAcrossMargin = 0.5f;
        private const float OverheadClearanceHeightMargin = 1f;
        private const string OverheadHostName = "Overhead";

        private static void ClearOverheadDressingNearDoor(Vector2 doorMouth, Wall wall, float wallHeight, float doorWidth)
        {
            Vector3 outDir = OutwardDir(wall);
            Vector3 acrossDir = AcrossDir(wall);
            Vector3 inDir = -outDir;

            float acrossSize = doorWidth + OverheadClearanceAcrossMargin * 2f;
            float height = wallHeight + OverheadClearanceHeightMargin;

            Vector3 center = new Vector3(doorMouth.x, height * 0.5f, doorMouth.y) + inDir * (OverheadClearanceReach * 0.5f);
            Vector3 size = new Vector3(
                Mathf.Abs(outDir.x) * OverheadClearanceReach + Mathf.Abs(acrossDir.x) * acrossSize,
                height,
                Mathf.Abs(outDir.z) * OverheadClearanceReach + Mathf.Abs(acrossDir.z) * acrossSize);
            var clearance = new Bounds(center, size);

            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled) continue;
                if (!UnderOverheadHost(r.transform)) continue;
                if (clearance.Intersects(r.bounds)) r.enabled = false;
            }
        }

        private static bool UnderOverheadHost(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                if (p.name == OverheadHostName) return true;
            return false;
        }

        private static void ResizeWallAxis(GameObject wallGo, bool alongX, float min, float max)
        {
            float length = max - min;
            if (length <= 0.02f) { wallGo.SetActive(false); return; }

            var collider = wallGo.GetComponent<BoxCollider>();
            Vector3 size = alongX
                ? new Vector3(length, collider.size.y, collider.size.z)
                : new Vector3(collider.size.x, collider.size.y, length);

            Vector3 pos = wallGo.transform.position;
            wallGo.transform.position = alongX
                ? new Vector3((min + max) * 0.5f, pos.y, pos.z)
                : new Vector3(pos.x, pos.y, (min + max) * 0.5f);
            collider.size = size;

            var mf = wallGo.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = CharacterMeshes.Bevelled(size, CharacterMeshes.DefaultBevel(size));
        }

        private static AreaGate BuildDoor(Vector2 doorMouth, Wall wall, float wallHeight, float wallThickness,
            Material wallMaterial, Transform parent, string name)
        {
            bool doorAlongX = wall == Wall.N || wall == Wall.S;
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = name;
            body.transform.SetParent(parent, worldPositionStays: false);

            float sealWidth = WorldTransitionEntry.DoorWidth + wallThickness * 2f;
            body.transform.position = new Vector3(doorMouth.x, wallHeight * 0.5f, doorMouth.y);
            // A door on an E/W wall runs along Z -- spin 90 deg the same way MapRuntime.BuildAreaGate
            // does for one, so AreaGate.StartHingeSwing (which reads localScale.x as "the width") finds
            // its hinge pivot on the right edge instead of the thin one.
            body.transform.rotation = doorAlongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            body.transform.localScale = new Vector3(sealWidth, wallHeight, wallThickness + 0.04f);

            var rend = body.GetComponent<MeshRenderer>();
            rend.sharedMaterial = wallMaterial != null ? wallMaterial : IntroBuild.Lit("join_door", new Color(0.35f, 0.33f, 0.30f));

            var gate = body.AddComponent<AreaGate>();
            gate.AwayFromPlayerDirection = OutwardDir(wall);
            return gate;
        }

        /// <summary>Floor + both side walls between <paramref name="alongMin"/> and
        /// <paramref name="alongMax"/> (metres outward from <see cref="_doorMouth"/>), parented under
        /// their own root so a dressing ticket can attach art to exactly this stretch.</summary>
        private Transform BuildSegment(Transform parent, string label, float wallHeight, float wallThickness,
            float alongMin, float alongMax, Material floorMat, Material wallMat)
        {
            Transform segRoot = new GameObject($"Corridor Segment {label}").transform;
            segRoot.SetParent(parent, worldPositionStays: false);

            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            BuildBox(segRoot, $"Segment {label} Floor", alongMin, alongMax, 0f, WorldTransitionEntry.CorridorWidth, 0.1f, -0.05f, floorMat);
            BuildBox(segRoot, $"Segment {label} Wall 1", alongMin, alongMax, halfWidth + wallThickness * 0.5f, wallThickness, wallHeight, wallHeight * 0.5f, wallMat);
            BuildBox(segRoot, $"Segment {label} Wall 2", alongMin, alongMax, -(halfWidth + wallThickness * 0.5f), wallThickness, wallHeight, wallHeight * 0.5f, wallMat);

            return segRoot;
        }

        /// <summary>A world-axis-aligned box spanning [<paramref name="alongMin"/>, <paramref name="alongMax"/>]
        /// outward from <see cref="_doorMouth"/> along its wall's own travel axis, offset
        /// <paramref name="acrossOffset"/> across it. <see cref="OutwardDir"/>/<see cref="AcrossDir"/> are
        /// always purely axial (never diagonal — every exit/arrival wall is N, E, S or W), so this size
        /// formula is exact for either orientation without ever rotating the box itself.</summary>
        private GameObject BuildBox(Transform parent, string name, float alongMin, float alongMax,
            float acrossOffset, float acrossSize, float height, float centerY, Material mat)
        {
            Vector3 dir = OutwardDir(_wall);
            Vector3 across = AcrossDir(_wall);
            float mid = (alongMin + alongMax) * 0.5f;
            float length = alongMax - alongMin;

            Vector3 center = new Vector3(_doorMouth.x, centerY, _doorMouth.y) + dir * mid + across * acrossOffset;
            Vector3 size = new Vector3(
                Mathf.Abs(dir.x) * length + Mathf.Abs(across.x) * acrossSize,
                height,
                Mathf.Abs(dir.z) * length + Mathf.Abs(across.z) * acrossSize);

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = center;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        /// <summary>MV-1076: at least this wide either side of the corridor/shell's own playable width,
        /// and this far past the last span's own far end (ticket's own numbers).</summary>
        private const float ApronSideWidth = 12f;
        private const float ApronFarExtension = 6f;
        private const float ApronHeight = 0.1f;
        private const float ApronCenterY = -0.05f;   // matches BuildSegment's own floor box -- tops stay coplanar

        /// <summary>Flat ground beside each <paramref name="spans"/> entry's own along-range (its own
        /// floor colour, so the apron reads as a continuation of the segment it runs beside, not one
        /// flat colour the whole way), plus a cap <see cref="ApronFarExtension"/> m past the last span's
        /// own far end. Without this, the camera's own clear colour shows the moment the corridor or
        /// arrival shell runs out of either world's ground (MV-1076: void beside the World 1 -> World 2
        /// corridor, a flat blue beside its arrival shell). KeepsOwnMaterial + no collider: decoration
        /// only, never reachable by Max or a robot, and never re-tinted by the world's own dressing
        /// sweep.</summary>
        private void BuildGroundApron(Transform root, float wallThickness,
            params (float AlongMin, float AlongMax, Material FloorMat)[] spans)
        {
            if (spans.Length == 0) return;

            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            float sideCenter = halfWidth + wallThickness + ApronSideWidth * 0.5f;

            foreach (var span in spans)
            {
                MarkApron(BuildBox(root, "Ground Apron E", span.AlongMin, span.AlongMax, sideCenter,
                    ApronSideWidth, ApronHeight, ApronCenterY, span.FloorMat));
                MarkApron(BuildBox(root, "Ground Apron W", span.AlongMin, span.AlongMax, -sideCenter,
                    ApronSideWidth, ApronHeight, ApronCenterY, span.FloorMat));
            }

            var last = spans[spans.Length - 1];
            float capWidth = (halfWidth + wallThickness + ApronSideWidth) * 2f;
            MarkApron(BuildBox(root, "Ground Apron Far Cap", last.AlongMax, last.AlongMax + ApronFarExtension,
                0f, capWidth, ApronHeight, ApronCenterY, last.FloorMat));
        }

        /// <summary>Decoration only: strips the primitive cube's default collider (ticket AC: "no
        /// collider that Max or robots can reach") and marks it <see cref="KeepsOwnMaterial"/> so neither
        /// world's own runtime dressing sweep repaints it.</summary>
        private static void MarkApron(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            go.AddComponent<KeepsOwnMaterial>();
        }

        private void BuildFade()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            _fadeMpb = new MaterialPropertyBlock();
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "World Join Fade";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            go.transform.SetParent(cam.transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 0f, cam.nearClipPlane + 0.1f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(3f, 3f, 1f);

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.AlphaBlend(VfxMaterials.Solid());
            r.shadowCastingMode = ShadowCastingMode.Off;
            _fade = go.transform;
            ApplyFadeAlpha(0f);
        }

        private void ApplyFadeAlpha(float alpha)
        {
            if (_fade == null) return;
            var r = _fade.GetComponent<MeshRenderer>();
            if (r == null) return;
            Color c = Color.black; c.a = Mathf.Clamp01(alpha);
            r.GetPropertyBlock(_fadeMpb);
            _fadeMpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_fadeMpb);
            r.enabled = c.a > 0.001f;
        }

        // ------------------------------------------------------------------ suspend / restore

        private void SuspendGameplay()
        {
            if (_player != null) _player.enabled = false;

            _hud = FindFirstObjectByType<HudController>();
            if (_hud != null) _hud.gameObject.SetActive(false);

            // MV-1081: freeze, don't disable -- disabling fired OnEnable's own ResetState on restore,
            // which stomped every robot's state/health/Submerged body-visibility/IsConverted back to a
            // fresh full-health Chase the moment Max arrived (a Dormant garrison waking, an invisible,
            // unkillable Submerged Lurker chasing him). SetCutsceneFrozen never touches OnEnable at all.
            _frozenRobots.Clear();
            _frozenRobots.AddRange(RobotEnemy.Active);
            foreach (RobotEnemy r in _frozenRobots)
                if (r != null) r.SetCutsceneFrozen(true);
        }

        private void RestoreGameplay()
        {
            if (_restored) return;
            _restored = true;
            if (_player != null) _player.enabled = true;
            if (_hud != null) _hud.gameObject.SetActive(true);
            foreach (RobotEnemy r in _frozenRobots) if (r != null) r.SetCutsceneFrozen(false);
            _frozenRobots.Clear();
        }

        // ------------------------------------------------------------------ running it

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advance the sequence by <paramref name="dt"/> seconds. Public so a test can drive it
        /// without a live frame loop.</summary>
        public void Tick(float dt)
        {
            switch (_phase)
            {
                case Phase.AwaitingCrossing: TickAwaitingCrossing(); break;
                case Phase.WalkToEnd: TickWalkToEnd(dt); break;
                case Phase.HoldAtEnd: TickHoldAtEnd(dt); break;
                case Phase.FadeOut: TickFadeOut(dt); break;
                case Phase.FadeIn: TickFadeIn(dt); break;
                case Phase.ArrivalWalk: TickArrivalWalk(dt); break;
                case Phase.Done: break;
            }
        }

        /// <summary>MV-964 §4.2: Max keeps full control (and robots keep behaving) until he's a metre
        /// past the door line -- this is the only phase that doesn't already have gameplay suspended.</summary>
        private void TickAwaitingCrossing()
        {
            if (_playerT == null) return;
            if (AlongDistance(_playerT.position, _doorMouth, _wall) >= 1f) BeginSuspendedWalk();
        }

        /// <summary>MV-964 §4.2: the moment Max crosses, gameplay suspends, any Weapon Core still on the
        /// ground is banked as if he'd walked over it, and the door swings shut behind him.</summary>
        private void BeginSuspendedWalk()
        {
            SuspendGameplay();
            PickupDirector.EnsureInstalled().CollectGroundedWeaponCore();
            if (_doorGate != null) _doorGate.Reclose();
            TeleportPlayer(PointAt(_doorMouth, _wall, 1f, _playerT.position.y));

            _phase = Phase.WalkToEnd;
            _phaseElapsed = 0f;
        }

        private void TickWalkToEnd(float dt)
        {
            ApplyCorridorLighting();

            Vector3 target = PointAt(_doorMouth, _wall, _entry.CorridorLength - WalkEndClearance, _playerT.position.y);
            _phaseElapsed += dt;

            if (_phaseElapsed >= WalkTimeoutSeconds)
            {
                TeleportPlayer(target);
                EnterHoldAtEnd();
                return;
            }

            if (StepToward(target, dt)) EnterHoldAtEnd();
        }

        /// <summary>Fog and lighting follow Max down the corridor (MV-964 §4.4), reaching the destination
        /// world's look at <see cref="LightingBlendFraction"/> of the way along it.</summary>
        private void ApplyCorridorLighting()
        {
            if (_lighting == null) return;
            float along = AlongDistance(_playerT.position, _doorMouth, _wall);
            float t = Mathf.Clamp01(along / (LightingBlendFraction * _entry.CorridorLength));
            _lighting.Apply(BackyardLook.Lerp(BackyardLook.ForWorld(_fromWorldIndex), BackyardLook.ForWorld(_fromWorldIndex + 1), t));
        }

        private void EnterHoldAtEnd() { _phase = Phase.HoldAtEnd; _phaseElapsed = 0f; }

        private void TickHoldAtEnd(float dt)
        {
            _phaseElapsed += dt;
            if (_phaseElapsed >= HoldAtEndSeconds) { _phase = Phase.FadeOut; _phaseElapsed = 0f; }
        }

        private void TickFadeOut(float dt)
        {
            _phaseElapsed += dt;
            float alpha = FadeDuration > 0f ? Mathf.Clamp01(_phaseElapsed / FadeDuration) : 1f;
            ApplyFadeAlpha(alpha);
            if (alpha >= 1f)
            {
                // MV-964 §4.5: the signal fires only once the screen is fully black -- RunTracker seals
                // on it and the Result card shows over that same black, not over the live corridor.
                HudSignals.EmitFinaleGateCrossed();
                Finish();
            }
        }

        private void TickFadeIn(float dt)
        {
            _phaseElapsed += dt;
            float alpha = FadeDuration > 0f ? 1f - Mathf.Clamp01(_phaseElapsed / FadeDuration) : 0f;
            ApplyFadeAlpha(alpha);
            if (alpha <= 0f) { _phase = Phase.ArrivalWalk; _phaseElapsed = 0f; }
        }

        private const float ArrivalInsideOffset = 1.5f;

        private void TickArrivalWalk(float dt)
        {
            Vector3 target = PointAt(_doorMouth, _wall, -ArrivalInsideOffset, _playerT.position.y);
            _phaseElapsed += dt;

            if (_phaseElapsed >= WalkTimeoutSeconds)
            {
                TeleportPlayer(target);
                FinishArrival();
                return;
            }

            if (StepToward(target, dt)) FinishArrival();
        }

        private void FinishArrival()
        {
            if (_doorGate != null) _doorGate.Reclose();
            Finish();
        }

        /// <summary>Steps Max toward <paramref name="target"/> at his normal walk speed via his own
        /// <see cref="CharacterController"/> (so he collides with whatever's actually there) and turns
        /// him to face the travel direction. Returns true once he's within <see cref="ArrivalEpsilon"/>
        /// of the target, measured AFTER the move, so a collision-shortened step is never mistaken for
        /// arrival.</summary>
        private bool StepToward(Vector3 target, float dt)
        {
            Vector3 pos = _playerT.position;
            Vector3 toTarget = target - pos; toTarget.y = 0f;
            float dist = toTarget.magnitude;

            if (dist > ArrivalEpsilon)
            {
                Vector3 dir = toTarget / dist;
                float step = Mathf.Min(_player.WalkSpeed * dt, dist);
                if (_cc != null) CharacterControllerMotion.SafeMove(_cc, dir * step);
                else _playerT.position = pos + dir * step;
                FaceDirection(dir, dt);
            }

            Vector3 remaining = target - _playerT.position; remaining.y = 0f;
            return remaining.magnitude <= ArrivalEpsilon;
        }

        private void FaceDirection(Vector3 dir, float dt)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion target = Quaternion.LookRotation(dir, Vector3.up);
            _playerT.rotation = Quaternion.RotateTowards(_playerT.rotation, target, RotationSpeedDegPerSec * dt);
        }

        /// <summary>Same disable-move-restore idiom as <c>MapRuntime.Adopt</c> -- a CharacterController
        /// caches its own position and will happily undo a plain <c>transform.position</c> assignment
        /// otherwise.</summary>
        private void TeleportPlayer(Vector3 worldPos)
        {
            bool was = _cc != null && _cc.enabled;
            if (_cc != null) _cc.enabled = false;
            Vector3 before = _playerT.position;
            _playerT.position = worldPos;

            // MV-1021: same guard MapRuntime.Adopt/WorldRunner.RespawnPlayer carry — see MapRuntime's
            // own comment.
            if (_cc != null && was)
            {
                if (CharacterControllerSafety.CanCreate(_playerT, _cc, out string teleportReason))
                {
                    _cc.enabled = true;
                }
                else
                {
                    _playerT.position = before;
                    _cc.enabled = true;
                    CharacterControllerSafety.LogRefusal("WorldJoinSequence.TeleportPlayer", _playerT.name,
                        teleportReason, worldPos, _playerT.lossyScale);
                }
            }
            else if (_cc != null)
            {
                _cc.enabled = was;
            }
        }

        // ------------------------------------------------------------------ handoff

        /// <summary>The exit side deliberately does NOT restore gameplay or destroy itself here — the
        /// screen must stay black (the Result card shows over it) until the scene reloads, either via
        /// NEXT WORLD or HOME. The arrival side, by contrast, is the live scene from here on: control and
        /// the HUD come back for real.</summary>
        private void Finish()
        {
            if (_phase == Phase.Done) return;
            _phase = Phase.Done;

            if (_mode == Mode.Arrival)
            {
                RestoreGameplay();
                _onFinished?.Invoke();
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
                return;
            }

            _onFinished?.Invoke();
        }

        private void OnDestroy()
        {
            RestoreGameplay();
            // The fade quad is parented to the real gameplay camera, not to this transform, so it
            // doesn't fall away for free when this GameObject is destroyed -- clean it up explicitly.
            if (_fade != null)
            {
                GameObject fadeGo = _fade.gameObject;
                _fade = null;
                if (Application.isPlaying) Destroy(fadeGo);
                else DestroyImmediate(fadeGo);
            }
        }
    }
}
