using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-739: MV726World2StartTests already proves <see cref="HomeScreen.StartSlotWorld2"/> seeds
    /// <c>SaveSlotData.PrimaryKind</c> AND flips <see cref="WeaponSystemState.ActivePrimary"/> to
    /// <see cref="WeaponCatalog.PrimaryKind.Lppe"/> — that part was never broken. What was broken is one
    /// level deeper: neither live weapon component ever actually read that flag.
    /// <see cref="WaterBlaster"/> is baked once into the scene and never torn down, so it kept
    /// auto-firing the RCDA stream regardless of <c>ActivePrimary</c>; <see cref="PulseLaser"/> was never
    /// attached to the live player at all, only ever driven directly by reflection in
    /// <c>PulseLaserTests</c>. This proves the RESOLVED firing behaviour — whether each component's own
    /// <c>IsEmitting</c> reads true with a full tank and the trigger held — not the save field or the
    /// static flag either was seeded from.
    ///
    /// Fails on base commit 1f7608c: <c>WaterBlaster.Update</c> has no <c>ActivePrimary</c> gate, so
    /// <c>blaster.IsEmitting</c> reads true right after a WORLD 2 start (quoted in the fix comment).
    /// </summary>
    public sealed class MV739World2PrimaryFiresLppeTests
    {
        private static readonly MethodInfo WaterBlasterAwake =
            typeof(WaterBlaster).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo WaterBlasterUpdate =
            typeof(WaterBlaster).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserAwake =
            typeof(PulseLaser).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo PulseLaserUpdate =
            typeof(PulseLaser).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private string _dir;
        private GameObject _wbGo;
        private GameObject _plGo;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv739-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = -1;
            WeaponSystemState.Reset();   // also resets RigState and RigBoard back to World 1
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.ResetForTests();
            WeaponSystemState.Reset();
            RigBoard.ResetForTests();
            if (_wbGo != null) Object.DestroyImmediate(_wbGo);
            if (_plGo != null) Object.DestroyImmediate(_plGo);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        [Test]
        public void StartSlotWorld2_TheLiveWeaponThatActuallyFiresIsTheLppeNotTheRcda()
        {
            HomeScreen.StartSlotWorld2(0);

            _wbGo = new GameObject("wb_mv739");
            var blaster = _wbGo.AddComponent<WaterBlaster>();
            WaterBlasterAwake.Invoke(blaster, null);
            blaster.SetFiring(true);
            WaterBlasterUpdate.Invoke(blaster, null);
            Assert.IsFalse(blaster.IsEmitting,
                "the RCDA hose is still emitting right after a WORLD 2 start -- ActivePrimary resolved " +
                "to Lppe but the live WaterBlaster never checked it, so the seeded value never reached " +
                "the weapon that actually fires");

            _plGo = new GameObject("pl_mv739");
            var laser = _plGo.AddComponent<PulseLaser>();
            PulseLaserAwake.Invoke(laser, null);
            laser.SetFiring(true);
            PulseLaserUpdate.Invoke(laser, null);
            Assert.IsTrue(laser.IsEmitting,
                "the LPPE never emits right after a WORLD 2 start, even though it is the equipped " +
                "primary Max is meant to be firing from his first shot");
        }
    }
}
