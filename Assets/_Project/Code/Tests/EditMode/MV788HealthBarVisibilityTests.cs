using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-788: every unit wore a bright-green health pill permanently — on World 2's dim palette that
    /// read as scattered neon blobs lying on the floor ("what are the neon green shapes", Lee). A bar
    /// must now earn its visibility (damage, or being Max's current target) and fade back out after a
    /// hold, except Max's own bar and an AreaGate's, which stay always-on (both navigational). Full
    /// health is also no longer green — a desaturated cool neutral instead, so colour means HURT rather
    /// than "exists".
    ///
    /// One test covers every acceptance criterion here rather than one per criterion (testing policy
    /// MV-465 Rule 1) — all five are facets of the same visibility/colour rewrite, not independent
    /// regressions.
    ///
    /// Must fail on 9476197c3b7d548bdb020af134c485a84c84c0c4 (the commit before this ticket's fix): that
    /// commit's <c>WorldHealthBar</c> has no <c>VisibilityAlpha</c> property and no <c>_secondsSinceTrigger</c>
    /// field (visibility there was a bare <c>alwaysShow || health &lt; FullEnough</c> with no trigger/fade
    /// concept at all), and its <c>HealthBarColor.Ramp(1f)</c> is bright green — (0.36, 0.85, 0.32), a
    /// saturation of (0.85-0.32)/0.85 ≈ 0.62, over the 0.25 ceiling this test asserts under. Compiling
    /// this test against that commit fails outright (no such property/field), and the colour assertion
    /// would fail even if it did compile:
    ///   Expected: less than 0.25
    ///   But was:  0.623529
    /// </summary>
    public sealed class MV788HealthBarVisibilityTests
    {
        private GameObject _go;
        private GameObject _go2;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_go2 != null) Object.DestroyImmediate(_go2);
        }

        private sealed class FakeUnit : MonoBehaviour, IHealthReadout
        {
            public float Hp = 100f;
            public float MaxHp = 100f;
            public bool Alive = true;
            public float HealthNormalized => MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
            public float HealthCurrent => Hp;
            public string ReadoutName => "TEST UNIT";
            public bool IsAlive => Alive;
        }

        /// <summary>Refresh() is private and normally only called from LateUpdate — invoke it directly,
        /// same idiom WorldHealthBarTests/AreaGateTests already use.</summary>
        private static void Refresh(WorldHealthBar bar)
        {
            var m = typeof(WorldHealthBar).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(bar, null);
        }

        /// <summary>Advances the fade timer by a controlled amount without a real player loop —
        /// Time.unscaledDeltaTime is not reliably non-zero outside Play mode, so the field is driven
        /// directly, the same "reach into private state a test needs to control deterministically"
        /// idiom WorldHealthBarNameplateTests already uses for <c>_nameText</c>.</summary>
        private static void SetSecondsSinceTrigger(WorldHealthBar bar, float seconds)
        {
            var f = typeof(WorldHealthBar).GetField("_secondsSinceTrigger", BindingFlags.NonPublic | BindingFlags.Instance);
            f.SetValue(bar, seconds);
        }

        private static void InvokeAwake(AreaGate gate)
        {
            var m = typeof(AreaGate).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(gate, null);
        }

        private static Image FindImageOn(GameObject go, string name)
        {
            foreach (Image i in go.GetComponentsInChildren<Image>(true))
                if (i.name == name) return i;
            Assert.Fail($"no '{name}' image on {go.name}'s bar");
            return null;
        }

        [Test]
        public void HealthBarsEarnVisibilityInsteadOfAlwaysShowingGreen()
        {
            // --- AC1: a robot's bar is inactive until damaged, then fades back out unwatched ---
            _go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var robot = _go.AddComponent<FakeUnit>();
            var robotBar = WorldHealthBar.Attach(_go, robot, heightAboveCentre: 1.15f, worldWidth: 1.1f);

            Assert.That(robotBar.Showing, Is.False,
                "a freshly built, undamaged, untargeted robot's bar pivot must be inactive");

            robot.Hp = 99f;   // one point of damage
            Refresh(robotBar);
            Assert.That(robotBar.Showing, Is.True, "took one point of damage and still shows nothing");

            // The ticket's own numbers: a 3.0s hold, then a 0.4s fade — comfortably past both (5s) so
            // the assertion isn't chasing float precision right at the 3.4s boundary.
            SetSecondsSinceTrigger(robotBar, 5f);
            Refresh(robotBar);
            Assert.That(robotBar.VisibilityAlpha, Is.EqualTo(0f),
                "3.0s hold + 0.4s fade after the last damage, the bar's alpha must have reached 0");

            // --- AC2: Max's bar and an AreaGate's bar stay active on a freshly built, undamaged unit ---
            var maxGo = new GameObject("Max stand-in");
            try
            {
                var maxUnit = maxGo.AddComponent<FakeUnit>();
                var maxBar = WorldHealthBar.Attach(maxGo, maxUnit, heightAboveCentre: 1.35f, worldWidth: 2.1f,
                    alwaysShow: true, isPlayerBar: true, desaturateWhenHealthy: true);
                Assert.That(maxBar.Showing, Is.True,
                    "Max's own bar must stay active on a freshly built, undamaged unit (navigational)");
            }
            finally { Object.DestroyImmediate(maxGo); }

            _go2 = new GameObject("Gate stand-in");
            var gate = _go2.AddComponent<AreaGate>();
            InvokeAwake(gate);
            var gateBar = _go2.GetComponent<WorldHealthBar>();
            Refresh(gateBar);
            Assert.That(gateBar.Showing, Is.True,
                "an AreaGate's bar must stay active on a freshly built, undamaged gate (navigational)");

            // --- AC3: HealthBarColor.Ramp(1.0)'s saturation is below 0.25 (no longer bright green) ---
            Color full = HealthBarColor.Ramp(1.0f);
            float max = Mathf.Max(full.r, full.g, full.b);
            float min = Mathf.Min(full.r, full.g, full.b);
            float saturation = max > 0f ? (max - min) / max : 0f;
            Assert.That(saturation, Is.LessThan(0.25f),
                $"full health must read as a desaturated neutral, got {full} (saturation {saturation:0.000})");

            // --- AC4: Ramp(0.5)/(0.25)/(0.1) are unchanged field-for-field from the base commit ---
            Assert.That(HealthBarColor.Ramp(0.5f), Is.EqualTo(new Color(0.96f, 0.86f, 0.16f)),
                "the yellow band must not move — only the healthy band's colour changed (MV-788)");
            Assert.That(HealthBarColor.Ramp(0.25f), Is.EqualTo(new Color(0.96f, 0.55f, 0.14f)),
                "the orange band must not move — only the healthy band's colour changed (MV-788)");
            Assert.That(HealthBarColor.Ramp(0.1f), Is.EqualTo(new Color(0.93f, 0.22f, 0.18f)),
                "the red band must not move — only the healthy band's colour changed (MV-788)");

            // --- AC5: resolved bar width/height/anchor height are unchanged from the base commit ---
            RectTransform outline = FindImageOn(_go, "Outline").rectTransform;
            float worldWidth = outline.rect.width * outline.lossyScale.x;
            float worldHeight = outline.rect.height * outline.lossyScale.y;
            Assert.That(worldWidth, Is.EqualTo(1.1f).Within(0.05f),
                "the bar's resolved world width must be unchanged (MV-788 touches colour/visibility only)");
            Assert.That(worldWidth / worldHeight, Is.GreaterThan(4.5f),
                "the bar's flat width:height ratio must be unchanged (MV-788 touches colour/visibility only)");
            Assert.That(robotBar.HeightAboveCentre, Is.EqualTo(1.15f).Within(0.001f),
                "the bar's anchor height must resolve unchanged (MV-788 touches colour/visibility only)");
        }
    }
}
