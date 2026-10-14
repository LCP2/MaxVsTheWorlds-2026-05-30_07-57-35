using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;
using MaxWorlds.Enemies;
using MaxWorlds.Intro;
using MaxWorlds.Player;
using MaxWorlds.Rendering;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1171 (the one new test, testing policy Rule 1): the World 2 exit corridor, built through the
    /// real <see cref="WorldJoinSequence.Initialize"/> entry point (same idiom MV845/1123 already
    /// establish), with Max placed at the corridor's own midpoint.
    ///
    /// Originally failed on base commit 234dca4 with an Ocean-backdrop-overscale defect (quoted in this
    /// ticket's own history) -- MV-1178 withdraws that premise: World 2 now climbs into the City, not
    /// the Reef, so there is no Reef hull/observation-window dressing left in this corridor for an
    /// ocean backdrop to overscale. The AC1a/AC1c Max-visibility assertions below are unaffected by
    /// which world is on the other end of the door and stay in place; the AC1b ocean-layer-size
    /// assertions (this ticket's own original Rule 1 proof) are removed along with the geometry they
    /// measured.
    /// </summary>
    public sealed class MV1171CorridorVisibilityTests
    {
        private GameObject _camGo;
        private GameObject _playerGo;
        private GameObject _rigGo;
        private WorldJoinSequence _sequence;

        [SetUp]
        public void SetUp()
        {
            foreach (var stray in Object.FindObjectsByType<WorldJoinSequence>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);

            // This corridor's own exit-side build (WorldJoinSequence.BeginCorridorWalk) walks the
            // field-wide RobotEnemy.Active registry and freezes every entry in it -- by design, for the
            // real game's own robots mid-fight when the exit door opens. This fixture spawns none of its
            // own, but a full-suite run means some OTHER test's own leftover (never reset) registry
            // entries could otherwise be swept up here and left stuck frozen for whatever runs next --
            // the same cross-test hygiene MV1105EveryKindDamagesASentinelTests' own ResetWorldState
            // already practises for itself. Resetting here is what keeps this fixture from ever being
            // that leak, regardless of what ran before it.
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();

            _camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            _camGo.AddComponent<Camera>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_sequence != null) Object.DestroyImmediate(_sequence.gameObject);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            foreach (var stray in Object.FindObjectsByType<BackyardLighting>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            RenderSettings.fog = false;

            WorldJoinDressing.Clear();
            StormdrainKit.Clear();
            MaterialLibrary.Clear();

            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [Test]
        public void MidCorridorMaxIsOnFloorAndUnobstructed()
        {
            WorldConfig fromCfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(fromCfg, "world2_config failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(fromCfg, out MapData map, out string reason), reason);

            WorldTransitionEntry entry = WorldTransitions.For(WorldCatalog.IndexOf(WorldIds.Stormdrain));
            Assert.IsNotNull(entry, "World 2 must author a WorldTransitions entry into World 3.");
            Vector2 doorMouth = entry.ExitDoorMouth(fromCfg);

            // The rig is built and resolved BEFORE the corridor, so AC1c below proves the corridor
            // build never touches it, instead of comparing a fresh instance's authored default to
            // itself. ApplyDeviceDefault is the real resolution path (public since MV-464 for exactly
            // this reason) rather than reading the serialized field's own initial value.
            _rigGo = new GameObject("FixedAngleCameraRig");
            var rig = _rigGo.AddComponent<FixedAngleCameraRig>();
            rig.ApplyDeviceDefault();
            float pitchBeforeCorridor = rig.Pitch;
            float distanceBeforeCorridor = rig.Distance;

            _playerGo = new GameObject("Max");
            _playerGo.AddComponent<CharacterController>();
            PlayerController player = _playerGo.AddComponent<PlayerController>();

            _sequence = new GameObject("WorldJoinSequence").AddComponent<WorldJoinSequence>();
            _sequence.Initialize(fromCfg, map, entry, fromWorldIndex: WorldCatalog.IndexOf(WorldIds.Stormdrain), player, () => { });

            // World 2's own exit wall is E (WorldCatalog), so the corridor travels +X.
            float mid = entry.CorridorLength * 0.5f;
            Vector3 feet = new Vector3(doorMouth.x + mid, 0f, doorMouth.y);
            player.transform.position = feet;

            // ---- AC1a: Max's feet are within 0.1 m of the corridor floor's top surface. ----
            Transform segB = _sequence.CorridorSegmentRoot('B');
            Assert.IsNotNull(segB, "segment B must have been built — the corridor's own midpoint sits in it");
            Transform floorB = segB.Find("Segment B Floor");
            Assert.IsNotNull(floorB, "segment B must build its own floor");
            float floorTopY = floorB.GetComponent<Renderer>().bounds.max.y;
            Assert.AreEqual(floorTopY, feet.y, 0.1f,
                "Max's feet must sit within 0.1 m of the corridor floor's top surface");

            // ---- AC1c: the camera keeps the normal play pitch — nothing about a corridor walk may
            // carry a bespoke angle. Resolved values, not authored constants: the SAME rig's pitch,
            // read again after the corridor exists, must still match what it resolved to before the
            // corridor was ever built; and no second, corridor-only rig may have been spawned either. ----
            Assert.AreEqual(pitchBeforeCorridor, rig.Pitch, 0.01f,
                "the corridor build must not have touched the existing camera rig's pitch");
            Assert.AreEqual(1, Object.FindObjectsByType<FixedAngleCameraRig>(FindObjectsSortMode.None).Length,
                "the corridor build must not spawn a second, bespoke camera rig");

            // ---- AC1b: no collider or renderer other than the corridor's own walls lies between the
            // play camera and Max's head. Literal line-of-sight check: the real per-device camera
            // offset (ComputeOffset, same formula FixedAngleCameraRig.Apply drives the live Cinemachine
            // follow with) from Max's head, against every renderer's own resolved world bounds. ----
            Vector3 head = feet + Vector3.up * 1.83f;   // MaxRig.cs's own doc: "stands 1.83 m to the tips of his hair"
            Vector3 camPos = head + FixedAngleCameraRig.ComputeOffset(distanceBeforeCorridor, pitchBeforeCorridor);
            Vector3 toHead = head - camPos;
            float headDist = toHead.magnitude;
            var sightLine = new Ray(camPos, toHead / headDist);

            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled) continue;
                if (r.name.Contains("Wall 1") || r.name.Contains("Wall 2") || r.name.StartsWith("Bank ")) continue;
                if (!r.bounds.IntersectRay(sightLine, out float hitDist)) continue;
                if (hitDist > 0.01f && hitDist < headDist - 0.05f)
                    Assert.Fail($"'{r.name}' (bounds {r.bounds}) lies between the play camera and Max's head " +
                        $"(hit at {hitDist:F2} m, head at {headDist:F2} m)");
            }

            // MV-1178: World 2's exit corridor no longer carries Reef hull dressing (observation
            // windows, ocean backdrops) at all -- it climbs into the City instead. The ocean-backdrop
            // overscale assertions this ticket originally proved its own fix against are removed along
            // with that geometry; ClimbCorridorTests (MV-1178) is this corridor's own current dressing
            // test.
        }
    }
}
