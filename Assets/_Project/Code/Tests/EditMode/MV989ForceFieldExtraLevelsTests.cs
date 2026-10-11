using NUnit.Framework;
using MaxWorlds.Arena;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-989, Lee (TestFlight): "The force field is too weak. Even at full power it gets dark and
    /// burnt too quickly." Three more Force Field levels on each board — World 1
    /// (<c>rig_board.json</c>) maxLevel 5 -&gt; 8, World 2 (<c>rig_board.world2.json</c>, also loaded by
    /// World 3) maxLevel 7 -&gt; 10 — no formula change, each new level still adds
    /// <see cref="AbilityTuning.DefaultForceFieldAbsorbCapPerLevel"/> (57.5). Asserts the RESOLVED
    /// absorb cap at each board's own new ceiling, read live through <see cref="WeaponCatalog.MaxLevel"/>
    /// rather than a hardcoded level literal, so a future retune of either board's maxLevel is caught
    /// here too. MV-1156 raised <see cref="AbilityTuning.DefaultForceFieldAbsorbCap"/> from 40 to 100,
    /// which shifts these ceilings by the same +60 (502.5 / 617.5) — updated in that same ticket.
    /// </summary>
    public sealed class MV989ForceFieldExtraLevelsTests
    {
        [SetUp]
        [TearDown]
        public void Clear() => RigBoard.ResetForTests();

        [Test]
        public void ForceFieldAbsorbCapAtEachBoardsMaxLevelMatchesTheMV989Retune()
        {
            int world1Max = WeaponCatalog.MaxLevel(AbilityKind.ForceField);
            float world1Cap = AbilityTuning.ForceFieldAbsorbCap(world1Max,
                AbilityTuning.DefaultForceFieldAbsorbCap, AbilityTuning.DefaultForceFieldAbsorbCapPerLevel);
            Assert.That(world1Cap, Is.EqualTo(502.5f).Within(1e-3f),
                "World 1's Force Field must cap at 502.5 (100 + 57.5*7) once maxLevel is 8");

            RigBoard.UseWorld(WorldCatalog.IndexOf(WorldIds.Stormdrain)); // rig_board.world2.json
            int world2Max = WeaponCatalog.MaxLevel(AbilityKind.ForceField);
            float world2Cap = AbilityTuning.ForceFieldAbsorbCap(world2Max,
                AbilityTuning.DefaultForceFieldAbsorbCap, AbilityTuning.DefaultForceFieldAbsorbCapPerLevel);
            Assert.That(world2Cap, Is.EqualTo(617.5f).Within(1e-3f),
                "World 2's Force Field must cap at 617.5 (100 + 57.5*9) once maxLevel is 10");
        }
    }
}
