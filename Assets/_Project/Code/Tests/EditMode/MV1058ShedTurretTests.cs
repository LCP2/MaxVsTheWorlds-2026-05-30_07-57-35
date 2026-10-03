using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1058 (Lee, live build, 2026-10-01): shed corner turrets mostly read as "plain cubes sitting on
    /// the corners" — only Missile (MV-913) had gotten a generated-mesh rig; Spiker and Laser were still
    /// a bare <c>GameObject.CreatePrimitive(PrimitiveType.Cube)</c> at the authored 0.5 m FittingSize,
    /// tinted the shed's own Structure colour, so they blended into the roof. MV-1058 fixed the "no
    /// primitive mesh" defect but sized the dome at 0.86 m — 38% of the 2.25 m shed side.
    ///
    /// MV-1072 (Lee, phone, World 1): "These turrets have been created massively larger than before.
    /// They look ridiculous." This ticket shrinks the WHOLE rig uniformly so the dome reads at 0.45 m —
    /// 20% of the shed's side — instead. Updates THIS test's own size assertion (CC_AUTONOMY's one-new-
    /// test rule: change the existing guard rather than add a second one) and must be shown failing on
    /// base <c>cc6ecb2</c>, where TurretDome's own built bounds are ~0.86 m wide, nowhere near the 0.45 m
    /// ± 0.02 m window this test now asserts.
    ///
    /// Tier 2 (resolved value, not an authored constant): reads the BUILT TurretDome renderer's own
    /// world-space bounds off a shed assembled through the real
    /// <c>MapRuntime.Build</c> -&gt; <c>BuildShedFittings</c> -&gt; <c>ShedFitting.Bind</c> pipeline (the
    /// same fixture shape <c>MV547ShedFittingTests</c> already uses), not <see cref="ShedTurretRig"/>'s
    /// own fields — a rig that draws any other size fails this even if those fields claim otherwise. One
    /// test (per CC_AUTONOMY's one-new-test rule) covering all three kinds via TestCase, the same shape
    /// <c>MV547ShedFittingTests</c> and <c>MV911</c>'s own guard already use.
    /// </summary>
    public sealed class MV1058ShedTurretTests
    {
        /// <summary>A minimal three-area world (entry stub / one-shed area / boss), same shape as
        /// <c>MV547ShedFittingTests.FittingWorld</c>, with the area's shed authoring one fitting of the
        /// given kind.</summary>
        private static WorldConfig OneFittingWorld(string fittingKind) => new WorldConfig
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
                    shedFittingCount = 1,
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

        [TestCase("spiker", ShedFittingKind.Spiker)]
        [TestCase("laser", ShedFittingKind.Laser)]
        [TestCase("missile", ShedFittingKind.Missile)]
        public void ShedTurretDome_ReadsAt20PercentOfTheShedSide_OnAShedBuiltThroughMapRuntime(
            string fittingKind, ShedFittingKind expectedKind)
        {
            Assert.IsTrue(WorldMapLoader.TryLoad(OneFittingWorld(fittingKind), out MapData map, out string reason), reason);

            var root = new GameObject("ShedTurret Probe Root");
            try
            {
                // Silences BuildCore's edit-mode DestroyImmediate console noise, same precedent as
                // MV547ShedFittingTests.
                LogAssert.ignoreFailingMessages = true;

                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.TryGetValue("a1_shed", out GameObject shedGo), "the shed actor did not build");

                ShedFitting fitting = shedGo.GetComponentInChildren<ShedFitting>();
                Assert.IsNotNull(fitting, "the shed built no fitting");
                Assert.AreEqual(expectedKind, fitting.Kind, "spawned the wrong fitting kind");

                Renderer dome = null;
                foreach (Renderer r in fitting.GetComponentsInChildren<Renderer>())
                    if (r.name == "TurretDome") { dome = r; break; }
                Assert.IsNotNull(dome, $"{fittingKind} fitting built no TurretDome renderer");

                Assert.That(dome.bounds.size.x, Is.EqualTo(0.45f).Within(0.02f),
                    $"{fittingKind} TurretDome reads {dome.bounds.size.x:F3} m wide in X, not the ticket's " +
                    "0.45 m (20% of the 2.25 m shed side)");
                Assert.That(dome.bounds.size.z, Is.EqualTo(0.45f).Within(0.02f),
                    $"{fittingKind} TurretDome reads {dome.bounds.size.z:F3} m wide in Z, not the ticket's " +
                    "0.45 m (20% of the 2.25 m shed side)");
            }
            finally
            {
                Object.DestroyImmediate(root);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
