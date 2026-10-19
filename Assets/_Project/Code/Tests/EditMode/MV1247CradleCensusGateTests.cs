using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1247 (one new test, MV-465 Rule 1) — World 5's Assembly Cradles never registered with the HUD
    /// counter's own census, so the HUD read "FACTORIES 0/1" (HudModel's own one-factory placeholder
    /// default, never overwritten) instead of "CRADLES 0/47", and nothing ever opened the Command
    /// Spine's own north gate (g29, into area 30 "The Bridge").
    ///
    /// Fails on base commit 1738b19: <c>mothership_config.json</c> carries no <c>replicatorNoun</c>, g29
    /// still authors <c>opensWith: "primary"</c> (an ordinary combat gate, never locked), and
    /// <see cref="AssemblyCradle"/> never calls <see cref="FactoryCensus.RegisterCradle"/> or
    /// <see cref="FactoryCensus.ReportCradleDestroyed"/> — so <c>FactoryCensus.CradlesTotal</c> doesn't
    /// exist on that commit at all (a compile failure), and even stubbed in, would read 0.
    ///
    /// Loads the real shipped config through the real loader (<see cref="WorldLibrary.Load"/> -&gt;
    /// <see cref="WorldMapLoader.TryLoad"/> -&gt; <see cref="MapRuntime.Build"/>), same idiom as
    /// <c>MV1232HangarBayTests</c>/<c>MV1245MothershipFinaleTests</c>. Tier 2 (resolved values): the
    /// census count, the HUD wording and the gate's own <c>Locked</c>/<c>IsOpen</c> are all read off
    /// real, built objects — never an authored constant re-read against itself.
    ///
    /// Each cradle's own <c>Build()</c> is invoked explicitly (same idiom as
    /// <c>MV1244WelderRepairTests</c>/<c>MV1246AssemblyCradleTurretTests</c>): <c>AddComponent</c>'s own
    /// <c>Awake</c> never runs outside Play mode in this project's EditMode harness, so
    /// <see cref="MapRuntime.BuildCradle"/>'s own after-the-fact <c>AddComponent&lt;AssemblyCradle&gt;()</c>
    /// leaves every cradle un-built until a caller does this.
    /// </summary>
    public sealed class MV1247CradleCensusGateTests
    {
        [SetUp]
        public void SetUp() => FactoryCensus.Reset();

        [TearDown]
        public void TearDown() => FactoryCensus.Reset();

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(component, null);

        [Test]
        public void MothershipCradleCensusDrivesHudWordingAndOpensCommandSpineGateOnlyAt47Of47()
        {
            GameObject root = null, areaDirectorGo = null, runnerGo = null;
            try
            {
                WorldConfig cfg = WorldLibrary.Load(WorldLibrary.Mothership);
                Assert.IsNotNull(cfg, "the Mothership's own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

                root = new GameObject("MV1247 Root");
                MapBuild built = MapRuntime.Build(map, root.transform);

                Assert.IsTrue(built.Actors.TryGetValue("g29", out GameObject gateGo) && gateGo != null,
                    "area 29's own incoming gate (g29, the Command Spine's north gate) must build");
                var gate = gateGo.GetComponent<AreaGate>();
                Assert.IsNotNull(gate, "g29 must build a real AreaGate");
                InvokeAwake(gate); // same idiom as MV1245MothershipFinaleTests' own built.ExitGate

                Assert.AreEqual(47, built.Cradles.Count, "World 5 authors 47 Assembly Cradles total");
                foreach (AssemblyCradle cradle in built.Cradles)
                    cradle.Build(); // AddComponent's own Awake never runs outside Play mode

                Assert.AreEqual(47, FactoryCensus.CradlesTotal,
                    "every built cradle must self-register with the HUD counter's own census");
                Assert.AreEqual(0, FactoryCensus.CradlesDestroyed, "none have been destroyed yet");

                Assert.IsTrue(HudSignals.LastWorldFactoryWording.HasValue,
                    "MapRuntime.Build must fire the HUD wording signal");
                (bool isReplicatorWorld, string noun) = HudSignals.LastWorldFactoryWording.Value;
                Assert.IsTrue(isReplicatorWorld,
                    "a world whose only sources are Cradles must read as replicator-style wording, not FACTORIES");
                Assert.AreEqual("CRADLES", noun, "the HUD wording must resolve to CRADLES, not REPLICATORS");

                areaDirectorGo = new GameObject("MV1247 AreaDirector");
                var areaDirector = areaDirectorGo.AddComponent<AreaAccumulationDirector>();
                areaDirector.ConfigureWorld(cfg, WorldCatalog.IndexOf(WorldIds.Mothership));

                runnerGo = new GameObject("MV1247 WorldRunner");
                var runner = runnerGo.AddComponent<WorldRunner>();
                runner.Configure(cfg, map, built, areaDirector);

                Assert.IsTrue(gate.Locked, "the Command Spine gate must stay locked before any cradle falls");
                Assert.IsFalse(gate.IsOpen);

                // Destroy 46 of the 47 — must stay locked and shut.
                for (int i = 0; i < built.Cradles.Count - 1; i++)
                    built.Cradles[i].TakeDamage(new DamageInfo(999999f, built.Cradles[i].transform.position, Vector3.forward, Team.Player));
                runner.RefreshConditionGates();

                Assert.AreEqual(46, FactoryCensus.CradlesDestroyed);
                Assert.IsTrue(gate.Locked, "46/47 destroyed must not unlock the Command Spine gate");
                Assert.IsFalse(gate.IsOpen, "46/47 destroyed must not open the Command Spine gate");

                // Destroy the 47th — must open.
                AssemblyCradle last = built.Cradles[built.Cradles.Count - 1];
                last.TakeDamage(new DamageInfo(999999f, last.transform.position, Vector3.forward, Team.Player));
                runner.RefreshConditionGates();

                Assert.AreEqual(47, FactoryCensus.CradlesDestroyed);
                Assert.IsFalse(gate.Locked, "47/47 destroyed must unlock the Command Spine gate");
                Assert.IsTrue(gate.IsOpen, "destroying every cradle (47/47) must open the Command Spine gate");
            }
            finally
            {
                if (runnerGo != null) Object.DestroyImmediate(runnerGo);
                if (areaDirectorGo != null) Object.DestroyImmediate(areaDirectorGo);
                if (root != null) Object.DestroyImmediate(root);
            }
        }
    }
}
