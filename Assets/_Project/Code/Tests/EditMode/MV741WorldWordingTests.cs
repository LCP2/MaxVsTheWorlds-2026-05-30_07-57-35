using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-741: a live World 2 run (build d63d576-0908-2256) still showed World 1's HUD copy — "FACTORIES
    /// 2/25" instead of "REPLICATORS", and the progress banner's "DOMINATION" / "ROBOTS GET FASTER &
    /// TOUGHER". <see cref="MaxWorlds.Arena.Map.MapRuntime.Build"/> already fired
    /// <c>HudSignals.EmitWorldFactoryWording</c> with the right answer — the bug is that it fires from
    /// <c>BackyardPath.Awake</c>, and Unity runs every object's <c>Awake</c> before any object's
    /// <c>OnEnable</c> (where <see cref="HudController"/> subscribes), so the signal always arrives before
    /// anything is listening. World 1's default (false/empty) happened to already match its own wording,
    /// which is why nobody noticed until World 2 needed the signal to actually change something.
    ///
    /// Reproduces exactly that ordering — build the real world map first (firing the signal into
    /// nobody), THEN construct and enable the HUD — and reads the RESOLVED text off the built HUD
    /// objects after a layout rebuild, never an authored format string. World 2 runs first (the bug);
    /// World 1 runs last (the regression guard), which also leaves the shared HudSignals latch back at
    /// World 1's own neutral wording for whichever test runs next, entirely through the same production
    /// entry points (WorldLibrary/WorldMapLoader/MapRuntime) rather than a test-only reset hook.
    /// </summary>
    public sealed class MV741WorldWordingTests
    {
        [Test]
        public void HudWordingComesFromTheLoadedWorldNotWorld1Defaults()
        {
            DifficultyDirector.Reset();
            GameObject root2 = null, hudGo2 = null, root1 = null, hudGo1 = null;
            try
            {
                // ---------------------------------------------------------------- World 2: MV-741's bug.
                WorldConfig cfg2 = WorldLibrary.Load(WorldLibrary.World2);
                Assert.IsNotNull(cfg2, "World 2's own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg2, out MapData map2, out string reason2), reason2);

                root2 = new GameObject("MV741 World2 Probe Root");
                MapRuntime.Build(map2, root2.transform); // fires the signals before any HUD exists — same ordering as BackyardPath.Awake

                hudGo2 = new GameObject("HUD2");
                var hud2 = hudGo2.AddComponent<HudController>();
                InvokeLifecycle(hud2, "Awake");
                InvokeLifecycle(hud2, "OnEnable");

                Assert.That(GetPrivateText(hud2, "_arenaLabel").text, Does.StartWith("REPLICATORS"),
                    "World 2's factories are Replicators, not sheds — MapRuntime already computes this correctly, " +
                    "so a wrong answer here means the HUD subscribed too late to hear it");

                DifficultyDirector.Tick(DifficultyDirector.RunLengthSeconds + 100f); // push into the top band
                InvokeUpdateInvasionDial(hud2, 0f);
                Assert.That(GetPrivateText(hud2, "_dialStageLabel").text, Is.Not.EqualTo("DOMINATION"),
                    "World 2's progress banner must not still speak World 1's framing");
                Assert.That(GetPrivateText(hud2, "_dialStageLabel").text, Is.EqualTo("FLOOD"));
                Assert.That(GetPrivateText(hud2, "_dialCaption").text, Is.EqualTo("THE STORMDRAIN IS FILLING"));

                InvokeLifecycle(hud2, "OnDisable");
                Object.DestroyImmediate(hudGo2); hudGo2 = null;
                Object.DestroyImmediate(root2); root2 = null;
                DifficultyDirector.Reset();

                // ---------------------------------------------------------------- World 1: unchanged.
                WorldConfig cfg1 = WorldLibrary.Load(WorldLibrary.World1);
                Assert.IsNotNull(cfg1, "World 1's own shipped config must load for this test to mean anything");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg1, out MapData map1, out string reason1), reason1);

                root1 = new GameObject("MV741 World1 Probe Root");
                MapRuntime.Build(map1, root1.transform); // same ordering, and leaves the shared latch at World 1's own neutral wording

                hudGo1 = new GameObject("HUD1");
                var hud1 = hudGo1.AddComponent<HudController>();
                InvokeLifecycle(hud1, "Awake");
                InvokeLifecycle(hud1, "OnEnable");

                Assert.That(GetPrivateText(hud1, "_arenaLabel").text, Does.StartWith("FACTORIES"),
                    "World 1's factories are sheds, not Replicators");

                DifficultyDirector.Tick(DifficultyDirector.RunLengthSeconds + 100f); // push into the top band
                InvokeUpdateInvasionDial(hud1, 0f);
                Assert.That(GetPrivateText(hud1, "_dialStageLabel").text, Is.EqualTo("DOMINATION"),
                    "World 1 authors no pressure wording of its own — the default band name must still show");
                Assert.That(GetPrivateText(hud1, "_dialCaption").text, Is.EqualTo("ROBOTS GET FASTER & TOUGHER"));
            }
            finally
            {
                if (hudGo2 != null) { InvokeLifecycle(hudGo2.GetComponent<HudController>(), "OnDisable"); Object.DestroyImmediate(hudGo2); }
                if (hudGo1 != null) { InvokeLifecycle(hudGo1.GetComponent<HudController>(), "OnDisable"); Object.DestroyImmediate(hudGo1); }
                if (root2 != null) Object.DestroyImmediate(root2);
                if (root1 != null) Object.DestroyImmediate(root1);
                DifficultyDirector.Reset();
            }
        }

        private static Text GetPrivateText(HudController hud, string fieldName) =>
            (Text)typeof(HudController).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud);

        private static void InvokeLifecycle(Object component, string methodName) =>
            component.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(component, null);

        private static void InvokeUpdateInvasionDial(HudController hud, float dt) =>
            typeof(HudController).GetMethod("UpdateInvasionDial", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(hud, new object[] { dt });
    }
}
