using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1196 (the one new test, Rule 1): proves RICOCHET's disc look (design v5 §3.1) resolves off
    /// POWER, RICOCHETS and AIMING rather than being an authored constant (MV-465 Tier 2) — the
    /// resolved mark and world diameter at four POWER levels, monotonically larger mark to mark; a
    /// spawned disc's own resolved mark/diameter/fin count; and the stud count dropping live off a
    /// REAL bounce (not just its starting number).
    ///
    /// Fails on base commit 8f9dc8d (main HEAD before this ticket): <see cref="RicochetDisc.DiscMark"/>,
    /// <see cref="RicochetDisc.MarkForPowerLevel"/>, <see cref="RicochetDisc.DiameterForMark"/>,
    /// <see cref="RicochetDisc.Mark"/>, <see cref="RicochetDisc.VisualDiameter"/>,
    /// <see cref="RicochetDisc.FinCount"/> and <see cref="RicochetDisc.StudCount"/> don't exist at all,
    /// and <see cref="RicochetDisc.Spawn"/> doesn't accept a powerLevel/aimLevel pair.
    /// </summary>
    public sealed class MV1196RicochetLookTests
    {
        private GameObject _wall;

        [TearDown]
        public void TearDown()
        {
            RicochetDisc.DestroyPoolForTests();
            if (_wall != null) Object.DestroyImmediate(_wall);
        }

        [Test]
        public void DiscMarkDiameterStudsAndFinsResolveFromPowerRicochetsAndAiming()
        {
            // --- the four marks, one per tested POWER level (design v5: Mark I 1-2, II 3-4, III 5-6,
            // IV 7-8), and their world diameters -- monotonically larger mark to mark, Mark I and Mark
            // IV each within AC1's own authored range.
            Assert.AreEqual(RicochetDisc.DiscMark.I, RicochetDisc.MarkForPowerLevel(1));
            Assert.AreEqual(RicochetDisc.DiscMark.II, RicochetDisc.MarkForPowerLevel(3));
            Assert.AreEqual(RicochetDisc.DiscMark.III, RicochetDisc.MarkForPowerLevel(5));
            Assert.AreEqual(RicochetDisc.DiscMark.IV, RicochetDisc.MarkForPowerLevel(7));

            float dI = RicochetDisc.DiameterForMark(RicochetDisc.DiscMark.I);
            float dII = RicochetDisc.DiameterForMark(RicochetDisc.DiscMark.II);
            float dIII = RicochetDisc.DiameterForMark(RicochetDisc.DiscMark.III);
            float dIV = RicochetDisc.DiameterForMark(RicochetDisc.DiscMark.IV);

            Assert.That(dI, Is.InRange(0.38f, 0.42f), "Mark I must be ~0.40m (19px at the play camera's ~48px/m)");
            Assert.That(dIV, Is.InRange(0.72f, 0.78f), "Mark IV must be ~0.75m (36px at the play camera's ~48px/m)");
            Assert.Greater(dII, dI, "diameter must grow monotonically mark to mark");
            Assert.Greater(dIII, dII, "diameter must grow monotonically mark to mark");
            Assert.Greater(dIV, dIII, "diameter must grow monotonically mark to mark");

            // --- a live disc at Power 5 (Mark III), 2 bounces, AIMING level 3 -- fired dead ahead into
            // a wall so it actually bounces once, proving the stud count drops off a REAL resolved
            // bounce rather than just reporting its own starting number back.
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "MV1196 Wall";
            _wall.transform.position = new Vector3(0f, 0f, 3f);
            _wall.transform.localScale = new Vector3(10f, 2f, 0.2f);
            CoverLayer.Assign(_wall);
            Physics.SyncTransforms();

            LogAssert.ignoreFailingMessages = true; // VfxMaterials' one-time shader-resolve log line
            RicochetDisc disc;
            try
            {
                disc = RicochetDisc.Spawn(Vector3.zero, Vector3.forward, speed: 20f, bounces: 2,
                    powerLevel: 5, aimLevel: 3,
                    damage: 30f, bankBonusPerBounce: 0.2f, turnRateDegPerSec: 0f, lookaheadMetres: 0f,
                    radius: 0.25f, lifetime: 2.5f);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
            Assert.IsNotNull(disc, "test precondition: the pool must have room for one disc");

            Assert.AreEqual(RicochetDisc.DiscMark.III, disc.Mark, "a Power-5 disc must resolve Mark III");
            Assert.AreEqual(dIII, disc.VisualDiameter, 1e-4f, "the spawned disc's own diameter must match Mark III's");
            Assert.AreEqual(3, disc.FinCount, "fin count must equal p_aim's own level");
            Assert.AreEqual(2, disc.StudCount, "stud count must equal the bounces left before any bounce");

            const float step = 1f / 60f;
            int frame = 0;
            while (disc.BouncesTaken == 0 && frame < 300) { disc.Tick(step); frame++; }
            Assert.AreEqual(1, disc.BouncesTaken, "test precondition: the disc must have bounced once off the wall");
            Assert.AreEqual(1, disc.StudCount, "stud count must drop by one the instant a bounce spends it");
        }
    }
}
