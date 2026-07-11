using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-56 — ambience. The one that matters is the decal cap: a long run kills a lot of robots,
    /// and an unbounded decal list would grow for the whole session.
    /// </summary>
    public sealed class AmbiencePlayTests
    {
        [UnityTest]
        public IEnumerator Decals_AreCapped_NoMatterHowManyThingsDie()
        {
            yield return null;   // let AmbienceVfx install itself

            var ambience = Object.FindFirstObjectByType<AmbienceVfx>();
            Assert.IsNotNull(ambience, "AmbienceVfx should install itself with no scene wiring");

            // A whole run's worth of kills, all at once.
            for (int i = 0; i < 200; i++)
            {
                HudSignals.EmitEnemyKilled(new Vector3(i % 20, 0f, i / 20));
            }
            yield return null;

            int decals = CountDecals(ambience);
            Assert.That(decals, Is.LessThanOrEqualTo(24),
                "scorch marks must recycle — an unbounded decal list grows for the whole session");
            Assert.That(decals, Is.GreaterThan(0), "kills should actually leave a mark");
        }

        [UnityTest]
        public IEnumerator Motes_Exist_AndAreCheap()
        {
            yield return null;

            var motes = FindSystem("AmbientMotes");
            Assert.IsNotNull(motes, "the arena should have drifting motes");
            Assert.IsNotNull(motes.GetComponent<ParticleSystemRenderer>().sharedMaterial,
                "no material — the motes would be invisible");
            Assert.That(motes.main.maxParticles, Is.LessThanOrEqualTo(200),
                "ambience must stay well inside the frame budget; it is never the point of the frame");
        }

        private static int CountDecals(AmbienceVfx ambience)
        {
            int n = 0;
            foreach (Transform child in ambience.transform)
            {
                if (child.name == "Decal") n++;
            }
            return n;
        }

        private static ParticleSystem FindSystem(string name)
        {
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.name == name) return ps;
            }
            return null;
        }
    }
}
