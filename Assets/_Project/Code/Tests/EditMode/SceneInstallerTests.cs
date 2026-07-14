using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The registry that rebuilds the world on a scene load knows about the world (YT-91).
    ///
    /// <see cref="SceneInstallers"/> finds the game's self-installing systems by reading the same
    /// attribute Unity boots from, rather than by holding a list of them — so the thing worth testing
    /// is that the scan actually finds them. If it ever silently returned nothing, Replay would go
    /// back to giving you a magenta yard and nothing else would complain.
    /// </summary>
    public sealed class SceneInstallerTests
    {
        [Test]
        public void TheSystemsThatBuildTheYard_AreAllFound()
        {
            var names = SceneInstallers.Discover().Select(m => m.DeclaringType.Name).ToArray();

            // The four that the magenta bug took down most visibly: the materials, the props, the
            // light, and the factory's moving parts.
            CollectionAssert.Contains(names, "WorldMaterials");
            CollectionAssert.Contains(names, "BackyardDressing");
            CollectionAssert.Contains(names, "BackyardLighting");
            CollectionAssert.Contains(names, "FactoryLife");
        }

        [Test]
        public void TheScan_FindsEverySystemThatSaysItInstallsItself()
        {
            // Counted from the source rather than pinned to a magic number: any type in the game's own
            // assemblies with an AfterSceneLoad method must be in the registry. A new system is covered
            // by writing it — which is the entire reason this is a scan and not a list.
            var expected = System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name.StartsWith("MaxWorlds")
                            && !a.GetName().Name.Contains("Tests")
                            && !a.GetName().Name.Contains("Editor"))
                .SelectMany(a => a.GetTypes())
                .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Static |
                                              System.Reflection.BindingFlags.Public |
                                              System.Reflection.BindingFlags.NonPublic |
                                              System.Reflection.BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false)
                             .Cast<RuntimeInitializeOnLoadMethodAttribute>()
                             .Any(a => a.loadType == RuntimeInitializeLoadType.AfterSceneLoad))
                .Where(m => m.DeclaringType != typeof(SceneInstallers))
                .Count();

            Assert.That(expected, Is.GreaterThan(10), "the game should have many self-installing systems");
            Assert.That(SceneInstallers.Discover().Length, Is.EqualTo(expected));
        }

        [Test]
        public void TheRegistry_DoesNotContainItself()
        {
            // Re-running the hook would subscribe to sceneLoaded a second time, and the world would be
            // rebuilt twice per load — every guard would hold, but the log would lie and the cost would
            // double for nothing.
            var self = SceneInstallers.Discover().Where(m => m.DeclaringType == typeof(SceneInstallers));
            Assert.That(self, Is.Empty);
        }
    }
}
