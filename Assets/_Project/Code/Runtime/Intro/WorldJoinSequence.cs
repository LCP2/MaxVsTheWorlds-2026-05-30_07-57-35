using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Intro
{
    /// <summary>
    /// MV-845: the World 1 -> World 2 joining sequence. Replaces <see cref="WorldTransitionCinematic"/>
    /// (MV-704's grate-collapse/shaft vignette) for this one transition — that class is left in place
    /// in case anything else ever wants it, but <see cref="MaxWorlds.UI.RunFlow.StartNextWorld"/> no
    /// longer calls it for W1 -> W2.
    ///
    /// Unlike <see cref="WorldTransitionCinematic"/> this is not an offscreen vignette built on its own
    /// fake set — it is real gameplay space: a door opens in a30's actual east wall, a corridor is
    /// built east of it, and Max is walked through both with his own <see cref="CharacterController"/>
    /// so he collides with whatever is actually there. World 1's look holds throughout; the gradual
    /// blend to World 2's presentation along the corridor is MV-849's, not this ticket's.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldJoinSequence : MonoBehaviour
    {
        private const string DoorAreaId = "a30";
        private const float DoorWidth = 3f;
        private const float CorridorLength = 30f;
        private const float CorridorHalfWidth = 1.5f;

        /// <summary>x offset from the door past which the screen starts fading — a30's east wall plus
        /// 27 m lands on x = 365 for World 1's own a30 (XMax 338), the ticket's own figure.</summary>
        private const float FadeStartOffset = 27f;

        private const float FadeDuration = 0.4f;

        /// <summary>"Max walks... to 1 m inside the door" (AC4).</summary>
        private const float DoorInsideOffset = 1f;

        /// <summary>"If no path to the door exists, place him 2 m inside the door first" (AC4) — the
        /// timeout fallback's landing point.</summary>
        private const float FallbackInsideOffset = 2f;

        /// <summary>How long a walk leg is given to make progress before it's treated as "no path
        /// exists" and Max is placed at the leg's fallback point instead (AC4's contingency — there is
        /// no authored route to a door that isn't part of the map graph, so this is what actually
        /// stands in for "pathing failed").</summary>
        private const float WalkTimeoutSeconds = 8f;

        private const float ArrivalEpsilon = 0.2f;

        /// <summary>Matches <see cref="PlayerController"/>'s own turn rate so the scripted walk turns
        /// exactly as fast as ordinary player-driven movement does.</summary>
        private const float RotationSpeedDegPerSec = 720f;

        private static System.Action s_pendingOnFinished;
        private static WorldConfig s_pendingCfg;
        private static MapData s_pendingMap;
        private static PlayerController s_pendingPlayer;

        /// <summary>Start the sequence once, only if a world map and Max are both actually resolvable
        /// (mirrors <see cref="WorldTransitionCinematic.TryPlay"/>'s same bail — no map/no player falls
        /// straight through to the caller's own fallback). Returns true if it started; on true, the
        /// sequence owns calling <paramref name="onFinished"/> exactly once, later.</summary>
        public static bool TryPlay(System.Action onFinished)
        {
            if (FindFirstObjectByType<WorldJoinSequence>() != null) return false;

            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null || path.Cfg == null || path.Map == null) return false;

            var player = FindFirstObjectByType<PlayerController>();
            if (player == null) return false;

            s_pendingOnFinished = onFinished;
            s_pendingCfg = path.Cfg;
            s_pendingMap = path.Map;
            s_pendingPlayer = player;
            new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            return true;
        }

        private void Awake()
        {
            System.Action onFinished = s_pendingOnFinished; s_pendingOnFinished = null;
            WorldConfig cfg = s_pendingCfg; s_pendingCfg = null;
            MapData map = s_pendingMap; s_pendingMap = null;
            PlayerController player = s_pendingPlayer; s_pendingPlayer = null;
            Initialize(cfg, map, player, onFinished);
        }

        // ------------------------------------------------------------------ state

        private enum Phase { WalkToDoor, WalkCorridor, Fade, Done }

        private System.Action _onFinished;
        private PlayerController _player;
        private CharacterController _cc;
        private Transform _playerT;

        private float _doorX;
        private float _doorZ;
        private float _fadeStartX;
        private float _corridorEndX;

        private Phase _phase;
        private float _phaseElapsed;
        private bool _restored;

        private HudController _hud;
        private readonly List<RobotEnemy> _frozenRobots = new List<RobotEnemy>(16);

        private Material _wallMaterialForCorridor;
        private Transform _fade;
        private MaterialPropertyBlock _fadeMpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>Running until it hands off. A test reads this to prove it reaches the end.</summary>
        public bool IsPlaying => _phase != Phase.Done;

        // ------------------------------------------------------------------ build

        /// <summary>The Awake work, exposed so an EditMode test can build the sequence directly — a
        /// MonoBehaviour without <c>[ExecuteAlways]</c> only ever receives <c>Awake</c> once Unity is
        /// actually in Play Mode, which this project's EditMode-only test suite never enters
        /// (MV-299/311/330).</summary>
        public void Initialize(WorldConfig cfg, MapData map, PlayerController player, System.Action onFinished)
        {
            _onFinished = onFinished;
            _player = player;
            _playerT = player.transform;
            _cc = player.GetComponent<CharacterController>();

            float wallHeight = map != null && map.wallHeight > 0f ? map.wallHeight : MapData.DefaultWallHeight;
            float wallThickness = map != null ? map.wallThickness : 0.4f;

            WorldArea door = cfg?.Area(DoorAreaId);
            if (door != null)
            {
                _doorX = door.XMax;
                _doorZ = door.WallSpan(Wall.E).Mid;
            }
            else
            {
                // Should not happen via TryPlay (it already checked), but a caller that hands Initialize
                // a door-less config directly gets a sequence that still runs its full phase machine
                // rather than silently doing nothing.
                _doorX = _playerT.position.x + DoorWidth;
                _doorZ = _playerT.position.z;
            }

            _fadeStartX = _doorX + FadeStartOffset;
            _corridorEndX = _doorX + CorridorLength;

            SuspendGameplay();
            OpenDoor(wallHeight, wallThickness);
            BuildCorridor(wallHeight, wallThickness);
            BuildFade();

            _phase = Phase.WalkToDoor;
            _phaseElapsed = 0f;
        }

        private void SuspendGameplay()
        {
            if (_player != null) _player.enabled = false;

            _hud = FindFirstObjectByType<HudController>();
            if (_hud != null) _hud.gameObject.SetActive(false);

            // Snapshot first -- disabling a RobotEnemy fires its own OnDisable, which removes it from
            // the very list RobotEnemy.Active is backed by, so iterating that list live while disabling
            // its members would mutate it mid-enumeration.
            _frozenRobots.Clear();
            _frozenRobots.AddRange(RobotEnemy.Active);
            foreach (RobotEnemy r in _frozenRobots)
                if (r != null) r.enabled = false;
        }

        private void RestoreGameplay()
        {
            if (_restored) return;
            _restored = true;
            if (_player != null) _player.enabled = true;
            if (_hud != null) _hud.gameObject.SetActive(true);
            foreach (RobotEnemy r in _frozenRobots) if (r != null) r.enabled = true;
            _frozenRobots.Clear();
        }

        /// <summary>Cuts a3 0's real east wall open and drops an <see cref="AreaGate"/> in the gap,
        /// forced open immediately -- reusing AreaGate's own hinge-swing visual rather than hand-rolling
        /// a second one. AreaGate's swing itself runs on its own fixed, shared 0.5 s timing (a private
        /// constant every other gate in the game also uses); this ticket's "opens over 1.0 s" is read as
        /// the beat's overall feel, not a reason to retune a constant every other gate in the game
        /// shares -- doing that would be exactly the kind of scope creep the tight-slice rule exists to
        /// stop.</summary>
        private void OpenDoor(float wallHeight, float wallThickness)
        {
            CutWallGap(wallHeight);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "World Join Door";
            body.transform.SetParent(transform, worldPositionStays: false);

            float sealWidth = DoorWidth + wallThickness * 2f;
            body.transform.localPosition = new Vector3(_doorX, wallHeight * 0.5f, _doorZ);
            // An E/W-wall doorway runs along Z -- spin 90 deg the same way MapRuntime.BuildAreaGate
            // does for one, so AreaGate.StartHingeSwing (which reads localScale.x as "the width") finds
            // its hinge pivot on the right edge instead of the thin one.
            body.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            body.transform.localScale = new Vector3(sealWidth, wallHeight, wallThickness + 0.04f);

            var rend = body.GetComponent<MeshRenderer>();
            rend.sharedMaterial = _wallMaterialForCorridor != null
                ? _wallMaterialForCorridor
                : IntroBuild.Lit("join_door", new Color(0.35f, 0.33f, 0.30f));

            var gate = body.AddComponent<AreaGate>();
            gate.AwayFromPlayerDirection = Vector3.right; // the corridor is +X of the door
            gate.ForceOpen();
        }

        /// <summary>Finds the live wall segment covering a30's real east edge and splits it around the
        /// door span, so the wall stays solid everywhere else (AC2) and Max's own CharacterController
        /// can actually walk through the gap instead of colliding with it. A no-op if no such wall is
        /// found (e.g. an EditMode test that never built real map geometry) -- the walk logic below
        /// doesn't depend on this having run.</summary>
        private void CutWallGap(float wallHeight)
        {
            var probe = new Vector3(_doorX, wallHeight * 0.5f, _doorZ);

            StructuralWall found = null;
            foreach (StructuralWall wall in FindObjectsByType<StructuralWall>(FindObjectsSortMode.None))
            {
                var col = wall.GetComponent<Collider>();
                if (col != null && col.bounds.Contains(probe)) { found = wall; break; }
            }
            if (found == null) return;

            GameObject wallGo = found.gameObject;
            var wallCollider = wallGo.GetComponent<BoxCollider>();
            var wallRenderer = wallGo.GetComponent<MeshRenderer>();
            if (wallCollider == null) return;

            _wallMaterialForCorridor = wallRenderer != null ? wallRenderer.sharedMaterial : null;

            Bounds b = wallCollider.bounds;
            float doorMin = _doorZ - DoorWidth * 0.5f;
            float doorMax = _doorZ + DoorWidth * 0.5f;

            ResizeWallZ(wallGo, b.min.z, doorMin); // shrink the found wall into the south remainder

            if (b.max.z - doorMax > 0.05f)
            {
                GameObject clone = Instantiate(wallGo, wallGo.transform.parent);
                clone.name = wallGo.name + " (MV-845 north of door)";
                ResizeWallZ(clone, doorMax, b.max.z);
            }
        }

        private static void ResizeWallZ(GameObject wallGo, float zMin, float zMax)
        {
            float length = zMax - zMin;
            if (length <= 0.02f) { wallGo.SetActive(false); return; }

            var collider = wallGo.GetComponent<BoxCollider>();
            Vector3 size = new Vector3(collider.size.x, collider.size.y, length);

            Vector3 pos = wallGo.transform.position;
            wallGo.transform.position = new Vector3(pos.x, pos.y, (zMin + zMax) * 0.5f);
            collider.size = size;

            var mf = wallGo.GetComponent<MeshFilter>();
            if (mf != null) mf.sharedMesh = CharacterMeshes.Bevelled(size, CharacterMeshes.DefaultBevel(size));
        }

        /// <summary>Floor + side walls + a closed far end, 3 m wide by 30 m long, east of the door
        /// (AC3). Built with real, un-stripped colliders (this is gameplay space Max is about to
        /// actually walk through, not an offscreen vignette set) and dressed in whatever material the
        /// real wall/floor already used, so it reads as World 1 throughout.</summary>
        private void BuildCorridor(float wallHeight, float wallThickness)
        {
            Transform root = new GameObject("World Join Corridor").transform;
            root.SetParent(transform, worldPositionStays: false);

            Material floorMat = FindMaterial("Map Floor") ??
                                 IntroBuild.Lit("join_corridor_floor", new Color(0.45f, 0.44f, 0.40f));
            Material wallMat = _wallMaterialForCorridor ??
                                IntroBuild.Lit("join_corridor_wall", new Color(0.35f, 0.33f, 0.30f));

            float midX = (_doorX + _corridorEndX) * 0.5f;
            float length = _corridorEndX - _doorX;
            float fullWidth = CorridorHalfWidth * 2f;

            SolidBox(root, "Corridor Floor",
                new Vector3(midX, -0.05f, _doorZ), new Vector3(length, 0.1f, fullWidth), floorMat);
            SolidBox(root, "Corridor Wall S",
                new Vector3(midX, wallHeight * 0.5f, _doorZ - CorridorHalfWidth - wallThickness * 0.5f),
                new Vector3(length, wallHeight, wallThickness), wallMat);
            SolidBox(root, "Corridor Wall N",
                new Vector3(midX, wallHeight * 0.5f, _doorZ + CorridorHalfWidth + wallThickness * 0.5f),
                new Vector3(length, wallHeight, wallThickness), wallMat);
            SolidBox(root, "Corridor End Cap",
                new Vector3(_corridorEndX + wallThickness * 0.5f, wallHeight * 0.5f, _doorZ),
                new Vector3(wallThickness, wallHeight, fullWidth + wallThickness * 2f), wallMat);
        }

        private static GameObject SolidBox(Transform parent, string name, Vector3 worldCenter, Vector3 size, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.position = worldCenter;
            go.transform.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            if (mat != null) r.sharedMaterial = mat;
            return go;
        }

        private static Material FindMaterial(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            var r = go != null ? go.GetComponent<MeshRenderer>() : null;
            return r != null ? r.sharedMaterial : null;
        }

        /// <summary>A screen-space fade quad parented to the live gameplay camera -- same idiom as
        /// <see cref="WorldTransitionCinematic"/>'s own fade, just simpler (one direction, one alpha
        /// target). A no-op if there's no <see cref="Camera.main"/> to hang it off (an EditMode test
        /// with no camera set up) -- the phase timer still runs the fade's duration regardless.</summary>
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

        // ------------------------------------------------------------------ running it

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advance the sequence by <paramref name="dt"/> seconds. Public so a test can drive it
        /// without a live Input System/frame loop.</summary>
        public void Tick(float dt)
        {
            if (_phase == Phase.Done) return;
            if (SkipRequested()) Skip();

            switch (_phase)
            {
                case Phase.WalkToDoor: TickWalkToDoor(dt); break;
                case Phase.WalkCorridor: TickWalkCorridor(dt); break;
                case Phase.Fade: TickFade(dt); break;
            }
        }

        private static bool SkipRequested()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
            var ptr = Pointer.current;
            if (ptr != null && ptr.press.wasPressedThisFrame) return true;
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch.press.wasPressedThisFrame) return true;
            return false;
        }

        /// <summary>Any tap/key skips straight to the fade (AC6) -- teleports Max to the fade's start
        /// point so the cut is covered by black rather than a visible jump. A no-op once already fading
        /// or done.</summary>
        public void Skip()
        {
            if (_phase == Phase.Fade || _phase == Phase.Done) return;
            TeleportPlayer(new Vector3(_fadeStartX, _playerT.position.y, _doorZ));
            EnterFade();
        }

        private void TickWalkToDoor(float dt)
        {
            _phaseElapsed += dt;
            var target = new Vector3(_doorX + DoorInsideOffset, _playerT.position.y, _doorZ);

            if (_phaseElapsed >= WalkTimeoutSeconds)
            {
                TeleportPlayer(new Vector3(_doorX + FallbackInsideOffset, _playerT.position.y, _doorZ));
                EnterCorridor();
                return;
            }

            if (StepToward(target, dt)) EnterCorridor();
        }

        private void TickWalkCorridor(float dt)
        {
            _phaseElapsed += dt;
            var target = new Vector3(_fadeStartX, _playerT.position.y, _doorZ);

            if (_phaseElapsed >= WalkTimeoutSeconds)
            {
                TeleportPlayer(target);
                EnterFade();
                return;
            }

            if (StepToward(target, dt)) EnterFade();
        }

        private void TickFade(float dt)
        {
            _phaseElapsed += dt;
            float alpha = FadeDuration > 0f ? Mathf.Clamp01(_phaseElapsed / FadeDuration) : 1f;
            ApplyFadeAlpha(alpha);
            if (alpha >= 1f) Finish();
        }

        private void EnterCorridor() { _phase = Phase.WalkCorridor; _phaseElapsed = 0f; }
        private void EnterFade() { _phase = Phase.Fade; _phaseElapsed = 0f; }

        /// <summary>Steps Max toward <paramref name="target"/> at his normal walk speed via his own
        /// <see cref="CharacterController"/> (so he collides with whatever's actually there, the same
        /// as ordinary player movement) and turns him to face the travel direction -- Max has no
        /// animator to trigger a walk clip on (he's a greybox capsule stand-in, same as everywhere else
        /// in this project), so his "walk animation" here is this same locomotion every other scripted
        /// or player-driven movement in the game already uses. Returns true once he's within
        /// <see cref="ArrivalEpsilon"/> of the target, measured AFTER the move (not from the requested
        /// step), so a collision-shortened step is never mistaken for arrival.</summary>
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
            _playerT.position = worldPos;
            if (_cc != null) _cc.enabled = was;
        }

        // ------------------------------------------------------------------ handoff

        /// <summary>Give the screen back exactly as it was borrowed, hand control to
        /// <see cref="_onFinished"/>, and remove the sequence entirely.</summary>
        private void Finish()
        {
            if (_phase == Phase.Done) return;
            _phase = Phase.Done;
            RestoreGameplay();
            _onFinished?.Invoke();
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
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
