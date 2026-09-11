using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-760 — the gauge above Max's head must read whichever primary is actually equipped rather than
    /// the RCDA's tank unconditionally. MV-739 gated the RCDA off in World 2, so that tank sat full for
    /// the whole run while the LPPE's own working <see cref="PulseLaser.EnergyNormalized"/> pool went
    /// undisplayed, and the gauge stayed water-blue on a weapon that isn't water. Consolidated into ONE
    /// EditMode test per Lee's AC-correction comment (MV-465 Rule 1) — covers source-switch, colour and
    /// the untouched RCDA case as one regression, not three.
    ///
    /// Asserts RESOLVED values only (MV-465 Tier 2): the real Image <see cref="WorldHealthBar"/> builds,
    /// after a real drain of each weapon's own <see cref="EnergyPool"/> — never an authored constant
    /// duplicated on both sides of an assertion.
    ///
    /// Fails on current main: <c>PlayerHealth.WaterNormalized()</c>/its fixed <c>WaterColor</c> always
    /// resolve to <see cref="WaterBlaster"/> regardless of <see cref="WeaponSystemState.ActivePrimary"/>,
    /// so with the LPPE active and its own tank drained, the gauge's fillAmount stays pinned at the
    /// untouched WaterBlaster tank's 1.0 instead of tracking the LPPE's drained pool, and its colour
    /// stays water blue instead of the LPPE's cyan-white.
    /// </summary>
    public sealed class MV760PrimaryGaugeSourceTests
    {
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo WaterBlasterTankField =
            typeof(WaterBlaster).GetField("_tank", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PulseLaserTankField =
            typeof(PulseLaser).GetField("_tank", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BarRefresh =
            typeof(WorldHealthBar).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo BarSecondaryFillField =
            typeof(WorldHealthBar).GetField("_secondaryFill", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo PlayerHealthWaterColorField =
            typeof(PlayerHealth).GetField("WaterColor", BindingFlags.NonPublic | BindingFlags.Static);

        private GameObject _rcdaGo;
        private GameObject _lppeGo;

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            UpgradeState.Reset();
            DevTuning.Reset();
            if (_rcdaGo != null) Object.DestroyImmediate(_rcdaGo);
            if (_lppeGo != null) Object.DestroyImmediate(_lppeGo);
        }

        /// <summary>Builds a fresh Max rig with both primaries attached (Awake invoked via reflection,
        /// the same "AddComponent doesn't call Awake outside Play mode" idiom <c>PulseLaserTests</c>
        /// already establishes) and <see cref="WeaponSystemState.ActivePrimary"/> set BEFORE
        /// <see cref="PlayerHealth.Initialize"/> runs — the gauge's colour is baked once at that call
        /// (<see cref="WorldHealthBar.Build"/> only ever runs once per bar), same as a real spawn.</summary>
        private static (Image secondaryFill, EnergyPool tank) BuildRig(
            string name, WeaponCatalog.PrimaryKind primary, out GameObject go)
        {
            WeaponSystemState.Reset();
            WeaponSystemState.ActivePrimary = primary;

            go = new GameObject(name, typeof(CharacterController), typeof(PlayerController));
            var waterBlaster = go.AddComponent<WaterBlaster>();
            WaterBlasterAwake.Invoke(waterBlaster, null);
            var pulseLaser = go.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(pulseLaser, null);
            var health = go.AddComponent<PlayerHealth>();
            health.Initialize();

            var bar = go.GetComponent<WorldHealthBar>();
            var secondaryFill = (Image)BarSecondaryFillField.GetValue(bar);
            EnergyPool tank = primary == WeaponCatalog.PrimaryKind.Lppe
                ? (EnergyPool)PulseLaserTankField.GetValue(pulseLaser)
                : (EnergyPool)WaterBlasterTankField.GetValue(waterBlaster);
            return (secondaryFill, tank);
        }

        [Test]
        public void GaugeSwitchesSourceAndColourWithActivePrimary_MV760()
        {
            // --- AC3: the RCDA still reads exactly as it does today ---
            var rcda = BuildRig("Max-RCDA", WeaponCatalog.PrimaryKind.Rcda, out _rcdaGo);
            rcda.tank.TrySpend(rcda.tank.Max * 0.35f);
            BarRefresh.Invoke(_rcdaGo.GetComponent<WorldHealthBar>(), null);

            Assert.That(rcda.secondaryFill.fillAmount, Is.EqualTo(rcda.tank.Normalized).Within(0.001f),
                "with the RCDA active, the gauge must still track WaterBlaster's own tank");
            Color expectedWaterColor = (Color)PlayerHealthWaterColorField.GetValue(null);
            Assert.That(rcda.secondaryFill.color, Is.EqualTo(expectedWaterColor),
                "the RCDA's gauge colour must be unchanged from today's water blue");

            // --- AC1 + AC2: the LPPE reads its OWN pool and a distinct, non-water colour ---
            var lppe = BuildRig("Max-LPPE", WeaponCatalog.PrimaryKind.Lppe, out _lppeGo);
            lppe.tank.TrySpend(lppe.tank.Max * 0.35f);
            BarRefresh.Invoke(_lppeGo.GetComponent<WorldHealthBar>(), null);

            Assert.That(lppe.secondaryFill.fillAmount, Is.EqualTo(lppe.tank.Normalized).Within(0.001f),
                "with the LPPE active and its own tank drained by a known amount, the gauge must track " +
                "PulseLaser.EnergyNormalized, not the untouched WaterBlaster tank");

            Color lppeColor = lppe.secondaryFill.color;
            Assert.That(lppeColor, Is.Not.EqualTo(expectedWaterColor),
                "the LPPE's gauge must not render water-blue -- it is not a water weapon");
            float waterMargin = expectedWaterColor.b - expectedWaterColor.r;
            float lppeMargin = lppeColor.b - lppeColor.r;
            Assert.That(lppeMargin, Is.LessThan(waterMargin),
                "the LPPE's gauge should read cyan-white (blue only modestly ahead of red), not the " +
                "water colour's much larger blue-over-red margin");
        }
    }
}
