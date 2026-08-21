using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Combat;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// MV-263: spending parts on the RCDA Range/Spread tracks (the Weapons screen, MV-248) must
    /// visibly change the weapon, exactly the same "not just the outline" requirement
    /// <see cref="UpgradeEffectsPlayTests"/> already enforces for the legacy nozzles — the reach/spread
    /// the hit test uses, the water JET, and the aim-arc outline all have to move together, or a track
    /// level-up is a number nobody can see or feel.
    /// </summary>
    public sealed class RcdaTrackEffectsPlayTests
    {
        private GameObject _max;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            UpgradeState.Reset();
            WeaponSystemState.Reset();
            PickupWallet.Reset();
            Time.timeScale = 1f;
            yield return null;

            _max = new GameObject("Max");
            _max.tag = "Player";
            _max.AddComponent<CharacterController>();
            _max.AddComponent<WaterBlaster>();
            _max.AddComponent<PlayerController>();
            yield return null;   // Awake/OnEnable build the weapon sub-objects
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1f;
            if (_max != null) Object.Destroy(_max);
            yield return null;
            UpgradeState.Reset();
            WeaponSystemState.Reset();
            PickupWallet.Reset();
        }

        private WaterBlaster Blaster => _max.GetComponent<WaterBlaster>();

        // MV-515: PartSpend.TrySpendOnTrack was deleted (dead — no runtime caller). This suite is about
        // a track's LEVEL visibly changing the weapon, not the currency that raises it, so raise the
        // level directly through the model layer, same as CellSpend.TryUpgradeNode does in production.
        private static void SpendOnTrack(WeaponTrackKind kind)
        {
            bool raised = WeaponSystemState.LevelUpTrack(kind);
            Assert.That(raised, Is.True, $"raising {kind}'s level should have succeeded");
        }

        [UnityTest]
        public IEnumerator RangeTrack_LengthensTheReach()
        {
            float baseRange = Blaster.Range;
            SpendOnTrack(WeaponTrackKind.Range);
            yield return null;

            Assert.That(Blaster.Range, Is.GreaterThan(baseRange + 0.1f),
                "spending on the Range track should have lengthened the reach — the effect never fired (MV-263)");
        }

        [UnityTest]
        public IEnumerator RangeTrack_CompoundsAcrossLevels()
        {
            SpendOnTrack(WeaponTrackKind.Range);
            yield return null;
            float afterOne = Blaster.Range;

            SpendOnTrack(WeaponTrackKind.Range);
            yield return null;

            Assert.That(Blaster.Range, Is.GreaterThan(afterOne + 0.1f), "a second level should reach further still");
        }

        [UnityTest]
        public IEnumerator SpreadTrack_WidensTheCone()
        {
            float baseCone = Blaster.ConeHalfAngle;
            SpendOnTrack(WeaponTrackKind.Spread);
            yield return null;

            Assert.That(Blaster.ConeHalfAngle, Is.GreaterThan(baseCone + 0.5f),
                "spending on the Spread track should have widened the cone — the effect never fired (MV-263)");
        }

        [UnityTest]
        public IEnumerator RangeTrack_TheWaterJetItselfReFits_NotJustTheOutline()
        {
            var vfx = _max.GetComponent<MaxWorlds.VFX.WaterVfx>();
            Assert.That(vfx, Is.Not.Null, "the blaster should carry its water VFX");
            float baseSpeed = vfx.EmitterSpeed;

            SpendOnTrack(WeaponTrackKind.Range);
            yield return null;

            Assert.That(vfx.EmitterSpeed, Is.GreaterThan(baseSpeed + 0.1f),
                "the water JET didn't lengthen — the emitter is still reading the base reach (the YT-141-shaped bug)");
        }

        [UnityTest]
        public IEnumerator SpreadTrack_TheWaterJetItselfReFits_NotJustTheOutline()
        {
            var vfx = _max.GetComponent<MaxWorlds.VFX.WaterVfx>();
            Assert.That(vfx, Is.Not.Null, "the blaster should carry its water VFX");
            float baseAngle = vfx.EmitterHalfAngle;

            SpendOnTrack(WeaponTrackKind.Spread);
            yield return null;

            Assert.That(vfx.EmitterHalfAngle, Is.GreaterThan(baseAngle + 0.5f),
                "the water JET didn't widen — the emitter is still reading the base cone (the YT-141-shaped bug)");
        }

        [UnityTest]
        public IEnumerator RangeTrack_TheReticleReFits()
        {
            var mesh = ReticleMesh();
            Assert.That(mesh, Is.Not.Null, "no reticle mesh before any spend");
            float baseDepth = mesh.bounds.size.z;

            SpendOnTrack(WeaponTrackKind.Range);
            yield return null;

            var mesh2 = ReticleMesh();
            Assert.That(mesh2, Is.Not.Null, "no reticle mesh after spend");
            Assert.That(mesh2.bounds.size.z, Is.GreaterThan(baseDepth + 0.05f),
                "the aim-arc outline didn't lengthen with the Range track — it now lies about the reach (MV-263)");
        }

        [UnityTest]
        public IEnumerator DamageTrack_IncreasesThePerTickDamage()
        {
            float baseDamage = Blaster.EffectiveDamagePerTick;
            SpendOnTrack(WeaponTrackKind.Damage);
            yield return null;

            Assert.That(Blaster.EffectiveDamagePerTick, Is.GreaterThan(baseDamage + 0.1f),
                "spending on the Damage track should have raised the per-tick damage (MV-291) — the primary's damage was a flat number nobody's upgrade ever touched");
        }

        [UnityTest]
        public IEnumerator DamageTrack_CompoundsAcrossLevels()
        {
            SpendOnTrack(WeaponTrackKind.Damage);
            yield return null;
            float afterOne = Blaster.EffectiveDamagePerTick;

            SpendOnTrack(WeaponTrackKind.Damage);
            yield return null;

            Assert.That(Blaster.EffectiveDamagePerTick, Is.GreaterThan(afterOne + 0.1f), "a second level should hit harder still");
        }

        [UnityTest]
        public IEnumerator SpreadTrack_TheReticleReFits()
        {
            var mesh = ReticleMesh();
            Assert.That(mesh, Is.Not.Null, "no reticle mesh before any spend");
            float baseWidth = mesh.bounds.size.x;

            SpendOnTrack(WeaponTrackKind.Spread);
            yield return null;

            var mesh2 = ReticleMesh();
            Assert.That(mesh2, Is.Not.Null, "no reticle mesh after spend");
            Assert.That(mesh2.bounds.size.x, Is.GreaterThan(baseWidth + 0.05f),
                "the aim-arc outline didn't widen with the Spread track — it now lies about the spray (MV-263)");
        }

        private static Mesh ReticleMesh()
        {
            foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                if (mf.gameObject.name == "AimReticle") return mf.sharedMesh;
            return null;
        }
    }
}
