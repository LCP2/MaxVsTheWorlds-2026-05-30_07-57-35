using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.CameraRig;
using MaxWorlds.Player;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1161 (Lee, 10 Oct: "it looks like a lot of his face is covered... see if you can adjust the
    /// design so that more of his face is available"). AC1: with Max built at rest, facing the camera's
    /// down-screen direction, and the play camera placed exactly as <c>FixedAngleCameraRig.RestingPose</c>
    /// places it, a line from the camera to each eye white's centre must not pass through the
    /// world-space bounds of any hair or brow renderer.
    ///
    /// Fails on the commit before this ticket (main 7db54db): the fringe locks hang straight down
    /// across the brow (<c>MaxHair.FringeHug0</c> = (0,-1,0.25)) and the hair cap overhangs it
    /// (profile point (0.214, 1.778) sits just above the brow at y=1.73), with no chin-up pitch to lift
    /// either clear of the camera's line to the eyes — expected to block at least one eye.
    /// </summary>
    public sealed class MV1161MaxFaceVisibilityTests
    {
        private GameObject _playerGo;
        private GameObject _rigGo;
        private GameObject _camGo;
        private PlayerController _player;
        private MaxRig _rig;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player-MV1161-Test", typeof(CharacterController));
            var cc = _playerGo.GetComponent<CharacterController>();
            _playerGo.transform.position = new Vector3(0f, cc.height * 0.5f - cc.center.y, 0f);
            _player = _playerGo.AddComponent<PlayerController>();
            InvokeAwake(_player);

            _rigGo = new GameObject("MaxRig-MV1161-Test");
            _rig = _rigGo.AddComponent<MaxRig>();
            InvokeAwake(_rig);
        }

        [TearDown]
        public void TearDown()
        {
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            WeaponSystemState.Reset();
        }

        private static void InvokeAwake(object target) =>
            target.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static object Invoke(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        /// <summary>Does the segment from <paramref name="from"/> to <paramref name="to"/> enter
        /// <paramref name="bounds"/> strictly before reaching <paramref name="to"/>? A hit exactly at (or
        /// past) the target is the eye's own bounds, not something blocking the view of it.</summary>
        private static bool SegmentEntersBounds(Vector3 from, Vector3 to, Bounds bounds)
        {
            Vector3 delta = to - from;
            float len = delta.magnitude;
            if (len < 1e-6f) return false;

            var ray = new Ray(from, delta / len);
            return bounds.IntersectRay(ray, out float dist) && dist < len - 1e-3f;
        }

        [Test]
        public void EyesAreClearOfHairAndBrowFromThePlayCamera()
        {
            // Max faces the camera's down-screen direction (local +Z) — same convention
            // MV1133MaxOutfitV3Tests' own face-visibility check uses.
            _playerGo.transform.eulerAngles = Vector3.zero;
            Invoke(_rig, "Follow");

            // Let the hair settle into its idle resting pose (zero wind/stride transients already
            // averaged out) before judging it — a fresh, un-simulated frame is not what the camera
            // actually sees.
            for (int i = 0; i < 120; i++) Invoke(_rig, "TickHair", 1f / 60f);

            // The play camera, placed exactly the way the game places it — never a hand-picked angle.
            _camGo = new GameObject("MV1161 Camera Probe");
            var camRig = _camGo.AddComponent<FixedAngleCameraRig>();
            camRig.RestingPose(_playerGo.transform.position, out Vector3 camPos, out _);

            var head = GetField<Transform>(_rig, "_head");
            var eyeMat = GetField<Material>(_rig, "_eyeMat");
            var hairMat = GetField<Material>(_rig, "_hairMat");
            var hairRibbonMat = GetField<Material>(_rig, "_hairRibbonMat");

            var allRenderers = head.GetComponentsInChildren<MeshRenderer>(true);

            int eyesChecked = 0;
            foreach (var eyeRenderer in allRenderers)
            {
                if (eyeRenderer.sharedMaterial != eyeMat) continue;
                eyesChecked++;

                Vector3 eyeCentre = eyeRenderer.transform.position;
                foreach (var occluder in allRenderers)
                {
                    bool isHairOrBrow = occluder.sharedMaterial == hairMat || occluder.sharedMaterial == hairRibbonMat;
                    if (!isHairOrBrow) continue;

                    Assert.IsFalse(SegmentEntersBounds(camPos, eyeCentre, occluder.bounds),
                        $"the camera's line to {eyeRenderer.name} at {eyeCentre} is blocked by " +
                        $"'{occluder.name}' (bounds {occluder.bounds}) before it reaches the eye.");
                }
            }

            Assert.That(eyesChecked, Is.EqualTo(2), $"expected 2 eye renderers under Head, found {eyesChecked}.");
        }
    }
}
