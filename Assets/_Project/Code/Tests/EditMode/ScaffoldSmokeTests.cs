using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Foundation smoke test (YT-32): proves the EditMode test harness is wired
    /// and runnable, so <c>cc-verify</c>'s test step has at least one test to
    /// execute. Real logic tests (movement maths, damage calc, etc.) land with
    /// the features that introduce that logic.
    /// </summary>
    public sealed class ScaffoldSmokeTests
    {
        [Test]
        public void EditModeTestHarness_IsLive()
        {
            Assert.That(2 + 2, Is.EqualTo(4));
        }
    }
}
