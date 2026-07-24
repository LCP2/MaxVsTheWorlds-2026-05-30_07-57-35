using NUnit.Framework;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Unit tests for the cross-factory live-robot budget (YT-186) — pure counter logic, no scene.
    /// See <see cref="EnemyCensus"/> for why this exists: four independent per-spawner caps of 8
    /// (YT-185) sum to a field twice the size the frame budget was ever tuned against.
    /// </summary>
    public sealed class EnemyCensusTests
    {
        [TearDown]
        public void ClearState() => EnemyCensus.Reset();

        [Test]
        public void StartsEmpty()
        {
            Assert.AreEqual(0, EnemyCensus.Live);
            Assert.IsTrue(EnemyCensus.HasRoom);
        }

        [Test]
        public void RegisterIncrements_ForgetDecrements()
        {
            EnemyCensus.Register();
            EnemyCensus.Register();
            Assert.AreEqual(2, EnemyCensus.Live);

            EnemyCensus.Forget();
            Assert.AreEqual(1, EnemyCensus.Live);
        }

        [Test]
        public void ForgetPastZero_NeverGoesNegative()
        {
            EnemyCensus.Forget();
            EnemyCensus.Forget();
            Assert.AreEqual(0, EnemyCensus.Live,
                "a stray Forget (a test cleanup racing a real death, say) must never take the count " +
                "negative and quietly grant the whole field extra room.");
        }

        [Test]
        public void HasRoom_GoesFalseExactlyAtTheGlobalCap()
        {
            for (int i = 0; i < EnemyCensus.GlobalMax; i++)
            {
                Assert.IsTrue(EnemyCensus.HasRoom, $"should still have room at {i}/{EnemyCensus.GlobalMax}");
                EnemyCensus.Register();
            }

            Assert.AreEqual(EnemyCensus.GlobalMax, EnemyCensus.Live);
            Assert.IsFalse(EnemyCensus.HasRoom, "the field is full but still claims to have room");
        }

        [Test]
        public void Reset_ClearsTheCount()
        {
            EnemyCensus.Register();
            EnemyCensus.Register();
            EnemyCensus.Reset();

            Assert.AreEqual(0, EnemyCensus.Live);
            Assert.IsTrue(EnemyCensus.HasRoom);
        }
    }
}
