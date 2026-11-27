using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1477 (Lee, 27 Nov): fewer robots in Worlds 3-5, a ranged share of 45-50%, the Marksman and Orbital at
    /// 75% size, and Marksman / Orbital / Arc Striker spread through every world. ONE test, three tiers-2
    /// assertions measured off the loaded configs and the built bodies (no authored constant is asserted):
    /// AC1 garrison totals and ranged share, AC2 the kind spread, AC3 the resolved body bounds against the
    /// efa6ba1 values.
    /// </summary>
    public sealed class MV1477W3to5RangedBalanceTests
    {
        private static readonly HashSet<string> Ranged = new HashSet<string>
            { "gunner", "launcher", "bolter", "turret", "orbital", "arcstriker", "dreadnought" };

        // Resolved mesh-bounds extents (m) of the built bodies at efa6ba1, measured by this test's own bounds helper.
        private static readonly Vector3 MarksmanBaseSize = new Vector3(0.95f, 1.63f, 2.49f);
        private static readonly Vector3 OrbitalBaseSize = new Vector3(1.63f, 1.105f, 1.665f);

        private static Vector3 BuiltSize(EnemyKind kind, string skin)
        {
            var m = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
            var root = new GameObject("MV1477Body").transform;
            try
            {
                RobotBodies.Build(kind, root, new RobotPalette(m, m, m, m), skin);
                // The hover pivot bobs from a random phase and its floor shadow / thruster glow are world-sized
                // quads: neither is the body, so measure the body at rest.
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == "OrbitalHover") t.localPosition = Vector3.zero;
                Bounds? b = null;
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null || mf.name == "OrbitalShadow" || mf.name == "OrbitalThrusterGlow") continue;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = mb.center + Vector3.Scale(mb.extents,
                            new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 w = mf.transform.TransformPoint(c);
                        if (b == null) b = new Bounds(w, Vector3.zero); else { var bb = b.Value; bb.Encapsulate(w); b = bb; }
                    }
                }
                return b.Value.size;
            }
            finally { Object.DestroyImmediate(root.gameObject); Object.DestroyImmediate(m); }
        }

        private static void AssertScale(string what, Vector3 built, Vector3 baseline)
        {
            for (int axis = 0; axis < 3; axis++)
                Assert.AreEqual(0.75f, built[axis] / baseline[axis], 0.03f,
                    $"{what} axis {axis}: built {built[axis]:0.000} m vs efa6ba1 {baseline[axis]:0.000} m");
        }

        [Test]
        public void Worlds3To5_FewerRobots_HalfRanged_SmallerMarksmanAndOrbital_SpreadThroughEveryWorld()
        {
            var worlds = new (string key, string label, int lo, int hi)[]
            {
                (WorldLibrary.City, "City", 620, 660),
                (WorldLibrary.World3, "Reef", 840, 900),
                (WorldLibrary.Mothership, "Mothership", 630, 680),
            };

            foreach (var (key, label, lo, hi) in worlds)
            {
                WorldConfig cfg = WorldLibrary.Load(key);
                Assert.IsNotNull(cfg, $"{label} config failed to load");
                List<WorldGarrisonEntry> all = cfg.areas.Where(a => a.garrison != null).SelectMany(a => a.garrison).ToList();

                Assert.That(all.Count, Is.InRange(lo, hi), $"{label}: garrison total");
                float share = 100f * all.Count(e => Ranged.Contains(e.kind)) / all.Count;
                Assert.That(share, Is.InRange(45f, 50f), $"{label}: ranged share %");

                foreach (string kind in new[] { "bolter", "orbital", "arcstriker" })
                {
                    for (int start = 3; start + 2 <= cfg.areas.Length - 1; start++)
                    {
                        bool present = cfg.areas.Where(a => a.index >= start && a.index <= start + 2)
                            .Any(a => a.garrison != null && a.garrison.Any(e => e.kind == kind));
                        Assert.IsTrue(present, $"{label}: no {kind} in areas {start}-{start + 2}");
                    }
                }

                if (key != WorldLibrary.World3 && key != WorldLibrary.Mothership) continue;
                Assert.IsTrue(cfg.EnemyOverrideFor(EnemyKind.Bolter)?.marksman == true, $"{label}: Bolter is not a marksman");
            }

            AssertScale("Marksman", BuiltSize(EnemyKind.Bolter, RobotBodies.MarksmanSkinTag), MarksmanBaseSize);
            AssertScale("Orbital", BuiltSize(EnemyKind.Orbital, null), OrbitalBaseSize);
        }
    }
}
