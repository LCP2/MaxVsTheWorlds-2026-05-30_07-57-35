using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1077: Lee, v0.11.5 on his phone, arriving in World 2 through the corridor — Max was not
    /// visible at all, his name/health bar cut off along a straight vertical edge as if standing
    /// behind or under part of a brick-topped walled structure around the arrival point.
    ///
    /// Reproduces against the real World 2 config and the real gameplay camera's own resting pose
    /// (<see cref="FixedAngleCameraRig.RestingPose"/> — never a hand-picked eye point, same idiom
    /// <see cref="MV819PipeVisibilityTests"/> already uses), at the three positions along the arrival
    /// shell the original ticket named: its own start (where control also returns now, MV-1123 §8 — no
    /// scripted walk-in any more), its own midpoint, and 1.5 m inside the stub. MV-1123 removed the
    /// scripted walk that used to carry Max between these points on its own, so this test now drives his
    /// position directly at each one instead. At each position, nothing but Max's own body may sit
    /// between the camera and his head/chest — checked as a ray/AABB test against every OTHER enabled
    /// renderer the real World 2 boot + arrival shell builds, both device-class camera defaults (phone
    /// and desktop) since the live bug reported on phone.
    /// </summary>
    public sealed class MV1077ArrivalVisibilityTests
    {
        /// <summary>Within MaxBody's own chest lathe (tunic spans y 0.66-1.48 above the feet pivot,
        /// MaxBody.cs) — clear of the belt (0.86-0.945) and the collar (1.40-1.50), the plain chest.</summary>
        private const float ChestHeight = 1.1f;

        /// <summary>Within MaxBody's own head lathe (y 1.474-1.824 above the feet pivot, MaxBody.cs) —
        /// roughly eye height (the eyes themselves sit at 1.66).</summary>
        private const float HeadHeight = 1.7f;

        /// <summary>WorldJoinSequence.ArrivalInsideOffset (private) — duplicated here per this test
        /// suite's own idiom (see MV845WorldJoinSequenceTests' duplicated WalkEndClearance) rather than
        /// exposed from production code.</summary>
        private const float ArrivalInsideOffsetDup = 1.5f;

        private const float OcclusionEpsilon = 0.03f;

        private GameObject _hostGo;
        private GameObject _camGo;
        private GameObject _rigGo;
        private GameObject _playerGo;
        private WorldJoinSequence _sequence;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();
            _rigGo = new GameObject("MV1077 cam rig");
            _hostGo = new GameObject("MV1077 host");
        }

        [TearDown]
        public void TearDown()
        {
            FixedAngleCameraRig.SimulatePhoneClass = null;

            if (_sequence != null) Object.DestroyImmediate(_sequence.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_hostGo != null) Object.DestroyImmediate(_hostGo);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            RenderSettings.fog = false;

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();
        }

        /// <summary>Same wall-outward math <see cref="WorldJoinSequence"/> itself uses internally —
        /// see MV965CorridorDressingTests' own copy of this helper.</summary>
        private static float AlongDistance(Vector3 pos, Vector2 doorMouth, Wall wall) => wall switch
        {
            Wall.N => pos.z - doorMouth.y,
            Wall.S => doorMouth.y - pos.z,
            Wall.E => pos.x - doorMouth.x,
            Wall.W => doorMouth.x - pos.x,
            _ => 0f,
        };

        /// <summary>The inverse of <see cref="AlongDistance"/>: the world position <paramref name="along"/>
        /// metres outward from <paramref name="doorMouth"/>, keeping <paramref name="currentPos"/>'s own Y
        /// and across-axis coordinate (MV-1123: this test now drives the player's own position directly at
        /// each checkpoint instead of relying on a scripted walk to carry it there).</summary>
        private static Vector3 PositionAtAlong(Vector3 currentPos, Vector2 doorMouth, Wall wall, float along) => wall switch
        {
            Wall.N => new Vector3(doorMouth.x, currentPos.y, doorMouth.y + along),
            Wall.S => new Vector3(doorMouth.x, currentPos.y, doorMouth.y - along),
            Wall.E => new Vector3(doorMouth.x + along, currentPos.y, doorMouth.y),
            Wall.W => new Vector3(doorMouth.x - along, currentPos.y, doorMouth.y),
            _ => currentPos,
        };

        [Test]
        public void MaxStaysVisibleFromTheGameplayCamera_ThroughTheWholeWorld2ArrivalWalk()
        {
            WorldConfig toCfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(toCfg, "world2_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(toCfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(0);
            Assert.IsNotNull(entry, "World 1 must author a WorldTransitions entry into World 2.");
            Vector2 doorMouth = entry.ArrivalDoorMouth(toCfg);

            // ---- the real World 2 boot, exactly BackyardPath.Awake's own order: geometry, then the
            // Stormdrain-only dressing + gate re-skin pass. ----
            MapBuild build = MapRuntime.Build(map, _hostGo.transform);
            StormdrainDressing.Dress(_hostGo.transform, map, build.Cover);
            foreach (AreaGate gate in Object.FindObjectsByType<AreaGate>(FindObjectsSortMode.None))
                gate.ApplyStormdrainGateSkin();

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            bool finished = false;
            _sequence = new GameObject("WorldJoinSequence (Arrival)").AddComponent<WorldJoinSequence>();
            _sequence.InitializeArrival(toCfg, map, entry, fromWorldIndex: 0, player, () => finished = true);

            var failures = new List<string>();

            // Checked against whatever is ACTUALLY alive and enabled at the moment of each checkpoint —
            // never a snapshot taken earlier, since Finish() (checkpoint c) destroys the whole
            // WorldJoinSequence GameObject (the shell, its dressing, the arrival door) outright, and a
            // stale Renderer reference throws MissingReferenceException rather than meaning anything.
            void CheckVisibility(string label, Vector3 pos)
            {
                Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
                    .Where(r => r.enabled)
                    .ToArray();

                foreach (bool simulatePhone in new[] { true, false })
                {
                    FixedAngleCameraRig.SimulatePhoneClass = simulatePhone;
                    string device = simulatePhone ? "phone" : "desktop";
                    var rig = _rigGo.GetComponent<FixedAngleCameraRig>();
                    if (rig == null) rig = _rigGo.AddComponent<FixedAngleCameraRig>();
                    rig.ApplyDeviceDefault();
                    rig.RestingPose(pos, out Vector3 camPos, out _);

                    foreach (var (part, height) in new[] { ("head", HeadHeight), ("chest", ChestHeight) })
                    {
                        Vector3 sample = pos + Vector3.up * height;
                        Renderer blocker = FirstOccluder(camPos, sample, renderers);
                        if (blocker != null)
                        {
                            failures.Add($"[{device}] {label}: Max's {part} (at {sample}) is occluded from the " +
                                         $"gameplay camera (at {camPos}) by '{blocker.name}' (bounds {blocker.bounds})");
                        }
                    }
                }
            }

            // ---- checkpoint (a): the first frame the fade lifts — flush FadeIn's own 0.4 s duration in
            // one call. MV-1123: control (and with it, the HUD/robots) comes back the instant the fade
            // lifts, with no scripted walk-in any more — Max is free to walk himself from here, so this
            // test now drives his position at each checkpoint directly rather than relying on a scripted
            // walk to carry it there. ----
            _sequence.Tick(0.5f);
            CheckVisibility("first frame the fade lifts / control returns", player.transform.position);

            float startAlong = AlongDistance(player.transform.position, doorMouth, entry.ArrivalWall);
            float targetAlong = -ArrivalInsideOffsetDup;
            float halfAlong = (startAlong + targetAlong) * 0.5f;

            // ---- checkpoint (b): midway between the shell's own start and 1.5 m inside the stub. ----
            player.transform.position = PositionAtAlong(player.transform.position, doorMouth, entry.ArrivalWall, halfAlong);
            _sequence.Tick(0.02f);
            CheckVisibility("midway through the arrival shell", player.transform.position);

            // ---- checkpoint (c): 1.5 m inside the stub — MV-1123 §8's own close/hand-off threshold.
            // The arrival shell (and its dressing, and the arrival door) is gone immediately after this
            // tick — Finish() destroys the whole WorldJoinSequence GameObject the moment Max reaches it.
            // Only the persistent World 2 map geometry remains to occlude Max from here on. ----
            player.transform.position = PositionAtAlong(player.transform.position, doorMouth, entry.ArrivalWall, targetAlong);
            _sequence.Tick(0.02f);
            Assert.IsTrue(finished, "reaching 1.5 m inside the stub must finish the arrival sequence.");
            CheckVisibility("well inside the entry area", player.transform.position);

            Assert.IsEmpty(failures, "MV-1077 arrival visibility violations:\n" + string.Join("\n", failures));
        }

        /// <summary>Same ray/AABB slab test <see cref="MV819PipeVisibilityTests"/> already uses — never
        /// <see cref="Physics"/>, since nothing this shell builds carries a collider that matters here
        /// (and the structural walls that DO carry one are exactly what this test is checking).</summary>
        private static Renderer FirstOccluder(Vector3 cameraPos, Vector3 sample, Renderer[] renderers)
        {
            Vector3 delta = sample - cameraPos;
            float maxDist = delta.magnitude;
            if (maxDist < 0.001f) return null;
            var ray = new Ray(cameraPos, delta / maxDist);

            foreach (Renderer r in renderers)
            {
                if (r.bounds.IntersectRay(ray, out float dist) && dist < maxDist - OcclusionEpsilon)
                    return r;
            }
            return null;
        }
    }
}
