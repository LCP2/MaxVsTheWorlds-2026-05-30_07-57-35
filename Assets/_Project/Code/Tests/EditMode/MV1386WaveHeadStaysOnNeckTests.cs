using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Feel;
using MaxWorlds.Player;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1386: at the idle wave's peak head pitch (-20 deg) the neck point must stay where it is at
    /// rest. Fails on the base commit: the Head pivot sits at feet level, so the pitch swings the skull
    /// on a ~1.5 m lever and the head detaches from the body.
    /// </summary>
    public sealed class MV1386WaveHeadStaysOnNeckTests
    {
        private GameObject _playerGo;
        private GameObject _rigGo;

        [TearDown]
        public void TearDown()
        {
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            WeaponSystemState.Reset();
        }

        private static void InvokeAwake(object t) =>
            t.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(t, null);

        [Test]
        public void WavePeakHeadPitch_KeepsNeckPointWithin1cmOfRest()
        {
            _playerGo = new GameObject("Player-MV1386", typeof(CharacterController));
            var player = _playerGo.AddComponent<PlayerController>();
            InvokeAwake(player);
            _rigGo = new GameObject("MaxRig-MV1386");
            var rig = _rigGo.AddComponent<MaxRig>();
            InvokeAwake(rig);

            var t = typeof(MaxRig);
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var head = (Transform)t.GetField("_head", flags).GetValue(rig);
            var tickHead = t.GetMethod("TickHeadLag", flags);
            var stepsField = t.GetField("IdleWaveSteps", BindingFlags.NonPublic | BindingFlags.Static);
            var waveField = t.GetField("_waveSeq", flags);

            var neckLocal = new Vector3(0f, 1.474f, 0f);
            tickHead.Invoke(rig, new object[] { 0.0001f });
            Vector3 rest = head.TransformPoint(neckLocal);

            var seq = new AnimSequence((AnimStep[])stepsField.GetValue(null));
            seq.Tick(0.6f);   // end of step 1: head pitch at its -20 deg peak
            waveField.SetValue(rig, seq);
            tickHead.Invoke(rig, new object[] { 0.0001f });
            Vector3 peak = head.TransformPoint(neckLocal);

            Assert.Less(Vector3.Distance(rest, peak), 0.01f,
                "neck point must not move when the wave pitches the head");
        }
    }
}
