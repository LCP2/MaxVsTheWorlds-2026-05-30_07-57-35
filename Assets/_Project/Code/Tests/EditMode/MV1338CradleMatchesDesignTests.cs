using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1338 — the Assembly Cradle's permanent gantry (corner posts, top rails, turret housings) must
    /// read as the ticket's reserved red, matching MV-1227.png's SPECIFICATION, and the eye lamp/out-pad
    /// must be present. Tier 2 (resolved values): reads each renderer's own resolved base colour off its
    /// sharedMaterial, never an authored constant restated as a literal.
    /// </summary>
    // Guards MV-1338
    public sealed class MV1338CradleMatchesDesignTests
    {
        private static readonly Vector3 RigOrigin = new Vector3(-91234f, 0f, 45678f);

        private GameObject _cradleGo;

        [SetUp]
        public void SetUp() => RobotEnemy.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_cradleGo != null) Object.DestroyImmediate(_cradleGo);
            RobotEnemy.ResetRegistry();
        }

        [Test]
        public void BuiltCradle_FrameAndTurretsReadRed_EyeLampAndOutPadPresent()
        {
            _cradleGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cradleGo.name = "AssemblyCradleDesign";
            _cradleGo.transform.position = RigOrigin;
            _cradleGo.transform.localScale = new Vector3(3f, 3f, 3f); // the ticket's own 3x3x3 m footprint

            var cradle = _cradleGo.AddComponent<AssemblyCradle>();
            cradle.Build(); // AddComponent's own Awake never runs outside Play mode

            Renderer[] all = _cradleGo.GetComponentsInChildren<Renderer>(includeInactive: true);

            Renderer[] posts = all.Where(r => r.name == "CornerPost").ToArray();
            Assert.AreEqual(4, posts.Length, "the gantry must have exactly four corner posts");
            Renderer[] rails = all.Where(r => r.name == "TopRailFront" || r.name == "TopRailBack").ToArray();
            Assert.AreEqual(2, rails.Length, "the gantry must have exactly two top rails");

            foreach (Renderer r in posts.Concat(rails))
                AssertResolvedRed(r);

            Assert.AreEqual(2, cradle.TurretHousings.Length, "the cradle must have exactly two turret housings");
            foreach (Transform housing in cradle.TurretHousings)
            {
                AssertResolvedRed(housing.GetComponent<Renderer>());
                Transform barrel = housing.parent.Find("Barrel");
                Assert.NotNull(barrel, "each turret must carry a barrel sibling");
                Assert.NotNull(barrel.GetComponent<Renderer>(), "the barrel must carry its own renderer");
            }

            Renderer eyeLamp = all.FirstOrDefault(r => r.name == "TopLens");
            Assert.NotNull(eyeLamp, "the eye lamp must be present at the top centre");

            Transform outPad = _cradleGo.GetComponentsInChildren<Transform>(includeInactive: true)
                .FirstOrDefault(t => t.name == "OutPad");
            Assert.NotNull(outPad, "the dashed out-pad root must be present");
            Assert.Greater(outPad.GetComponentsInChildren<Renderer>(includeInactive: true).Length, 0,
                "the out-pad must carry at least one dash renderer");
        }

        /// <summary>AC1's "resolved base colour" alone (a plain diffuse <c>_BaseColor</c> multiplied by
        /// the Mothership deck's own bright ambient/key light) is exactly what the Mothership deck's
        /// own lighting washes toward grey — the same mechanism named in the ticket's own "the deck
        /// lighting washing it out". Every OTHER reserved-colour hazard surface in this codebase
        /// (<see cref="MaxWorlds.Rendering.WorldMaterials.M_Hazard"/>, every emissive tone
        /// <see cref="MaxWorlds.Rendering.ShipKit"/> builds) carries its own emission so the hue survives
        /// however bright the incident light is — <see cref="_EmissionColor"/> is added AFTER the
        /// lit/ambient term in <c>StylizedCharacter.shader</c>'s <c>LitFrag</c>, never multiplied by it.
        /// The cradle's own red gantry never got that treatment, so this asserts BOTH resolved channels:
        /// base colour (already correct — MV-1227 wired this) and emission colour (the actual gap).</summary>
        private static void AssertResolvedRed(Renderer r)
        {
            Assert.NotNull(r, "expected a renderer");
            AssertRedChannel(ResolvedColor(r, "_BaseColor", "_Color"), r.name, "base");
            AssertRedChannel(ResolvedColor(r, "_EmissionColor"), r.name, "emission");
        }

        private static void AssertRedChannel(Color c, string rendererName, string channel)
        {
            Assert.GreaterOrEqual(c.r, 0.6f, $"{rendererName} resolved {channel} red must be >= 0.6, was {c.r}");
            Assert.LessOrEqual(c.g, 0.3f, $"{rendererName} resolved {channel} green must be <= 0.3, was {c.g}");
            Assert.LessOrEqual(c.b, 0.3f, $"{rendererName} resolved {channel} blue must be <= 0.3, was {c.b}");
        }

        private static Color ResolvedColor(Renderer r, params string[] propertyNames)
        {
            Material m = r.sharedMaterial;
            Assert.NotNull(m, $"{r.name} must carry a material");
            foreach (string prop in propertyNames)
                if (m.HasProperty(prop)) return m.GetColor(prop);
            Assert.Fail($"{r.name}'s material exposes none of [{string.Join(", ", propertyNames)}]");
            return default;
        }
    }
}
