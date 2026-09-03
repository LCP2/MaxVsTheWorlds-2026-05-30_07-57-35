using NUnit.Framework;
using UnityEngine;
using MaxWorlds.CameraRig;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The per-device-class camera default (YT-106, re-baked YT-200, retuned MV-276): phones sit at
    /// 16.1 / 1.1 m. Desktop kept the wider serialized framing (also / 1.1 post-MV-276) until MV-468
    /// re-baked it to 24.284037 m — the distance that preserves the pre-MV-468 72°/24.643636 m visible
    /// play area at the shipped 60° pitch. MV-658 then baked Lee's 2026-09-02 tuning pass, 26.02 m.
    /// This is a default, not a hard override — the dev nudge and the tuning slider still move it —
    /// so it only decides where a fresh session starts on each device.
    ///
    /// MV-464: moved from PlayMode. <see cref="FixedAngleCameraRig.ApplyDeviceDefault"/> is called
    /// directly instead of relying on Awake, which never runs from AddComponent outside Play mode.
    /// </summary>
    public sealed class CameraPhoneDefaultTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            FixedAngleCameraRig.SimulatePhoneClass = null;
        }

        private FixedAngleCameraRig MakeRig(bool phone)
        {
            FixedAngleCameraRig.SimulatePhoneClass = phone;
            _go = new GameObject("CameraRig");
            var rig = _go.AddComponent<FixedAngleCameraRig>();
            rig.ApplyDeviceDefault();
            return rig;
        }

        [Test]
        public void OnAPhoneItStartsAt16Point1m()
        {
            var rig = MakeRig(phone: true);
            Assert.That(rig.Distance,
                        Is.EqualTo(16.1f / FixedAngleCameraRig.ZoomFactor).Within(0.001f),
                        "a phone should get Lee's tighter framing by default, 10% closer post-MV-276");
        }

        [Test]
        public void OnDesktopItKeepsTheSerializedWideFraming()
        {
            var rig = MakeRig(phone: false);
            Assert.That(rig.Distance,
                        Is.EqualTo(26.02f).Within(0.001f),
                        "desktop keeps the committed framing — MV-658 baked Lee's 2026-09-02 tuning pass to 26.02 m (was 24.284037)");
        }

        [Test]
        public void ThePhoneDefaultIsWithinTheZoomBounds()
        {
            Assert.That(FixedAngleCameraRig.PhoneDistance,
                        Is.InRange(FixedAngleCameraRig.MinDistance, FixedAngleCameraRig.MaxDistance),
                        "the phone default must be a value the zoom clamp actually allows");
        }
    }
}
