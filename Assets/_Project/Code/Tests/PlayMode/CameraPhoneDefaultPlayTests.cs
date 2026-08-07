using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The per-device-class camera default (YT-106, re-baked YT-200, retuned MV-276): phones sit at
    /// 16.1 / 1.1 m, desktop keeps the wider serialized framing (also / 1.1 post-MV-276). This is a
    /// default, not a hard override — the dev nudge and the tuning slider still move it — so it only
    /// decides where a fresh session starts on each device.
    /// </summary>
    public sealed class CameraPhoneDefaultPlayTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            FixedAngleCameraRig.SimulatePhoneClass = null;
            yield return null;
        }

        private IEnumerator MakeRig(bool phone)
        {
            FixedAngleCameraRig.SimulatePhoneClass = phone;
            _go = new GameObject("CameraRig");
            _go.AddComponent<FixedAngleCameraRig>();   // Awake picks the per-device default
            yield return null;
        }

        [UnityTest]
        public IEnumerator OnAPhoneItStartsAt16Point1m()
        {
            yield return MakeRig(phone: true);
            Assert.That(_go.GetComponent<FixedAngleCameraRig>().Distance,
                        Is.EqualTo(16.1f / FixedAngleCameraRig.ZoomFactor).Within(0.001f),
                        "a phone should get Lee's tighter framing by default, 10% closer post-MV-276");
        }

        [UnityTest]
        public IEnumerator OnDesktopItKeepsTheSerializedWideFraming()
        {
            yield return MakeRig(phone: false);
            Assert.That(_go.GetComponent<FixedAngleCameraRig>().Distance,
                        Is.EqualTo(25.1f / FixedAngleCameraRig.ZoomFactor).Within(0.001f),
                        "desktop keeps the same relative framing — just 10% closer post-MV-276");
        }

        [UnityTest]
        public IEnumerator ThePhoneDefaultIsWithinTheZoomBounds()
        {
            Assert.That(FixedAngleCameraRig.PhoneDistance,
                        Is.InRange(FixedAngleCameraRig.MinDistance, FixedAngleCameraRig.MaxDistance),
                        "the phone default must be a value the zoom clamp actually allows");
            yield return null;
        }
    }
}
