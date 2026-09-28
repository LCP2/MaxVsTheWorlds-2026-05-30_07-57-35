using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-991: the Force Field's remaining % used to appear only inside the small bottom-left HUD
    /// button (32pt dark ink on the ring) — Lee's eyes are on Max during a fight, not the corner, so
    /// it went unnoticed on TestFlight. This pins the fix: a big, colour-coded readout directly above
    /// Max's own name label, live only while the bubble is actually up.
    /// </summary>
    public sealed class MV991ForceFieldReadoutTests
    {
        private GameObject _go;

        private sealed class FakeUnit : MonoBehaviour, IHealthReadout
        {
            public float HealthNormalized => 1f;
            public float HealthCurrent => 500f;
            public string ReadoutName => "MAX";
            public bool IsAlive => true;
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static void Refresh(WorldHealthBar bar)
        {
            var m = typeof(WorldHealthBar).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(bar, null);
        }

        private static Text ShieldTextOf(WorldHealthBar bar)
        {
            var f = typeof(WorldHealthBar).GetField("_shieldText", BindingFlags.NonPublic | BindingFlags.Instance);
            return (Text)f.GetValue(bar);
        }

        [Test]
        public void TheShieldReadoutShowsResolvedPercentAndColourByFraction()
        {
            bool active = true;
            float fraction = 0.64f;

            _go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var unit = _go.AddComponent<FakeUnit>();
            var bar = WorldHealthBar.Attach(_go, unit, heightAboveCentre: 1.35f, worldWidth: 2.1f,
                                            alwaysShow: true, isPlayerBar: true,
                                            shieldActive: () => active, shieldFraction: () => fraction);

            Refresh(bar);
            Text shieldText = ShieldTextOf(bar);

            Assert.IsTrue(shieldText.gameObject.activeSelf, "an active Force Field must show the readout");
            Assert.AreEqual("64%", shieldText.text, "0.64 must ceil to 64%, not truncate to 63%");
            Assert.AreEqual(36, shieldText.fontSize);
            Assert.AreEqual(new Color(0.55f, 0.9f, 1.0f), shieldText.color, "cyan at >= 0.50 remaining");

            fraction = 0.20f;
            Refresh(bar);
            Assert.AreEqual(new Color(0.95f, 0.25f, 0.2f), shieldText.color, "red below 0.25 remaining");

            active = false;
            Refresh(bar);
            Assert.IsFalse(shieldText.gameObject.activeSelf,
                "an inactive Force Field must leave no readout, not an empty gap");
        }
    }
}
