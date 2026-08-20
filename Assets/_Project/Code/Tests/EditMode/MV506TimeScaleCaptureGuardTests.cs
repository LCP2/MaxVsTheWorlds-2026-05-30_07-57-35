using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Pickups;
using MaxWorlds.UI;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-506 AC2/AC3. The bug: on a cold boot the engine itself can start with
    /// <see cref="Time.timeScale"/> already at 0 (traced to a stray <c>ProjectSettings/TimeManager.asset</c>
    /// <c>m_TimeScale: 0</c>, confirmed by a temporary probe at
    /// <c>RuntimeInitializeLoadType.SubsystemRegistration</c> — the earliest callback Unity offers,
    /// strictly before any script including this one could have written it — logging
    /// <c>Time.timeScale=0</c> before the fix and <c>Time.timeScale=1</c> after; removed once confirmed,
    /// per the ticket's own step 5). Whatever the source, every pause-on-open screen captured that 0
    /// verbatim as "the speed to restore" and handed it straight back on <c>Close()</c>, latching the
    /// freeze forever. <see cref="MaxWorlds.Core.TimeScaleCapture.ClampForCapture"/> is the fix, applied
    /// at all seven capture sites across the four screens; this proves it end to end (resolved
    /// <see cref="Time.timeScale"/> after a real Open()/Close(), not the source clamp line) rather than
    /// unit-testing the pure function alone. One test, both halves of the guard: a non-positive capture
    /// clamps to 1, and a legitimate slow-mo (0.3) still round-trips — so this is a zero-clamp, not a
    /// blanket <c>= 1f</c>.
    /// </summary>
    public sealed class MV506TimeScaleCaptureGuardTests
    {
        private static void InvokePrivate(Object target, string methodName, params object[] args) =>
            target.GetType()
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static float OpenCloseHomeScreen(float startScale)
        {
            Time.timeScale = startScale;
            var go = new GameObject("HomeScreen Probe");
            var screen = go.AddComponent<HomeScreen>();
            try
            {
                InvokePrivate(screen, "Open");
                // HomeScreen.Close() unconditionally Destroy()s its canvas root, which only ever runs
                // in a real Play session — under the edit-mode test runner that call logs an error
                // that isn't the thing under test here, so it's expected rather than left to fail the
                // test on an unrelated log message (same idiom MV504's test suite already uses).
                LogAssert.Expect(LogType.Error, new Regex("Destroy may not be called from edit mode"));
                InvokePrivate(screen, "Close");
                return Time.timeScale;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static float OpenCloseSettingsPanel(float startScale)
        {
            Time.timeScale = startScale;
            var go = new GameObject("SettingsPanel Probe");
            var panel = go.AddComponent<SettingsPanel>();
            try
            {
                InvokePrivate(panel, "Build");          // Start() would normally do this
                InvokePrivate(panel, "SetOpen", true);
                InvokePrivate(panel, "SetOpen", false);
                return Time.timeScale;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static float OpenCloseWeaponsScreen(float startScale)
        {
            Time.timeScale = startScale;
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            var go = new GameObject("WeaponsScreen Probe");
            var screen = go.AddComponent<WeaponsScreen>();
            try
            {
                screen.Open();
                screen.Close();
                return Time.timeScale;
            }
            finally
            {
                Object.DestroyImmediate(go);
                WeaponSystemState.Reset();
                PickupWallet.Reset();
            }
        }

        private static float OpenCloseUpgradeScreen(float startScale)
        {
            Time.timeScale = startScale;
            var go = new GameObject("UpgradeScreen Probe");
            var screen = go.AddComponent<UpgradeScreen>();
            try
            {
                screen.Open(UpgradePart.Generic);
                screen.Continue();
                return Time.timeScale;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [TearDown]
        public void TearDown() => Time.timeScale = 1f;

        [Test]
        public void ClosingClampsANonPositiveCaptureToOne_AndRoundTripsALegitimateSlowMo()
        {
            Assert.That(OpenCloseHomeScreen(0f), Is.EqualTo(1f),
                "HomeScreen: closing over a captured 0 must not hand the freeze back.");
            Assert.That(OpenCloseHomeScreen(0.3f), Is.EqualTo(0.3f).Within(0.0001f),
                "HomeScreen: a legitimate prior speed must still round-trip, not get forced to 1.");

            Assert.That(OpenCloseSettingsPanel(0f), Is.EqualTo(1f),
                "SettingsPanel: closing over a captured 0 must not hand the freeze back.");
            Assert.That(OpenCloseSettingsPanel(0.3f), Is.EqualTo(0.3f).Within(0.0001f),
                "SettingsPanel: a legitimate prior speed must still round-trip, not get forced to 1.");

            Assert.That(OpenCloseWeaponsScreen(0f), Is.EqualTo(1f),
                "WeaponsScreen: closing over a captured 0 must not hand the freeze back.");
            Assert.That(OpenCloseWeaponsScreen(0.3f), Is.EqualTo(0.3f).Within(0.0001f),
                "WeaponsScreen: a legitimate prior speed must still round-trip, not get forced to 1.");

            Assert.That(OpenCloseUpgradeScreen(0f), Is.EqualTo(1f),
                "UpgradeScreen: closing over a captured 0 must not hand the freeze back.");
            Assert.That(OpenCloseUpgradeScreen(0.3f), Is.EqualTo(0.3f).Within(0.0001f),
                "UpgradeScreen: a legitimate prior speed must still round-trip, not get forced to 1.");
        }
    }
}
