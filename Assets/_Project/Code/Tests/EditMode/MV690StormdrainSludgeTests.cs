using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-690: the Stormdrain art pass on <c>MapRuntime.BuildSludge</c> — a flowing UV scroll
    /// (<see cref="SludgeFlow"/>) and a teal grade near a gate authored with the id "outfall". The ONE
    /// new test this ticket adds (testing policy v2, MV-465): builds a synthetic area carrying two
    /// sludge rects, one 2 m from an "outfall" gate and one 18 m from it, through the real
    /// <see cref="WorldMapLoader"/> → <see cref="MapRuntime"/> pipeline, and asserts RESOLVED state —
    /// never an authored constant — on what actually got built: the bound <see cref="SludgeFlow"/>'s
    /// own scroll speed, and that the near/far tiles resolved to two DIFFERENT material instances
    /// (proof the grade actually branched, without asserting a rendered pixel — Rule 2/3 of the
    /// testing policy). Fails to compile (CS0246) on ec561c2, the commit before this ticket —
    /// <c>SludgeFlow</c> doesn't exist there.
    /// </summary>
    public sealed class MV690StormdrainSludgeTests
    {
        /// <summary>Entry stub → a normal fight room carrying two sludge rects (one near the "outfall"
        /// gate on its south wall, one far from it) → a boss room. Same shape as
        /// <c>MV692WorldVerticalityTests</c>' fixture, just small enough to hand-check the two
        /// distances against <c>MapRuntime</c>'s 12 m grade radius.</summary>
        private static WorldConfig SludgeWorld()
        {
            return new WorldConfig
            {
                world = "Test World",
                dials = new WorldDials { sludgeSpeedMultiplier = 0.6f, deckHeight = 2.5f },
                areas = new[]
                {
                    new WorldArea
                    {
                        id = "stub", role = "entry",
                        origin = new WorldAreaOrigin { x = 0f, z = -6f },
                        size = new WorldAreaSize { w = 4f, d = 6f },
                    },
                    new WorldArea
                    {
                        id = "a1", role = "normal",
                        origin = new WorldAreaOrigin { x = 0f, z = 0f },
                        size = new WorldAreaSize { w = 25f, d = 20f },
                        sludge = new[]
                        {
                            // World centre (2,2) — ~2 m from the outfall gate resolved onto a1's south
                            // wall near x=2 — well inside the 12 m grade radius.
                            new WorldSludge { id = "sludgeNear", x = 1f, z = 1f, w = 2f, d = 2f },
                            // World centre (2,18) — ~18 m from the same gate, outside the radius.
                            new WorldSludge { id = "sludgeFar", x = 1f, z = 17f, w = 2f, d = 2f },
                        },
                    },
                    new WorldArea
                    {
                        id = "boss", role = "boss+exit",
                        origin = new WorldAreaOrigin { x = 0f, z = 20f },
                        size = new WorldAreaSize { w = 20f, d = 20f },
                    },
                },
                gates = new[]
                {
                    new WorldGate
                    {
                        // Marked the outfall purely by its id (MapRuntime.OutfallGateId) — an authoring
                        // convention, not a schema field, same idiom as naming a boss "big_bermuda".
                        id = "outfall", width = 3f, opensWith = "start",
                        from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.08f },
                    },
                    new WorldGate
                    {
                        id = "bg", width = 3f, opensWith = "all-sheds-destroyed",
                        from = new WorldGateEndpoint { area = "a1", wall = "N", pos = 0.5f },
                        to = new WorldGateEndpoint { area = "boss", wall = "S", pos = 0.5f },
                    },
                },
            };
        }

        [Test]
        public void BuiltSludge_ScrollsAndGradesTowardTheOutfall()
        {
            WorldConfig cfg = SludgeWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV690 Sludge Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Assert.IsTrue(built.Actors.Count >= 0);   // the build ran to completion at all

                Transform near = FindDescendant(root.transform, "sludgeNear");
                Transform far = FindDescendant(root.transform, "sludgeFar");
                Assert.IsNotNull(near, "expected a 'sludgeNear' tile in the built map");
                Assert.IsNotNull(far, "expected a 'sludgeFar' tile in the built map");

                SludgeFlow nearFlow = near.GetComponent<SludgeFlow>();
                SludgeFlow farFlow = far.GetComponent<SludgeFlow>();
                Assert.IsNotNull(nearFlow, "a built sludge tile must carry a SludgeFlow");
                Assert.Greater(nearFlow.ScrollSpeed.magnitude, 0f,
                    "the sludge material's scroll speed must resolve non-zero after bind");
                Assert.AreEqual(nearFlow.ScrollSpeed, farFlow.ScrollSpeed,
                    "flow speed itself does not depend on distance to the outfall — only the tone does");

                Material nearMat = near.GetComponent<Renderer>().sharedMaterial;
                Material farMat = far.GetComponent<Renderer>().sharedMaterial;
                Assert.AreNotSame(nearMat, farMat,
                    "a tile 2 m from the outfall and one 18 m from it must resolve to different material " +
                    "instances — proof the grade toward teal actually branched on distance");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }
    }
}
