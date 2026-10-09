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
    ///    (MV-1123) robots freeze but Max keeps walking the corridor himself; the far door opens on its
    ///    own proximity and the fade starts once he's walked past it.
    ///  * <see cref="TryPlayArrival"/> — installed on the destination world's own boot when
    ///    <see cref="WorldTransitions.PendingArrivalFrom"/> is set. Builds the arrival shell outside the
    ///    destination stub's wall, fades the world in, and (MV-1123) hands Max control the instant the
    ///    fade lifts -- no scripted walk-in.
    ///
    /// <see cref="Initialize"/> is the third, test-facing entry point (MV-845's own idiom): builds the
    /// exit geometry and skips straight to the corridor walk, as if Max had already crossed -- a test
    /// drives it with <see cref="Tick"/> exactly as before this ticket, it just no longer has to walk him
    /// to the door first (that leg is ordinary, un-scripted gameplay now, not this class's job).
    /// </summary>
    [DisallowMultipleComponent]
    [MaxWorlds.Core.PerfSection("intro")]
    public sealed class WorldJoinSequence : MonoBehaviour
    {
        private const float FadeDuration = 0.4f;

        /// <summary>MV-849: the corridor's lighting/fog reach the destination world's look this fraction
        /// of the way along it — arriving a little early reads better than the blend still finishing
        /// right as the fade-to-black starts.</summary>
        private const float LightingBlendFraction = 0.73f;

        // --- MV-1123: Max keeps full control the whole corridor — no scripted walk, no teleport, no
        // HUD drop. The far door opens on proximity and the fade starts once he's walked past it on his
        // own, so the old WalkTimeoutSeconds/StepToward/FaceDirection machinery (and the exit side's own
        // scripted stop point, WalkEndClearance) has nothing left to drive.

        /// <summary>How close Max must get to the far door before it slides open (MV-1123 §5).</summary>
        private const float FarDoorOpenDistance = 6f;

        /// <summary>How far past the far door Max must walk before the screen starts fading (MV-1123 §6).</summary>
        private const float FarDoorCrossClearance = 2f;

        /// <summary>How far inside the arrival area's own wall Max must walk before the arrival door
        /// re-closes behind him, and before this sequence hands control back for good (MV-1123 §8).</summary>
        private const float ArrivalInsideOffset = 1.5f;

        // --- MV-1123 §3: unlit guide-light floor strips, every world's corridor and arrival shell.

        private const float GuideLightLength = 1.2f;
        private const float GuideLightWidth = 0.15f;
        private const float GuideLightPitch = 4f;
        private const float GuideLightInset = 0.9f;
        private const float GuideLightY = 0.011f;   // just proud of the floor's own top (y = 0)

        // --- MV-1123 §2: solid banks replacing the old floor-level ground apron.

        private const float BankSideWidth = 15f;
        private const float BankEndExtension = 6f;
        private const float BankTint = 0.7f;

        // --- MV-1123 §5: the far/arrival door's own jamb lamps — red while closed, green while open.

        private const float JambLampDiameter = 0.25f;
        private static readonly Color DoorLampRed = new Color(0.85f, 0.12f, 0.10f);
        private static readonly Color DoorLampGreen = new Color(0.208f, 0.878f, 0.420f);   // #35E06B

        // --- MV-1123 §7: the title card holds this long after the destination world's own fade-in.

        private const float TitleCardHoldAfterFadeIn = 1.5f;

        // ------------------------------------------------------------------ static installation

        private enum Mode { Exit, Arrival }
        private enum Phase { AwaitingCrossing, CorridorWalk, FadeOut, FadeIn, PlayerControl, Done }

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

        // --- MV-1123: the far gate (exit side only — the arrival side's own door is _doorGate, above)
        // plus whichever gate's jamb lamps this instance is driving.
        private AreaGate _farGate;
        private Vector2 _farDoorMouth;
        private bool _farDoorOpened;
        private Renderer _doorLampLRend;
        private Renderer _doorLampRRend;

        private string _titleWorldLine;
        private string _titleNameLine;
        private WorldJoinTitleCard _titleCard;
        private float _titleCardHoldRemaining = -1f;

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

        /// <summary>MV-1123 §7: the title card's own "WORLD n" line, resolved from the destination
        /// config's <c>world</c> field — exposed so a test can assert it without reading UI text.</summary>
        public string TitleWorldLine => _titleWorldLine;

        /// <summary>The title card's own world-name line (e.g. "STORMDRAIN").</summary>
        public string TitleNameLine => _titleNameLine;

        /// <summary>The far gate, Exit mode only — null for an Arrival-mode instance, and null until
        /// <see cref="BuildExit"/> has run.</summary>
        public AreaGate FarGate => _farGate;

        // ------------------------------------------------------------------ build (exit)

        /// <summary>The test-facing entry point (MV-845's own idiom: a MonoBehaviour without
        /// <c>[ExecuteAlways]</c> only ever receives <c>Awake</c> once Unity is actually in Play Mode,
        /// which this EditMode-only suite never enters, MV-299/311/330). Builds the door and corridor and
        /// skips straight to the corridor walk, as if Max had already crossed the threshold — the real
        /// game's own "walk up to the door under full control" leg is ordinary, un-scripted gameplay now,
        /// not something a test needs to simulate. MV-1123: the corridor walk itself is ALSO just
        /// ordinary gameplay from here on (Max keeps control throughout), so this now only has to prime
        /// the robot freeze and let <see cref="Tick"/> watch his real position from there.</summary>
        public void Initialize(WorldConfig fromCfg, MapData fromMap, WorldTransitionEntry entry, int fromWorldIndex,
            PlayerController player, System.Action onFinished = null, AreaGate exitGate = null)
        {
            _mode = Mode.Exit;
            _onFinished = onFinished;
            BuildExit(fromCfg, fromMap, entry, fromWorldIndex, player, exitGate);
            BeginCorridorWalk();
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
            ClearIntrudingGeometry(entry.CorridorLength, wallHeight);

            int toWorld = fromWorldIndex + 1;
            _farDoorMouth = PointAtXZ(_doorMouth, _wall, entry.CorridorLength);
            _farGate = BuildDoor(_farDoorMouth, _wall, wallHeight, wallThickness, null, transform, "World Join Far Gate");
            ApplyDestinationSkin(_farGate, toWorld);
            (_doorLampLRend, _doorLampRRend) = BuildJambLamps(transform, _farDoorMouth, _wall, wallHeight, "Far Door");
            SetDoorLamps(DoorLampRed);

            ResolveTitleCard(toWorld);

            BuildFade();
        }

        /// <summary>MV-1123 §7: the title card's two lines, resolved from the destination world's own
        /// <c>world</c> config field (e.g. "World 2 — Stormdrain" gives "WORLD 2" / "STORMDRAIN"). Reads
        /// the real, shipped <see cref="WorldConfig"/> through <see cref="WorldLibrary"/> rather than
        /// <see cref="WorldCatalog"/>, since the ticket's own source of truth is the config's own field,
        /// not the catalog row.</summary>
        private void ResolveTitleCard(int toWorld)
        {
            WorldConfig toCfg = WorldLibrary.Load(WorldLibrary.KeyForIndex(toWorld));
            (_titleWorldLine, _titleNameLine) = ParseTitle(toCfg?.world);
        }

        private static (string worldLine, string nameLine) ParseTitle(string worldField)
        {
            if (string.IsNullOrEmpty(worldField)) return (string.Empty, string.Empty);
            int dash = worldField.IndexOf('—');   // em dash, the config's own separator
            if (dash < 0) return (worldField.Trim().ToUpperInvariant(), string.Empty);

            string worldLine = worldField.Substring(0, dash).Trim().ToUpperInvariant();
            string nameLine = worldField.Substring(dash + 1).Trim().ToUpperInvariant();
            return (worldLine, nameLine);
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
            BiomePalette from = WorldCatalog.Get(_fromWorldIndex).Palette;
            BiomePalette to = WorldCatalog.Get(toWorld).Palette;
            string keyPrefix = $"join{_fromWorldIndex}";

            Color groundA = from.ColorFor(SurfaceKind.Ground);
            Color groundB = Color.Lerp(from.ColorFor(SurfaceKind.Ground), to.ColorFor(SurfaceKind.Ground), 0.5f);
            Color groundC = to.ColorFor(SurfaceKind.Ground);

            Material floorA = IntroBuild.Lit($"{keyPrefix}_A_floor", groundA);
            Material floorB = IntroBuild.Lit($"{keyPrefix}_B_floor", groundB);
            Material floorC = IntroBuild.Lit($"{keyPrefix}_C_floor", groundC);

            _segmentARoot = BuildSegment(root, "A", wallHeight, wallThickness, 0f, _entry.SegmentAEnd,
                floorA, IntroBuild.Lit($"{keyPrefix}_A_wall", from.ColorFor(SurfaceKind.Wall)));

            _segmentBRoot = BuildSegment(root, "B", wallHeight, wallThickness, _entry.SegmentAEnd, _entry.SegmentBEnd,
                floorB, IntroBuild.Lit($"{keyPrefix}_B_wall", Color.Lerp(from.ColorFor(SurfaceKind.Wall), to.ColorFor(SurfaceKind.Wall), 0.5f)));

            _segmentCRoot = BuildSegment(root, "C", wallHeight, wallThickness, _entry.SegmentBEnd, _entry.CorridorLength,
                floorC, IntroBuild.Lit($"{keyPrefix}_C_wall", to.ColorFor(SurfaceKind.Wall)));

            // MV-1123 §2: solid banks, top level with the wall tops, replacing the old floor-level
            // ground apron (MV-1076) — the camera no longer looks over the walls into a void OR a flat
            // slab filling the whole view (Lee device observation).
            BuildBanks(root, wallThickness, wallHeight,
                (0f, _entry.SegmentAEnd, groundA),
                (_entry.SegmentAEnd, _entry.SegmentBEnd, groundB),
                (_entry.SegmentBEnd, _entry.CorridorLength, groundC));

            // MV-1123 §3: guide-light floor strips the whole corridor length, in the accent of the
            // world ahead (the destination this corridor leads to).
            BuildGuideLights(root, _doorMouth, _wall, 0f, _entry.CorridorLength, GuideLightAccent(toWorld));

            // MV-965: garden/kerb+grate/culvert set-dressing on top of the three flat-coloured shells
            // above — a separate pass, same reason StormdrainDressing/BackyardDressing are separate from
            // MapRuntime.Build, only ever relevant to the World 1 -> World 2 row this ticket covers.
            if (_fromWorldIndex == 0)
                WorldJoinDressing.DressExit(_segmentARoot, _segmentBRoot, _segmentCRoot, _doorMouth, _wall, wallHeight, _entry);
            // MV-967: outfall/breach/hull set-dressing for the World 2 -> World 3 row.
            else if (_fromWorldIndex == 1)
                WorldJoinDressing.DressExitReef(_segmentARoot, _segmentBRoot, _segmentCRoot, _doorMouth, _wall, wallHeight, _entry);
        }

        /// <summary>MV-1123 §3: the accent colour guide lights point toward — the world AHEAD, i.e. the
        /// destination this corridor/arrival shell leads into. The two shipped rows' own named tones
        /// (ticket's own numbers); falls back to <see cref="StormdrainKit.Status"/> for any future row
        /// not yet given its own accent.</summary>
        private static Color GuideLightAccent(int toWorld) => toWorld switch
        {
            1 => StormdrainKit.Status,
            2 => WorldMaterials.ReefLampViolet,
            _ => StormdrainKit.Status,
        };

        // ------------------------------------------------------------------ build (arrival)

        /// <summary>Builds the arrival shell outside the destination stub's own wall, cuts the real gap
        /// into the stub, and fades the destination world in around Max (MV-964 §4.7). Suspends gameplay
        /// only for the fade-in itself -- there is nothing to see yet, the world has only just booted --
        /// then hands control straight back (MV-1123 §8: no scripted walk-in any more; Max is free to
        /// walk himself from the moment the fade lifts, and <see cref="TickPlayerControl"/> watches for
        /// him to walk far enough inside to close the arrival door behind him).</summary>
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

            CutWallGap(_doorMouth, _wall, wallHeight, WorldTransitionEntry.CorridorWidth, out Material wallMaterial);
            ClearOverheadDressingNearDoor(_doorMouth, _wall, wallHeight, WorldTransitionEntry.CorridorWidth);
            _doorGate = BuildDoor(_doorMouth, _wall, wallHeight, wallThickness, wallMaterial, transform, "World Arrival Door");
            ApplyDestinationSkin(_doorGate, toWorld);
            _doorGate.ForceOpen();
            (_doorLampLRend, _doorLampRRend) = BuildJambLamps(transform, _doorMouth, _wall, wallHeight, "Arrival Door");
            SetDoorLamps(DoorLampGreen);   // MV-1123 §8: the arrival door is already open on arrival

            BuildArrivalShell(wallHeight, wallThickness, toWorld);
            ClearIntrudingGeometry(entry.ArrivalShellLength, wallHeight);
            BuildFade();
            ApplyFadeAlpha(1f);   // fully black -- the world has just booted, nothing to see yet

            (_titleWorldLine, _titleNameLine) = ParseTitle(toCfg?.world);
            _titleCard = WorldJoinTitleCard.Create();
            _titleCard.Show(_titleWorldLine, _titleNameLine, 1f);

            float startAlong = entry.ArrivalShellLength - 1f;   // AC (MV-964 §4.7): "1 m from its far end"
            Vector3 start = PointAt(_doorMouth, _wall, startAlong, _playerT.position.y);
            TeleportPlayer(start);
            _playerT.rotation = Quaternion.LookRotation(-OutwardDir(_wall), Vector3.up);   // facing the stub

            _phase = Phase.FadeIn;
            _phaseElapsed = 0f;
        }

        private void BuildArrivalShell(float wallHeight, float wallThickness, int toWorld)
        {
            BiomePalette palette = WorldCatalog.Get(toWorld).Palette;
            Color ground = palette.ColorFor(SurfaceKind.Ground);
            Material floorMat = IntroBuild.Lit($"arrival{toWorld}_floor", ground);
            _arrivalRoot = BuildSegment(transform, "Arrival", wallHeight, wallThickness, 0f, _entry.ArrivalShellLength,
                floorMat, IntroBuild.Lit($"arrival{toWorld}_wall", palette.ColorFor(SurfaceKind.Wall)));

            // MV-1123 §2: same solid banks the exit corridor carries, replacing the old floor-level
            // ground apron (MV-1076) -- the shell's far end (where Max first appears) has nothing
            // beyond it either.
            BuildBanks(_arrivalRoot, wallThickness, wallHeight, (0f, _entry.ArrivalShellLength, ground));

            // MV-1123 §3: guide lights, same as the exit corridor -- the world ahead, here, is simply
            // the world Max has already arrived in.
            BuildGuideLights(_arrivalRoot, _doorMouth, _wall, 0f, _entry.ArrivalShellLength, GuideLightAccent(toWorld));

            // MV-965: same dressing pass as the exit side's segment C, continued into the arrival shell.
            if (toWorld == 1)
                WorldJoinDressing.DressArrival(_arrivalRoot, _doorMouth, _wall, wallHeight, _entry.ArrivalShellLength);
            // MV-967: same idea for the World 3 arrival shell — segment C's hull dressing continued.
            else if (toWorld == 2)
                WorldJoinDressing.DressArrivalReef(_arrivalRoot, _doorMouth, _wall, wallHeight, _entry.ArrivalShellLength);
        }

        // ------------------------------------------------------------------ intrusion clearance (MV-1116)

        /// <summary>MV-1116 (Lee, device: "a wall going through the middle of the corridor"): a dark
        /// slab cut straight across the World 1 -> World 2 exit corridor and ran on far past both its
        /// sides. Root cause -- <see cref="MaxWorlds.Arena.BackyardBackdrop"/> wraps a fence line around
        /// the WHOLE of World 1's map bounds at scene boot, long before this corridor exists (it's only
        /// built later, when the final boss dies, 30 m out past a30's own E wall); nothing ever cut that
        /// fence through for a corridor that didn't exist yet when it was built.
        ///
        /// General on purpose, not a special case for the backdrop: scans every enabled
        /// <see cref="Renderer"/>/<see cref="Collider"/> in the scene and disables (never destroys --
        /// some of what intrudes may be statically batched already, and disabling a renderer after
        /// <c>StaticBatchingUtility.Combine</c> is safe, destroying the GameObject isn't always) whatever
        /// isn't part of THIS sequence's own hierarchy (built under <c>transform</c>, see
        /// <see cref="BuildExitCorridor"/>/<see cref="BuildArrivalShell"/>) or Max's own body, and reaches
        /// more than <see cref="IntrusionEpsilon"/> into the walkable volume: <paramref name="length"/>
        /// outward from <see cref="_doorMouth"/> along <see cref="_wall"/>'s own axis, the corridor's 3 m
        /// interior width, floor to <paramref name="wallHeight"/>.</summary>
        private const float IntrusionEpsilon = 0.05f;

        private void ClearIntrudingGeometry(float length, float wallHeight)
        {
            Bounds volume = WalkableVolume(_doorMouth, _wall, length, WorldTransitionEntry.CorridorWidth, wallHeight);
            Bounds strict = volume;
            strict.Expand(-IntrusionEpsilon * 2f);

            foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || IsOwnOrPlayer(r.transform)) continue;
                if (strict.Intersects(r.bounds)) r.enabled = false;
            }

            foreach (Collider c in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (c == null || !c.enabled || IsOwnOrPlayer(c.transform)) continue;
                if (strict.Intersects(c.bounds)) c.enabled = false;
            }
        }

        private bool IsOwnOrPlayer(Transform t)
        {
            Transform dressingRoot = _doorGate != null ? _doorGate.DressingRoot : null;
            for (Transform p = t; p != null; p = p.parent)
            {
                if (p == transform || p == dressingRoot) return true;
                if (_playerT != null && p == _playerT) return true;
                // The exit side's door is real map geometry MapRuntime built (never a descendant of
                // this sequence's own transform, unlike the arrival side's, which BuildDoor parents
                // under transform directly) -- it IS the corridor's own mouth, not a foreign intruder.
                if (_doorGate != null && p == _doorGate.transform) return true;
            }
            return false;
        }

        /// <summary>Same outward-axis convention as <see cref="PointAtXZ"/>, expressed as a world-space
        /// AABB rather than a point -- every exit/arrival wall is N, E, S or W, so this is always
        /// axis-aligned, never a rotated OBB.</summary>
        private static Bounds WalkableVolume(Vector2 doorMouth, Wall wall, float length, float width, float wallHeight)
        {
            bool travelAlongX = wall == Wall.E || wall == Wall.W;
            bool positive = wall == Wall.N || wall == Wall.E;
            Rect footprint;
            if (travelAlongX)
            {
                float xMin = positive ? doorMouth.x : doorMouth.x - length;
                footprint = new Rect(xMin, doorMouth.y - width * 0.5f, length, width);
            }
            else
            {
                float yMin = positive ? doorMouth.y : doorMouth.y - length;
                footprint = new Rect(doorMouth.x - width * 0.5f, yMin, width, length);
            }

            return new Bounds(new Vector3(footprint.center.x, wallHeight * 0.5f, footprint.center.y),
                new Vector3(footprint.width, wallHeight, footprint.height));
        }

        // ------------------------------------------------------------------ shared geometry

        private static float ResolveWallHeight(MapData map) =>
            map != null && map.wallHeight > 0f ? map.wallHeight : MapData.DefaultWallHeight;

        private static float ResolveWallThickness(MapData map) =>
            map != null ? map.wallThickness : MapData.DefaultWallThickness;

        private static void ApplyDestinationSkin(AreaGate gate, int toWorld)
        {
            switch (WorldCatalog.Get(toWorld).GateSkin)
            {
                case WorldGateSkin.Stormdrain: gate.ApplyStormdrainGateSkin(); break;
                case WorldGateSkin.Reef: gate.ApplyReefSkin(); break;
            }
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

            // MV-1123: the far/arrival door matches the walkway's own width now (5 m), not the narrower
            // 3 m exit doorway cut into the FROM world's wall.
            float sealWidth = WorldTransitionEntry.CorridorWidth + wallThickness * 2f;
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

        /// <summary>MV-1123 §2: solid banks outside both walls, replacing the old floor-level ground
        /// apron (MV-1076) -- the camera used to look either over the low apron into a void (MV-1076's
        /// own fix) or, once that shipped, into a flat slab that read as filling the whole view (Lee
        /// device observation, this ticket). A bank's TOP sits level with the wall top instead, so the
        /// view is always bounded by solid ground, never void or an oversized slab.
        ///
        /// One bank per <paramref name="spans"/> entry's own along-range (its own ground colour
        /// times <see cref="BankTint"/>, so each bank reads as a darker continuation of the segment it
        /// runs beside), extended <see cref="BankEndExtension"/> m past BOTH the first span's near end and
        /// the last span's far end. No collider (Max/robots can never reach it) and never re-tinted by
        /// the world's own dressing sweep.</summary>
        private void BuildBanks(Transform root, float wallThickness, float wallHeight,
            params (float AlongMin, float AlongMax, Color GroundColor)[] spans)
        {
            if (spans.Length == 0) return;

            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            float sideCenter = halfWidth + wallThickness + BankSideWidth * 0.5f;
            float centerY = wallHeight * 0.5f;   // top-aligned: spans y [0, wallHeight], same as a wall box

            for (int i = 0; i < spans.Length; i++)
            {
                var span = spans[i];
                float alongMin = i == 0 ? span.AlongMin - BankEndExtension : span.AlongMin;
                float alongMax = i == spans.Length - 1 ? span.AlongMax + BankEndExtension : span.AlongMax;

                Material bankMat = IntroBuild.Lit($"bank_{GetInstanceID()}_{i}", span.GroundColor * BankTint);
                MarkApron(BuildBox(root, "Bank E", alongMin, alongMax, sideCenter, BankSideWidth, wallHeight, centerY, bankMat));
                MarkApron(BuildBox(root, "Bank W", alongMin, alongMax, -sideCenter, BankSideWidth, wallHeight, centerY, bankMat));
            }
        }

        /// <summary>Decoration only: strips the primitive cube's default collider (ticket AC: "no
        /// collider") and marks it <see cref="KeepsOwnMaterial"/> so neither world's own runtime dressing
        /// sweep repaints it.</summary>
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

        /// <summary>MV-1123 §3: unlit emissive floor strips every <see cref="GuideLightPitch"/> m along
        /// both sides of the walkway, <see cref="GuideLightInset"/> m in from each wall, in the accent
        /// colour of the world ahead.</summary>
        private static void BuildGuideLights(Transform root, Vector2 doorMouth, Wall wall, float alongMin, float alongMax, Color accent)
        {
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f;
            float acrossOffset = halfWidth - GuideLightInset;
            Material mat = StormdrainKit.Unlit(accent, "WorldJoinGuideLight");

            Vector3 dir = OutwardDir(wall);
            Vector3 across = AcrossDir(wall);

            for (float d = alongMin + GuideLightPitch * 0.5f; d < alongMax; d += GuideLightPitch)
            {
                float dMin = d - GuideLightLength * 0.5f;
                float dMax = d + GuideLightLength * 0.5f;
                foreach (float side in new[] { 1f, -1f })
                {
                    float mid = (dMin + dMax) * 0.5f;
                    float length = dMax - dMin;
                    Vector3 center = new Vector3(doorMouth.x, GuideLightY, doorMouth.y) + dir * mid + across * (side * acrossOffset);
                    Vector3 size = new Vector3(
                        Mathf.Abs(dir.x) * length + Mathf.Abs(across.x) * GuideLightWidth,
                        0.02f,
                        Mathf.Abs(dir.z) * length + Mathf.Abs(across.z) * GuideLightWidth);

                    GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "Guide Light";
                    go.transform.SetParent(root, worldPositionStays: true);
                    go.transform.position = center;
                    go.transform.localScale = size;
                    go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    MarkApron(go);
                }
            }
        }

        /// <summary>MV-1123 §5: a lamp on each jamb of a far/arrival door — red while closed, green
        /// while open (<see cref="SetDoorLamps"/>). Built directly here rather than through
        /// <see cref="AreaGate.ApplyStormdrainGateSkin"/>'s own single centred lamp: this ticket's colours
        /// and two-lamp layout apply to every corridor's far door regardless of which world's gate skin
        /// (if any) it also wears.</summary>
        private (Renderer l, Renderer r) BuildJambLamps(Transform parent, Vector2 doorMouth, Wall wall, float wallHeight, string label)
        {
            float halfWidth = WorldTransitionEntry.CorridorWidth * 0.5f + 0.15f;
            float y = wallHeight * 0.5f;
            Renderer l = BuildJambLamp(parent, $"{label} Lamp L", doorMouth, wall, -halfWidth, y);
            Renderer r = BuildJambLamp(parent, $"{label} Lamp R", doorMouth, wall, halfWidth, y);
            return (l, r);
        }

        private static Renderer BuildJambLamp(Transform parent, string name, Vector2 doorMouth, Wall wall, float acrossOffset, float y)
        {
            Vector3 pos = new Vector3(doorMouth.x, y, doorMouth.y) + AcrossDir(wall) * acrossOffset;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * JambLampDiameter;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            var rend = go.GetComponent<MeshRenderer>();
            rend.sharedMaterial = StormdrainKit.Unlit(DoorLampRed, name);
            return rend;
        }

        private void SetDoorLamps(Color color)
        {
            Material mat = StormdrainKit.Unlit(color, "WorldJoinDoorLamp");
            if (_doorLampLRend != null) _doorLampLRend.sharedMaterial = mat;
            if (_doorLampRRend != null) _doorLampRRend.sharedMaterial = mat;
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
                case Phase.CorridorWalk: TickCorridorWalk(dt); break;
                case Phase.FadeOut: TickFadeOut(dt); break;
                case Phase.FadeIn: TickFadeIn(dt); break;
                case Phase.PlayerControl: TickPlayerControl(dt); break;
                case Phase.Done: break;
            }
        }

        /// <summary>MV-964 §4.2: Max keeps full control (and robots keep behaving) until he's a metre
        /// past the door line -- this is the only phase that doesn't already have gameplay suspended.</summary>
        private void TickAwaitingCrossing()
        {
            if (_playerT == null) return;
            if (AlongDistance(_playerT.position, _doorMouth, _wall) >= 1f) BeginCorridorWalk();
        }

        /// <summary>MV-1123 §4: the moment Max crosses, any Weapon Core still on the ground is banked as
        /// if he'd walked over it and the door swings shut behind him -- but UNLIKE the old scripted
        /// walk, his own control, the HUD and robot behaviour are left exactly as they were (only the
        /// robots freeze, via MV-1081's own pause flag, since nothing hostile exists past this door).</summary>
        private void BeginCorridorWalk()
        {
            PickupDirector.EnsureInstalled().CollectGroundedWeaponCore();
            if (_doorGate != null) _doorGate.Reclose();
            TeleportPlayer(PointAt(_doorMouth, _wall, 1f, _playerT.position.y));

            _frozenRobots.Clear();
            _frozenRobots.AddRange(RobotEnemy.Active);
            foreach (RobotEnemy r in _frozenRobots)
                if (r != null) r.SetCutsceneFrozen(true);

            _phase = Phase.CorridorWalk;
            _phaseElapsed = 0f;
        }

        /// <summary>MV-1123 §4-6: Max walks the whole corridor himself. This just watches his real
        /// position: lighting/fog follow him (unchanged from MV-964 §4.4), the far door slides open once
        /// he's within <see cref="FarDoorOpenDistance"/> of it (§5), and the fade starts once he's walked
        /// <see cref="FarDoorCrossClearance"/> m past it (§6).</summary>
        private void TickCorridorWalk(float dt)
        {
            ApplyCorridorLighting();

            float alongFromExit = AlongDistance(_playerT.position, _doorMouth, _wall);
            float distanceToFarDoor = _entry.CorridorLength - alongFromExit;

            if (!_farDoorOpened && distanceToFarDoor <= FarDoorOpenDistance)
            {
                _farDoorOpened = true;
                if (_farGate != null) _farGate.ForceOpen();
                SetDoorLamps(DoorLampGreen);
            }

            if (alongFromExit >= _entry.CorridorLength + FarDoorCrossClearance)
            {
                _phase = Phase.FadeOut;
                _phaseElapsed = 0f;
            }
        }

        /// <summary>Fog and lighting follow Max down the corridor (MV-964 §4.4), reaching the destination
        /// world's look at <see cref="LightingBlendFraction"/> of the way along it.</summary>
        private void ApplyCorridorLighting()
        {
            if (_lighting == null) return;
            float along = AlongDistance(_playerT.position, _doorMouth, _wall);
            float t = Mathf.Clamp01(along / (LightingBlendFraction * _entry.CorridorLength));
            _lighting.Apply(BackyardLook.Lerp(WorldCatalog.Get(_fromWorldIndex).Look, WorldCatalog.Get(_fromWorldIndex + 1).Look, t));
        }

        private void TickFadeOut(float dt)
        {
            _phaseElapsed += dt;
            float alpha = FadeDuration > 0f ? Mathf.Clamp01(_phaseElapsed / FadeDuration) : 1f;
            ApplyFadeAlpha(alpha);
            if (alpha >= 1f)
            {
                // MV-1123 §7: the title card shows the instant the screen reads fully black.
                if (_titleCard == null) _titleCard = WorldJoinTitleCard.Create();
                _titleCard.Show(_titleWorldLine, _titleNameLine, 1f);

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
            if (alpha <= 0f)
            {
                // MV-1123 §8: Max is under his own control again the instant the destination world is
                // actually visible -- no scripted walk left to run.
                RestoreGameplay();
                _titleCardHoldRemaining = TitleCardHoldAfterFadeIn;
                _phase = Phase.PlayerControl;
                _phaseElapsed = 0f;
            }
        }

        /// <summary>MV-1123 §8: Max is free to walk the arrival shell himself from here. This just
        /// watches for the title card's own hold to expire and for him to actually walk far enough
        /// inside the destination area's own wall to close the arrival door and hand this sequence off
        /// for good -- never on a timer, since destroying the shell (<see cref="Finish"/>, Arrival mode)
        /// while he's still standing in it would drop him through the floor.</summary>
        private void TickPlayerControl(float dt)
        {
            if (_titleCardHoldRemaining >= 0f)
            {
                _titleCardHoldRemaining -= dt;
                if (_titleCardHoldRemaining <= 0f)
                {
                    _titleCardHoldRemaining = -1f;
                    if (_titleCard != null) _titleCard.Hide();
                }
            }

            float along = AlongDistance(_playerT.position, _doorMouth, _wall);
            if (along > -ArrivalInsideOffset) return;

            if (_doorGate != null && _doorGate.IsOpen) _doorGate.Reclose();
            Finish();
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
