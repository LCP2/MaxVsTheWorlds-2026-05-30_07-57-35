using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The yard survives a Replay (YT-91).
    ///
    /// This is the test the project did not have, and its absence is the whole bug. Every PlayMode
    /// test in this suite stands its subject up BY HAND, because "the AfterSceneLoad moment is gone
    /// inside a test" — so nothing ever loaded the real scene, and nothing ever loaded it TWICE. The
    /// second load is where the game came back magenta: the seventeen self-installing systems that
    /// build the world run once per process, not once per scene, and Replay is a scene load.
    ///
    /// So this one does the thing the others avoid: it loads the actual shipped scene, and then it
    /// loads it again, the way the REPLAY button does.
    /// </summary>
    public sealed class SceneReloadPlayTests
    {
        private const int Slice = 0;   // Backyard_Slice — scene 0 is the playable scene (see CC_AUTONOMY)

        [UnityTest]
        public IEnumerator TheYardIsRebuilt_EveryTimeTheSceneLoads()
        {
            yield return Load();
            AssertTheYardIsThere("on the first load");

            // REPLAY.
            yield return Load();
            AssertTheYardIsThere("after one replay");

            // And again — a run of replays must not degrade it either.
            yield return Load();
            AssertTheYardIsThere("after two replays");
        }

        private static IEnumerator Load()
        {
            SceneManager.LoadScene(Slice);

            // One frame for the scene's own Awake and the sceneLoaded callback that re-installs the
            // world; a second for the systems' Awake to actually dress it.
            yield return null;
            yield return null;
        }

        private static void AssertTheYardIsThere(string when)
        {
            Assert.That(Object.FindFirstObjectByType<WorldMaterials>(), Is.Not.Null,
                $"the materials system is missing {when} — the greybox has nothing to wear");

            Assert.That(Object.FindFirstObjectByType<BackyardDressing>(), Is.Not.Null,
                $"the yard was never dressed {when} — no fence, no trees, no flower beds");

            Assert.That(Object.FindFirstObjectByType<BackyardLighting>(), Is.Not.Null,
                $"the lighting is missing {when}");

            // The one this ticket's neighbour cares about: the factory's moving parts (YT-78).
            Assert.That(Object.FindFirstObjectByType<FactoryLife>(), Is.Not.Null,
                $"the factory is not running {when}");

            // And the load-bearing one. A renderer still wearing the material it was CREATED with —
            // rather than one of ours — is a renderer that comes out magenta in a URP player. This is
            // the assertion that fails on the code that shipped.
            var ground = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.sharedMaterial != null && r.sharedMaterial.shader != null)
                .Any(r => r.sharedMaterial.shader.name == MaterialLibrary.GroundShaderName);

            Assert.That(ground, Is.True,
                $"nothing in the yard is wearing the ground shader {when} — the lawn is undressed");
        }
    }
}
