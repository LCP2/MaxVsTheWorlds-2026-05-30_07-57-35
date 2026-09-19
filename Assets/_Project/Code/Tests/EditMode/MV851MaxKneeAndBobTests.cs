using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-851 AC1 — the run cycle's new knee joint and new bob formula, both resolved off the live
    /// rig (testing policy Tier 2), never an authored constant. Must fail on 24aceb3 (the pre-MV-851
    /// body): that commit's <c>MaxRig</c> has no <c>_knees</c> field at all (CS1061), and even patched
    /// to compile, its old <c>Abs(Sin(phase)) * bob</c> formula never dips below the waist — its span
    /// at full speed is <c>bob</c> (0.035 m), well under the 0.055 m floor this test asserts.
    ///
    /// EditMode, same reflection idiom <see cref="MV730MaxArmsGunWaddleTests"/>/
    /// <see cref="MV804MaxLeanTests"/> already use for this rig: Awake is not called automatically
    /// outside Play Mode, so it is invoked directly, and the private <c>_stride</c> field is driven by
    /// hand with <c>dt = 0</c> so the sweep is independent of whatever <c>strideRate</c> happens to be
    /// tuned to.
    /// </summary>
    public sealed class MV851MaxKneeAndBobTests
    {
        private GameObject _playerGo;
        private GameObject _rigGo;
        private PlayerController _player;
        private MaxRig _rig;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player-MV851-Test", typeof(CharacterController));
            _player = _playerGo.AddComponent<PlayerController>();
            Invoke(_player, "Awake");

            _rigGo = new GameObject("MaxRig-MV851-Test");
            _rig = _rigGo.AddComponent<MaxRig>();
            Invoke(_rig, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
        }

        private static void Invoke(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static object GetField(object target, string name) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        private void SetMoveInput(float x, float y) =>
            typeof(PlayerController).GetProperty("MoveInput")
                .GetSetMethod(nonPublic: true)
                .Invoke(_player, new object[] { new Vector2(x, y) });

        /// <summary>AC1, both halves: at the phase where leg 0's <c>cos(phase) = 1</c>, its knee must
        /// resolve to 64° ± 1° (<c>max(0, cos) * 58 + 6</c> at full speed); and sweeping <c>_stride</c>
        /// through a full cycle at full speed, the torso's resolved bob height must span at least
        /// 0.055 m.</summary>
        [Test]
        public void KneeFlexesTo64DegreesAtFullExtension_AndBobSpansAtLeast55mmOverAStride()
        {
            SetMoveInput(0f, 1f);   // full stick, forward

            SetField(_rig, "_stride", 0f);   // leg 0's cos(phase) = 1 here
            Invoke(_rig, "TickRun", 0f);     // dt = 0: applies the pose at this phase without advancing it

            var knees = (Transform[])GetField(_rig, "_knees");
            float kneeFlexDegrees = knees[0].localEulerAngles.x;

            Assert.That(kneeFlexDegrees, Is.EqualTo(64f).Within(1f),
                $"leg 0's knee resolved to {kneeFlexDegrees:F1} degrees at _stride = 0 (cos(phase) = 1, " +
                "full speed) — expected 64 (max(0, cos(phase)) * 58 + 6).");

            var torso = (Transform)GetField(_rig, "_torso");
            float minY = float.MaxValue, maxY = float.MinValue;
            for (float phase = 0f; phase < Mathf.PI * 2f; phase += 0.02f)
            {
                SetField(_rig, "_stride", phase);
                Invoke(_rig, "TickRun", 0f);
                float y = torso.localPosition.y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }

            Assert.That(maxY - minY, Is.GreaterThanOrEqualTo(0.055f),
                $"the torso's resolved bob height spans {maxY - minY:F3} m over a full stride at full " +
                "speed, under the 0.055 m floor — the run cycle's bounce is too small to read.");
        }
    }
}
