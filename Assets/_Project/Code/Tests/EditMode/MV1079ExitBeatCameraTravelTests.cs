using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.CameraRig;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1079 (the one new test, per CC_AUTONOMY's testing policy): Beat B (EXIT OPEN)'s camera travel,
    /// driven with an explicit dt so the assertions land exactly on the ticket's own authored instants —
    /// resolved <see cref="CameraTargetRig"/> positions, not an authored constant. Fails on base commit
    /// c1cf628 (MV-1075, the tip before this ticket): neither <c>WorldFinaleGate.BeginExitBeat</c>/
    /// <c>TickExitBeat</c> nor <c>CameraTargetRig.ApplyFocusOverride</c> exist on that commit at all — the
    /// exit beat this ticket adds is new API surface, so this test cannot even compile against it (the
    /// compiler's own missing-member errors are the fail-first proof, quoted in the fix comment).
    /// </summary>
    public sealed class MV1079ExitBeatCameraTravelTests
    {
        private GameObject _maxGo;
        private GameObject _rigGo;
        private GameObject _gateGo;

        [SetUp]
        public void SetUp()
        {
            _maxGo = new GameObject("MV-1079 Max Probe");
            _maxGo.tag = "Player";
            _maxGo.transform.position = Vector3.zero;

            _rigGo = new GameObject("MV-1079 CameraTargetRig Probe");
            var rig = _rigGo.AddComponent<CameraTargetRig>();
            rig.SetSubject(_maxGo.transform);

            _gateGo = new GameObject("MV-1079 WorldFinaleGate Probe");
        }

        [TearDown]
        public void TearDown()
        {
            // MV-1079: OnDisable isn't reliably invoked for AddComponent outside Play mode (same note
            // MV1078FinaleWeaponAndCleanupTests carries for OnEnable/Update), so the beat's own scratch
            // VFX/UI never get the chance to self-clean here -- sweep them by hand, same idiom MV1078's
            // own TearDown uses for a leaked ResultScreen.
            foreach (var ring in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(ring.gameObject);
            foreach (var bolt in Object.FindObjectsByType<SentinelBolt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(bolt.gameObject);
            foreach (var banner in Object.FindObjectsByType<FinaleBanner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(banner.gameObject);

            if (_gateGo != null) Object.DestroyImmediate(_gateGo);
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
        }

        [Test]
        public void ExitBeat_CameraTravelsToTheDoorThenBackToMax_OnTheTicketsOwnInstants()
        {
            var gate = _gateGo.AddComponent<WorldFinaleGate>();
            var rig = _rigGo.GetComponent<CameraTargetRig>();
            var doorPosition = new Vector3(20f, 0f, 0f); // AC1: Max standing 20 m from the exit door

            gate.BeginExitBeat(doorPosition);

            gate.TickExitBeat(1.0f); // t = 1.0 s
            Assert.IsTrue(gate.IsOpen, "MV-1079: WorldFinaleGate.Open() moves to t=1.0s into the exit beat");
            float doorDistXZ = Vector2.Distance(
                new Vector2(rig.transform.position.x, rig.transform.position.z),
                new Vector2(doorPosition.x, doorPosition.z));
            Assert.LessOrEqual(doorDistXZ, 1.0f,
                $"MV-1079: at t=1.0s the camera must be within 1.0m (XZ) of the door mouth, was {doorDistXZ}m");

            gate.TickExitBeat(2.0f); // t = 3.0 s total
            float maxDistXZ = Vector2.Distance(
                new Vector2(rig.transform.position.x, rig.transform.position.z),
                new Vector2(_maxGo.transform.position.x, _maxGo.transform.position.z));
            Assert.LessOrEqual(maxDistXZ, 0.5f,
                $"MV-1079: at t=3.0s the camera must have eased back within 0.5m of Max, was {maxDistXZ}m");
        }
    }
}
