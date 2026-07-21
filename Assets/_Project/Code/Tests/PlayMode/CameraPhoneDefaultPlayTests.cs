using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The per-device-class camera default (YT-106): phones sit at 23 m, desktop keeps the wider
    /// serialized framing. This is a default, not a hard override — the dev nudge and the tuning
    /// slider still move it — so it only decides where a fresh session starts on each device.
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
        public IEnumerator OnAPhoneItStartsAt23m()
        {
            yield return MakeRig(phone: true);
            Assert.That(_go.GetComponent<FixedAngleCameraRig>().Distance,
                        Is.EqualTo(23f).Within(0.001f),
                        "a phone should get Lee's tighter 23 m framing by default");
        }

        [UnityTest]
        public IEnumerator OnDesktopItKeepsTheSerializedWideFraming()
        {
            yield return MakeRig(phone: false);
            Assert.That(_go.GetComponent<FixedAngleCameraRig>().Distance,
                        Is.EqualTo(25.1f).Within(0.001f),
                        "desktop must be left as-is — the wide framing read fine on a monitor");
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
