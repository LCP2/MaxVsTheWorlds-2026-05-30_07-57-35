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

            // NO frames yielded, deliberately (YT-113). This used to wait two, because the zone was
            // built from a bare primitive and depended on the surface director sweeping it a frame
            // later. That frame was real: grass puddles spawn every 0.18s for the length of a
            // charge, so on any given frame one of them was still wearing Unity's default material.
            // The zone now takes a proper material at construction, so the guarantee is no longer
            // "claimed in time" but "never wrong at all" — which is what this test should have been
            // asking for, and it can only be asked before the sweep has had a chance to run.
            var renderer = zone.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(renderer, "the zone should have a visual");

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
