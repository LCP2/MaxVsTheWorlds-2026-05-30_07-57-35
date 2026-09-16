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
    /// MV-805 -- "Max's weapon looks too much like water" (Lee, 2026-09-15): the LPPE bolt, the head
    /// gauge above Max and the muzzle/impact flashes were all the same cold cyan-white as the RCDA's
    /// own tank, and the bolt's mesh was a Unity capsule at an aspect where the two hemispheres meet
    /// in a hard crease across the middle -- "a bend" in Lee's own words. This proves the colour half
    /// of the fix: the bolt's resolved tint reads orange, and the head gauge
    /// <see cref="PlayerHealth"/> hands <see cref="WorldHealthBar.Attach"/> for the LPPE stays locked
    /// to it.
    ///
    /// MV-815 update: this test's own second half -- the resolved bolt mesh, walked along its own long
    /// axis, is a single smooth curve, never the capsule's abrupt crease -- is culled. MV-815 replaced
    /// the ogive of revolution that check was written against with a bowed crescent (a different mesh,
    /// not a tweak to the same profile), so "walked along its own long axis" no longer resolves to
    /// anything meaningful; the same "one smooth curve, no crease" property is now proven for the
    /// crescent by <c>MV815CrescentBoltTests</c> instead.
    ///
    /// Fails on base commit f89c4e7: <c>SeekerPulse.BoltColor</c> is still the cold cyan-white
    /// (0.55, 0.95, 1).
    /// </summary>
    public sealed class MV805FireBoltTests
    {
        private static readonly FieldInfo BarSecondaryFillField =
            typeof(WorldHealthBar).GetField("_secondaryFill", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _maxGo;
        private SeekerPulse _pulse;

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            UpgradeState.Reset();
            DevTuning.Reset();
            if (_maxGo != null) Object.DestroyImmediate(_maxGo);
            if (_pulse != null) _pulse.Tick(1f); // forces Retire() -> destroys bolt+trail+glow
        }

        [Test]
        public void BoltReadsOrangeLockedToTheGauge()
        {
            _pulse = SeekerPulse.Fire(Vector3.zero, Vector3.forward, speed: 18f, turnRateDegPerSec: 360f,
                lifetime: 0.01f, damage: 9f, lockRange: 14f, lockHalfAngleDeg: 35f);

            Transform boltMesh = _pulse.transform.Find("Bolt");
            Assert.IsNotNull(boltMesh, "test precondition: SeekerPulse must build a child named 'Bolt'");
            var renderer = boltMesh.GetComponent<MeshRenderer>();

            // --- (1a) colour: the bolt itself reads orange, not cyan-white ---
            Color boltTint = renderer.sharedMaterial.GetColor("_BaseColor");
            Assert.That(boltTint.r, Is.GreaterThan(boltTint.g),
                $"bolt red ({boltTint.r:0.000}) is not ahead of green ({boltTint.g:0.000}) -- doesn't read orange");
            Assert.That(boltTint.g, Is.GreaterThan(boltTint.b),
                $"bolt green ({boltTint.g:0.000}) is not ahead of blue ({boltTint.b:0.000}) -- doesn't read orange");
            Assert.That(boltTint.r, Is.GreaterThanOrEqualTo(0.9f),
                $"bolt red channel ({boltTint.r:0.000}) too low to read as orange");
            Assert.That(boltTint.b, Is.LessThanOrEqualTo(0.2f),
                $"bolt blue channel ({boltTint.b:0.000}) too high -- still reads cyan/water");

            // --- (1b) the LPPE head gauge stays locked to the bolt's own tint ---
            WeaponSystemState.Reset();
            WeaponSystemState.ActivePrimary = WeaponCatalog.PrimaryKind.Lppe;
            _maxGo = new GameObject("Max", typeof(CharacterController), typeof(PlayerController));
            var pulseLaser = _maxGo.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(pulseLaser, null);
            var waterBlaster = _maxGo.AddComponent<WaterBlaster>();
            WaterBlasterAwake.Invoke(waterBlaster, null);
            var health = _maxGo.AddComponent<PlayerHealth>();
            health.Initialize();

            var bar = _maxGo.GetComponent<WorldHealthBar>();
            var secondaryFill = (Image)BarSecondaryFillField.GetValue(bar);
            Color gaugeColor = secondaryFill.color;

            Assert.That(Mathf.Abs(gaugeColor.r - boltTint.r), Is.LessThanOrEqualTo(0.02f),
                $"gauge red ({gaugeColor.r:0.000}) doesn't match the bolt's ({boltTint.r:0.000})");
            Assert.That(Mathf.Abs(gaugeColor.g - boltTint.g), Is.LessThanOrEqualTo(0.02f),
                $"gauge green ({gaugeColor.g:0.000}) doesn't match the bolt's ({boltTint.g:0.000})");
            Assert.That(Mathf.Abs(gaugeColor.b - boltTint.b), Is.LessThanOrEqualTo(0.02f),
                $"gauge blue ({gaugeColor.b:0.000}) doesn't match the bolt's ({boltTint.b:0.000})");
        }
    }
}
