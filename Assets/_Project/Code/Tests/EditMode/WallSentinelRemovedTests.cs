using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-422: the Wall (Blocker) sentinel is deleted entirely — one sentinel only, the Gunner, now
    /// just "Sentinel". Asserts the type is gone from the assembly rather than merely unused, so a
    /// stray re-add doesn't silently slip back in.
    /// </summary>
    public sealed class WallSentinelRemovedTests
    {
        [Test]
        public void WallSentinelTypeNoLongerExistsInTheAssembly()
        {
            Assembly asm = typeof(MaxWorlds.Weapons.RigState).Assembly;
            Type found = asm.GetTypes().FirstOrDefault(t => t.Name == "WallSentinel");

            Assert.That(found, Is.Null, "WallSentinel must be deleted entirely (MV-422) — found: " + found);
        }

        [Test]
        public void SentinelKindEnumNoLongerExists()
        {
            // One sentinel only now — the Wall/Gunner distinction (and the enum that named it) is gone.
            Assembly asm = typeof(MaxWorlds.Weapons.RigState).Assembly;
            Type found = asm.GetTypes().FirstOrDefault(t => t.Name == "SentinelKind");

            Assert.That(found, Is.Null, "SentinelKind must be deleted with WallSentinel (MV-422) — found: " + found);
        }
    }
}
