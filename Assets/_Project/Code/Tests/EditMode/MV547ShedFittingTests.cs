using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// Shed roadmap stage 2 (MV-547): sheds gain corner weapon fittings by progression — spikers, then
    /// lasers, then missiles, each independently destroyable, on a FIXED authored curve. This one test
    /// (per CC_AUTONOMY's one-new-test rule) pins the whole vertical slice against the ticket's own
    /// table: a fixture area authoring a fitting tier spawns the authored count of the authored kind on
    /// the shed, each with that kind's own range/cadence resolved off <see cref="ShedFitting.StatsFor"/>
    /// (not re-asserting an authored constant — these are read off the BUILT instance, same idiom
    /// <c>MV541MultiShedTests</c> uses for shed body size); one fitting can be destroyed while the shed
    /// and its siblings survive; and destroying the shed takes any survivors with it.
    /// </summary>
    public sealed class MV547ShedFittingTests
    {
        /// <summary>A minimal three-area world (entry stub / one-shed area / boss), same shape as
        /// <c>MV541MultiShedTests.TwoShedWorld</c>, with the area's shed authoring one fitting tier.</summary>
        private static WorldConfig FittingWorld(string fittingKind, int fittingCount) => new WorldConfig
        {
            world = "Test World",
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", role = "entry",
                    origin = new WorldAreaOrigin { x = -2f, z = -6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", role = "shed", hasShed = true,
                    origin = new WorldAreaOrigin { x = -15f, z = 0f },
                    size = new WorldAreaSize { w = 30f, d = 30f },
                    shedFittings = fittingKind,
                    shedFittingCount = fittingCount,
                    sheds = new[] { new WorldShed { x = 0f, z = 15f } },
                },
                new WorldArea
                {
                    id = "boss", role = "boss+exit",
                    origin = new WorldAreaOrigin { x = -15f, z = 30f },
                    size = new WorldAreaSize { w = 30f, d = 20f },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
                new WorldGate
                {
                    id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                    from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                },
            },
        };

        [TestCase("spiker", ShedFittingKind.Spiker, 8f, 2.5f, 40f)]
        [TestCase("laser", ShedFittingKind.Laser, 12f, 6f, 60f)]
        [TestCase("missile", ShedFittingKind.Missile, 14f, 8f, 80f)]
        public void ShedFittings_SpawnAuthoredCountAndType_ResolveTheTable_AndDieIndependentlyOrWithTheShed(
            string fittingKind, ShedFittingKind expectedKind, float expectedRange, float expectedCadence, float expectedHp)
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(FittingWorld(fittingKind, 3), out MapData map, out string reason), reason);

            var root = new GameObject("ShedFitting Probe Root");
            try
            {
                // Awake isn't reliably invoked for AddComponent outside Play mode (same note
                // MV456ShedFaucetTests/MV683HutchAreaLeashTests carry) — MapRuntime.BuildFactory adds
                // MowerHutch exactly as it would in a live level, but this EditMode test has to drive its
                // Awake by hand for _health to actually exist before TakeDamage is exercised. Also silences
                // BuildCore's edit-mode DestroyImmediate console noise, same precedent as those tests.
                LogAssert.ignoreFailingMessages = true;

                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.TryGetValue("a1_shed", out GameObject shedGo), "the shed actor did not build");

                var hutch = shedGo.GetComponent<MowerHutch>();
                Assert.IsNotNull(hutch, "the shed carries no MowerHutch");
                typeof(MowerHutch).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(hutch, null);

                // AC: spawns the authored count of the authored type on the shed's corners.
                ShedFitting[] fittings = shedGo.GetComponentsInChildren<ShedFitting>();
                Assert.AreEqual(3, fittings.Length, "the authored fitting count did not spawn");

                foreach (ShedFitting f in fittings)
                {
                    Assert.AreEqual(expectedKind, f.Kind, "spawned the wrong fitting kind");
                    Assert.AreEqual(expectedRange, f.Range, 1e-3f, "range did not resolve to the authored table");
                    Assert.AreEqual(expectedCadence, f.Cadence, 1e-3f, "cadence did not resolve to the authored table");
                }

                // AC: one fitting can be destroyed while the shed and remaining fittings survive.
                ShedFitting hit = fittings[0];
                hit.TakeDamage(new DamageInfo(expectedHp, hit.transform.position, Vector3.forward, Team.Player));
                Assert.IsFalse(hit.IsAlive, "the fitting should be dead after taking its full authored HP");
                Assert.IsTrue(hutch.IsAlive, "destroying one fitting must not damage the shed");
                for (int i = 1; i < fittings.Length; i++)
                    Assert.IsTrue(fittings[i].IsAlive, "an undamaged sibling fitting died along with the destroyed one");

                // AC: destroying the shed removes the survivors.
                hutch.TakeDamage(new DamageInfo(hutch.AuthoredMax, shedGo.transform.position, Vector3.forward, Team.Player));
                Assert.IsFalse(hutch.IsAlive, "the shed should be dead after taking its full authored HP");
                for (int i = 1; i < fittings.Length; i++) fittings[i].Tick(0f); // one poll tick to notice
                for (int i = 1; i < fittings.Length; i++)
                    Assert.IsFalse(fittings[i].IsAlive, "a surviving fitting outlived the shed it was mounted on");
            }
            finally
            {
                Object.DestroyImmediate(root);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
