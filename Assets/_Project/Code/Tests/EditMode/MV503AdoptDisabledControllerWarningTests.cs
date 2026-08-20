using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-503: <c>MapRuntime.Adopt</c> disables an actor's <see cref="CharacterController"/> across the
    /// teleport to its map-authored start position, then restores it to whatever it found — silently.
    /// If the controller arrived already disabled (e.g. two Awakes racing, or a previous frame leaving
    /// it off), Max is placed disabled and nothing said so. This is one of the two mechanisms the
    /// ticket's own investigation names as a live candidate for "Max rotates but never translates" on a
    /// fresh run; this proves the new warning actually fires for that case.
    /// </summary>
    public sealed class MV503AdoptDisabledControllerWarningTests
    {
        private GameObject _actor;

        [TearDown]
        public void TearDown()
        {
            if (_actor != null) Object.DestroyImmediate(_actor);
        }

        [Test]
        public void RestoringAnAlreadyDisabledControllerLogsTheMV503Warning()
        {
            _actor = new GameObject("MV-503 Adopt Probe", typeof(CharacterController));
            var cc = _actor.GetComponent<CharacterController>();
            cc.enabled = false;

            var entity = new MapEntity { id = "probe-actor", kind = "playerSpawn", x = 4f, z = -2f };
            var built = new MapBuild();

            var adopt = typeof(MapRuntime).GetMethod("Adopt", BindingFlags.NonPublic | BindingFlags.Static);
            LogAssert.Expect(LogType.Warning,
                new Regex(@"^\[MV-503\] MapRuntime\.Adopt restored 'probe-actor' \(playerSpawn\) CharacterController to disabled"));

            adopt.Invoke(null, new object[] { entity, built, _actor });

            Assert.That(cc.enabled, Is.False,
                "the controller must come back disabled, not silently flipped true by the warning itself");
            Assert.That(built.Actors["probe-actor"], Is.SameAs(_actor),
                "the warning must not skip Adopt's own bookkeeping (registering the actor into the build)");
        }
    }
}
