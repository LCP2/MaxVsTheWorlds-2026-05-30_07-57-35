using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-61 — the magenta, finally reproduced.
    ///
    /// The boss's damage zones are created mid-fight with GameObject.CreatePrimitive, which hands
    /// them Unity's BUILT-IN default material. That material has no URP subshader, so a player build
    /// draws it with the magenta error shader — while the editor quietly substitutes the pipeline's
    /// default and shows it correctly. That asymmetry is why it survived every editor check and only
    /// ever appeared on the deployed link.
    ///
    /// This test spawns a zone exactly the way the boss does and asserts it never ends up wearing
    /// something the build can't draw.
    /// </summary>
    public sealed class RuntimeSurfacePlayTests
    {
        [UnityTest]
        public IEnumerator ABossDamageZone_SpawnedMidFight_NeverKeepsTheDefaultMaterial()
        {
            yield return null;   // let RuntimeSurfaceDirector install itself
            Assert.IsNotNull(Object.FindFirstObjectByType<RuntimeSurfaceDirector>(),
                "the surface director should install itself with no scene wiring");

            var zone = DamageZone.Spawn(
                new Vector3(3f, 0f, 3f), radius: 2f, damage: 5f, life: 5f,
                armDelay: 0.3f, color: new Color(0.45f, 0.7f, 0.25f, 0.7f));

            // Give the sweep a frame — the same frame budget a real zone gets.
            yield return null;
            yield return null;

            var renderer = zone.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(renderer, "the zone should have a visual");

            Assert.IsNotNull(renderer.GetComponent<SurfaceSkinned>(),
                "the zone's visual must be claimed by the surface director within a frame of spawning");

            var shader = renderer.sharedMaterial != null ? renderer.sharedMaterial.shader : null;
            Assert.IsNotNull(shader, "no material at all would draw nothing");
            Assert.IsTrue(shader.isSupported, $"unsupported shader on the zone: {shader.name}");
            Assert.That(shader.name, Does.Not.Contain("InternalErrorShader"));

            // The tell-tale: Unity's built-in default is a Built-in-RP shader with no URP subshader,
            // which is exactly what URP renders magenta.
            Assert.That(shader.name, Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds"),
                "a damage zone must wear a URP material — the built-in default is what draws magenta " +
                "in a player build");

            Object.Destroy(zone.gameObject);
        }
    }
}
