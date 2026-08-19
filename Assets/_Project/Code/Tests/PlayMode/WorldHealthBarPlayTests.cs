using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The floating HP readout over each unit (YT-111), for the two cases that genuinely need a real
    /// robot: <see cref="RobotEnemy"/> attaches its own bar from <c>Awake()</c>, which — like every
    /// other Awake/OnEnable-driven build in this codebase — never runs as a side effect of
    /// AddComponent outside Play mode, so these two can't move to EditMode without extracting
    /// RobotEnemy's build path (bigger surgery, deferred — see MV-464). Every other assertion in this
    /// fixture (bar geometry, colour ramp, water gauge, camera billboard, clutter rules) tests
    /// <see cref="WorldHealthBar.Attach"/> directly and has moved to
    /// <c>Tests/EditMode/WorldHealthBarTests.cs</c>.
    /// </summary>
    public sealed class WorldHealthBarPlayTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            yield return null;
        }

        /// <summary>
        /// Robots are pooled: a dead one is deactivated and handed back, not destroyed. The bar is a
        /// child so it returns with the body — this pins that, because the failure mode is a second
        /// wave that spawns with no bars and looks like the feature was never built.
        /// </summary>
        [UnityTest]
        public IEnumerator ARecycledRobotComesBackWithItsBar()
        {
            _go = new GameObject("Robot", typeof(CharacterController));
            var robot = _go.AddComponent<RobotEnemy>();
            yield return null;

            Assert.IsNotNull(_go.GetComponent<WorldHealthBar>(), "a robot spawned with no bar");

            _go.SetActive(false);
            yield return null;
            _go.SetActive(true);
            robot.ResetState();
            yield return null;

            var bar = _go.GetComponent<WorldHealthBar>();
            Assert.IsNotNull(bar, "the bar did not survive being pooled");
            Assert.IsNotNull(_go.GetComponentInChildren<Canvas>(true),
                             "the bar's canvas did not come back with the recycled body");
        }

        /// <summary>
        /// YT-122: a robot shows its bar from the moment it spawns, at full health — not only once
        /// it has been hit. That "hidden until hit" default (YT-111) is what read on device as the
        /// robots having no life bars at all.
        /// </summary>
        [UnityTest]
        public IEnumerator AFullHealthRobotAlreadyShowsItsColourCodedBar()
        {
            _go = new GameObject("Robot", typeof(CharacterController));
            _go.AddComponent<RobotEnemy>();
            yield return null;

            var bar = _go.GetComponent<WorldHealthBar>();
            Assert.That(bar.Showing, Is.True,
                "a full-health robot must already show its bar — that is the whole ticket");

            // Full health reads green (the calm end of the shared ramp), so a wall of them stays quiet.
            Color full = FindImage("Fill").color;
            Assert.That(full.g, Is.GreaterThan(full.r).And.GreaterThan(full.b),
                "a healthy robot's bar should be green, not already alarming");
        }

        private UnityEngine.UI.Image FindImage(string name)
        {
            foreach (UnityEngine.UI.Image i in _go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (i.name == name) return i;
            Assert.Fail($"no '{name}' image on the bar");
            return null;
        }
    }
}
