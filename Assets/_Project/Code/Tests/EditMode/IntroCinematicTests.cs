using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Intro;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The opening cinematic's ART assets (YT-156). The three acts are plain builder classes — no
    /// MonoBehaviour, no scene — so they stand up in an EditMode test exactly as the code-driven rule
    /// promises, and every claim here is a claim about the geometry they build: the invaders wear the
    /// Brood-Hulk's cold chitin, nothing ships magenta, nothing carries a collider, and the shed's door
    /// and Max's turn actually move when the beat is scrubbed.
    /// </summary>
    public sealed class IntroCinematicTests
    {
        private GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("IntroTestRoot");

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        // ------------------------------------------------------------------ shared guards

        /// <summary>A primitive's default material has no URP subshader and ships MAGENTA in a build
        /// while looking correct in the editor (YT-58). Every part the cinematic builds must wear a real
        /// one — this is the whole reason the set is code-driven and not hand-placed greybox.</summary>
        private static void AssertNoMagenta(Transform under)
        {
            foreach (var r in under.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                Assert.IsNotNull(r.sharedMaterial, $"'{r.name}' has no material — it draws nothing.");
                string shader = r.sharedMaterial.shader.name;
                Assert.That(shader,
                    Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds")
                        .Or.StartWith("Sprites").Or.StartWith("Standard"),
                    $"'{r.name}' wears '{shader}' — a default material is magenta in the build.");
            }
        }

        /// <summary>Nothing in the cinematic collides: it is a picture, not a place. A stray collider in
        /// the intro set could catch gameplay raycasts the instant the yard is handed control.</summary>
        private static void AssertNoColliders(Transform under)
        {
            var cols = under.GetComponentsInChildren<Collider>(includeInactive: true);
            Assert.IsEmpty(cols, $"the intro set carries {cols.Length} collider(s); it must carry none.");
        }

        private static bool Has(Transform under, string name) =>
            under.GetComponentsInChildren<Transform>(includeInactive: true).Any(t => t.name == name);

        // ------------------------------------------------------------------ space act

        [Test]
        public void Space_HasEarthCometAndFivePods()
        {
            var space = new IntroSpace(_root.transform, Vector3.zero);
            space.SetActive(true);

            Assert.IsTrue(Has(space.Root, "Ocean"), "the Earth has no ocean sphere.");
            Assert.GreaterOrEqual(space.Root.GetComponentsInChildren<Transform>(true)
                                       .Count(t => t.name == "Continent"), 5,
                "the globe is an all-blue marble — it needs land patches to read as Earth.");
            Assert.IsTrue(Has(space.Root, "CometCore"), "there is no comet.");
            Assert.AreEqual(5, space.LandingPoints.Count, "the comet must split into five landing pods.");
            Assert.AreEqual(5, space.Root.GetComponentsInChildren<Transform>(true)
                                    .Count(t => t.name.StartsWith("Pod") && t.name != "PodShell"
                                             && t.name != "PodPlate" && t.name != "PodEye"),
                "there are not five pods to rain down.");

            AssertNoColliders(space.Root);
            AssertNoMagenta(space.Root);
        }

        [Test]
        public void Space_PodsWearTheInvaderChitin_NotAnEarthlyColour()
        {
            var space = new IntroSpace(_root.transform, Vector3.zero);
            space.SetActive(true);

            var shell = space.Root.GetComponentsInChildren<MeshRenderer>(true)
                                  .First(r => r.name == "PodShell");
            Color c = shell.sharedMaterial.GetColor("_BaseColor");

            // The Brood-Hulk's void chitin (YT-150): dark, and cold (blue >= red). An earthly grey or
            // brown rock would break the "the thing that lands is the thing you fight" read.
            Assert.Less(c.r + c.g + c.b, 0.9f, "the pod shell is not the invaders' dark chitin.");
            Assert.GreaterOrEqual(c.b + 0.001f, c.r, "the pod shell is warm — the invaders read COLD.");
        }

        [Test]
        public void Space_ScorchesAreDarkUntilAPodLands()
        {
            var space = new IntroSpace(_root.transform, Vector3.zero);
            space.SetActive(true);

            foreach (var t in space.Root.GetComponentsInChildren<Transform>(true)
                                   .Where(t => t.name == "Scorch"))
                Assert.IsFalse(t.gameObject.activeSelf,
                    "a landing scorch is lit before any pod has come down.");
        }

        [Test]
        public void Space_PodsLandAndLeaveScorches()
        {
            var space = new IntroSpace(_root.transform, Vector3.zero);
            space.SetActive(true);

            space.SetSplit(1f);   // run the split to the end — every pod has come down by now

            int lit = space.Root.GetComponentsInChildren<Transform>(true)
                                .Count(t => t.name == "Scorch" && t.gameObject.activeSelf);
            Assert.AreEqual(5, lit, "the pods did not all land and leave a scorch on the globe.");
        }

        // ------------------------------------------------------------------ descent act

        [Test]
        public void Descent_BuildsATownAndTheShedTheDiveLandsOn()
        {
            var descent = new IntroDescent(_root.transform, Vector3.zero);
            descent.SetActive(true);

            int buildings = descent.Root.GetComponentsInChildren<Transform>(true)
                                        .Count(t => t.name == "Building");
            Assert.Greater(buildings, 20, "the town is too sparse to read as a town from the dive.");
            Assert.IsTrue(Has(descent.Root, "Walls"), "Max's shed has no walls.");
            Assert.IsTrue(Has(descent.Root, "RoofSlab"), "the shed has no pitched roof to rush at.");
            Assert.IsNotNull(descent.DiveTarget, "the dive has no roof apex to aim at.");

            AssertNoColliders(descent.Root);
            AssertNoMagenta(descent.Root);
        }

        // ------------------------------------------------------------------ shed act

        [Test]
        public void Shed_HasMaxWithHisHoseAndADoor()
        {
            var shed = new IntroShed(_root.transform, Vector3.zero);
            shed.SetActive(true);

            Assert.IsNotNull(shed.Max, "there is no Max in the shed.");
            Assert.IsTrue(Has(shed.Root, "Hood"), "Max has no hood — the load-bearing 'kid in a hoodie' read.");
            Assert.IsTrue(Has(shed.Root, "Tank"), "Max is not holding his water gun.");
            Assert.IsTrue(Has(shed.Root, "Hose"), "the gun has no hose — this is a HOSE, not a bottle gun.");
            Assert.IsNotNull(shed.Door, "the shed has no door to open.");

            AssertNoColliders(shed.Root);
            AssertNoMagenta(shed.Root);
        }

        [Test]
        public void Shed_DoorShutAndMaxAtBenchAtTheStart()
        {
            var shed = new IntroShed(_root.transform, Vector3.zero);
            shed.SetPhase(0f);

            Assert.Less(shed.DoorOpen01, 0.01f, "the door is already open before the beat begins.");
            Assert.Less(shed.Turn01, 0.01f, "Max has already turned from his bench before he notices.");
        }

        [Test]
        public void Shed_GrabsHoseAndOpensDoorAsTheBeatRuns()
        {
            var shed = new IntroShed(_root.transform, Vector3.zero);

            shed.SetPhase(1f);
            Assert.Greater(shed.DoorOpen01, 0.9f, "the door never opens on the game — the payoff beat.");
            Assert.Greater(shed.Turn01, 0.9f, "Max never turns from his bench to the door.");

            // The door actually swings (its hinge rotates), it is not a still image that reports 'open'.
            float openYaw = Mathf.Abs(Mathf.DeltaAngle(0f, shed.Door.localEulerAngles.y));
            shed.SetPhase(0f);
            float shutYaw = Mathf.Abs(Mathf.DeltaAngle(0f, shed.Door.localEulerAngles.y));
            Assert.Greater(openYaw - shutYaw, 30f, "the door hinge does not actually swing between shut and open.");
        }
    }
}
