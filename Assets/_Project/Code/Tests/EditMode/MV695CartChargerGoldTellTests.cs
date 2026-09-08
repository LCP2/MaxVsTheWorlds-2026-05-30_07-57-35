using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-695 — World 2's Stormdrain roster (Scrap Rat, Sludge Drone, Cart Charger, Grate Lurker,
    /// Pipe Turret) carries the enemy design page's Tier-2 tell: a single glowing orb eye plus a
    /// gold filigree accent, the same "one gold part" rule every other kind in <see cref="RobotBodies"/>
    /// already follows (Rusher, Launcher, Blinker, Gunner, Bolter, Lurker, Turret, Sludger, Bruiser,
    /// Heavy and Brute each place at least one <see cref="RobotPalette.Gold"/> part). <c>BuildCharger</c>
    /// was the one gap: every part in it is Cool, Warm or Dark — never Gold. This asserts the RESOLVED
    /// material actually assigned to a built Cart Charger's renderers, not an authored constant, so it
    /// would still catch a future edit that reintroduces the same gap under a different method body.
    /// </summary>
    public sealed class MV695CartChargerGoldTellTests
    {
        [Test]
        public void ChargerBody_CarriesAtLeastOneGoldFiligreePart()
        {
            var root = new GameObject("Root").transform;
            var warm = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var cool = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var dark = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var gold = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            try
            {
                RobotBodies.Build(EnemyKind.Charger, root, new RobotPalette(warm, cool, dark, gold));

                int goldParts = root.GetComponentsInChildren<MeshRenderer>(true)
                    .Count(r => r.sharedMaterial == gold);

                Assert.That(goldParts, Is.GreaterThanOrEqualTo(1),
                    "the Cart Charger must carry the Tier-2 gold filigree tell every other Stormdrain " +
                    "kind already has — BuildCharger built zero parts in RobotPalette.Gold");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
                Object.DestroyImmediate(warm);
                Object.DestroyImmediate(cool);
                Object.DestroyImmediate(dark);
                Object.DestroyImmediate(gold);
            }
        }
    }
}
