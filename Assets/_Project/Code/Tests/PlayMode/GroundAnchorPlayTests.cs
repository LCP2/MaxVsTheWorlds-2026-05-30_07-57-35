using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The ground-anchor layer against real actors (YT-85). The EditMode tests prove the colours and
    /// the textures are right; these prove the layer actually finds an actor, puts the marks on the
    /// floor beneath it, and — the bit that would rot silently — lets go of them again when that
    /// actor dies, because enemies are POOLED and a leaked ring would outlive its robot.
    /// </summary>
    public sealed class GroundAnchorPlayTests
    {
        private GameObject _actor;

        /// <summary>A stand-in actor: a CharacterController plus an IDamageable is the whole
        /// contract GroundAnchorVfx works from, which is deliberate — it's what lets a new enemy
        /// type get anchors without anyone remembering to wire them.</summary>
        private sealed class FakeActor : MonoBehaviour, IDamageable
        {
            public Team Side = Team.Enemy;
            public bool Alive = true;
            public bool IsAlive => Alive;
            public Team Team => Side;
            public void TakeDamage(in DamageInfo info) { }
        }

        private static GroundAnchorVfx Layer() =>
            Object.FindFirstObjectByType<GroundAnchorVfx>();

        private static GroundRing[] VisibleMarks(string named) =>
            Layer() == null
                ? new GroundRing[0]
                : Layer().GetComponentsInChildren<GroundRing>(includeInactive: true)
                    .Where(r => r.Visible && r.name == named)
                    .ToArray();

        private FakeActor SpawnActor(Team side, Vector3 at, float radius = 0.5f)
        {
            _actor = new GameObject("Actor Under Test");
            _actor.transform.position = at;
            var cc = _actor.AddComponent<CharacterController>();
            cc.radius = radius;
            cc.height = 2f;
            var a = _actor.AddComponent<FakeActor>();
            a.Side = side;
            return a;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_actor != null) Object.Destroy(_actor);
            yield return null;
            yield return null;   // let the layer retire the rings it can no longer claim
        }

        [UnityTest]
        public IEnumerator TheLayerInstallsItself_WithNoSceneWiring()
        {
            yield return null;
            Assert.IsNotNull(Layer(),
                "the anchor layer should install itself — a feature that needs hand-wiring in the " +
                "editor doesn't survive a headless build (docs/CODE_DRIVEN_SCENES.md)");
        }

        [UnityTest]
        public IEnumerator AnActorGetsBothARingAndAContactShadow_OnTheGroundBeneathIt()
        {
            SpawnActor(Team.Enemy, new Vector3(4f, 1f, -2f));
            yield return null;   // install
            yield return null;   // LateUpdate places the marks

            var rings = VisibleMarks("AnchorRing");
            var shadows = VisibleMarks("ContactShadow");

            Assert.AreEqual(1, rings.Length, "the actor has no anchor ring");
            Assert.AreEqual(1, shadows.Length, "the actor is still floating — no contact shadow");

            foreach (var mark in new[] { rings[0], shadows[0] })
            {
                // On the floor under the actor — NOT at the actor's own origin, which sits at the
                // centre of its capsule and differs per actor type.
                Assert.AreEqual(4f, mark.transform.position.x, 1e-3);
                Assert.AreEqual(-2f, mark.transform.position.z, 1e-3);
                Assert.Greater(mark.transform.position.y, 0f, "coplanar with the lawn — it will z-fight");
                Assert.Less(mark.transform.position.y, 0.1f, "the mark is hovering above the ground");
            }
        }

        [UnityTest]
        public IEnumerator TheMarksFollowTheActorAsItMoves()
        {
            var actor = SpawnActor(Team.Player, Vector3.zero);
            yield return null;
            yield return null;

            actor.transform.position = new Vector3(-7f, 1f, 9f);
            yield return null;

            var ring = VisibleMarks("AnchorRing").Single();
            Assert.AreEqual(-7f, ring.transform.position.x, 1e-3, "the ring was left behind");
            Assert.AreEqual(9f, ring.transform.position.z, 1e-3);
        }

        [UnityTest]
        public IEnumerator ADeadActorLetsGoOfItsMarks_SoAPooledRobotCannotLeakOne()
        {
            // Robots are recycled, never destroyed. If the layer tracked marks per actor rather than
            // re-claiming them from zero each frame, a dead robot's ring would sit on the lawn
            // forever — and the next robot out of the pool would get a second one.
            var actor = SpawnActor(Team.Enemy, new Vector3(3f, 1f, 3f));
            yield return null;
            yield return null;
            Assert.AreEqual(1, VisibleMarks("AnchorRing").Length, "precondition: the actor is anchored");

            actor.Alive = false;
            yield return null;

            Assert.AreEqual(0, VisibleMarks("AnchorRing").Length,
                "a dead actor kept its ring — it will outlive the robot and be handed to the next one");
            Assert.AreEqual(0, VisibleMarks("ContactShadow").Length,
                "a dead actor kept its contact shadow");
        }

        [UnityTest]
        public IEnumerator MaxAndAnEnemyAreAnchoredInDifferentColours()
        {
            // The point of the whole ticket: find yourself instantly.
            SpawnActor(Team.Player, Vector3.zero);
            var enemy = new GameObject("Enemy");
            enemy.transform.position = new Vector3(5f, 1f, 0f);
            var ecc = enemy.AddComponent<CharacterController>();
            ecc.radius = 0.4f;
            enemy.AddComponent<FakeActor>().Side = Team.Enemy;

            try
            {
                yield return null;
                yield return null;

                Assert.AreEqual(2, VisibleMarks("AnchorRing").Length, "both actors should be anchored");
                Assert.AreNotEqual(GroundAnchorTuning.PlayerRing, GroundAnchorTuning.EnemyRing,
                    "Max and the robots are wearing the same ring");
            }
            finally { Object.Destroy(enemy); }
        }

        /// <summary>
        /// The one that closes the gap. Every test above builds a FakeActor that satisfies the
        /// contract GroundAnchorVfx works from — of course it does, I wrote both. What none of them
        /// prove is that the actors the GAME ships satisfy it. If Max turned out to carry no
        /// CharacterController, or a robot no IDamageable, this feature would do exactly nothing in
        /// the real game and every other test here would still be green. So: use the real
        /// components, and let them fail if the contract is a fiction.
        /// </summary>
        [UnityTest]
        public IEnumerator TheREALMaxAndTheREALRobotBothSatisfyTheContract()
        {
            _actor = new GameObject("Real Max");
            _actor.transform.position = new Vector3(1f, 1f, 1f);
            _actor.AddComponent<CharacterController>();
            _actor.AddComponent<MaxWorlds.Player.PlayerController>();
            var health = _actor.AddComponent<MaxWorlds.Player.PlayerHealth>();

            var robot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            robot.name = "Real Robot";
            robot.transform.position = new Vector3(6f, 0.7f, 1f);
            robot.AddComponent<CharacterController>();
            var enemy = robot.AddComponent<MaxWorlds.Enemies.RobotEnemy>();

            try
            {
                yield return null;
                yield return null;

                Assert.IsInstanceOf<IDamageable>(health, "Max is not damageable — no anchor for him");
                Assert.IsInstanceOf<IDamageable>(enemy, "the robot is not damageable — no anchor for it");
                Assert.IsTrue(health.IsAlive && enemy.IsAlive, "precondition: both actors are alive");

                Assert.AreEqual(2, VisibleMarks("AnchorRing").Length,
                    "the real Max and the real robot did not both get a ring — the contract " +
                    "GroundAnchorVfx relies on (CharacterController + IDamageable) does not hold " +
                    "for the actors the game actually ships, and this feature is a no-op in game");
                Assert.AreEqual(2, VisibleMarks("ContactShadow").Length,
                    "the real actors are still floating");
            }
            finally { Object.Destroy(robot); }
        }

        /// <summary>
        /// The Craft Bible's non-negotiable: readable on a 6-inch screen, "verify at phone aspect
        /// ratios, not just desktop... a change that looks great on a monitor but turns actors into
        /// ants on a phone is not done."
        ///
        /// This is the ticket where that has to be MEASURED rather than asserted, because YT-82 just
        /// pulled the camera back and took 18% off every actor, and these anchors are the thing
        /// that's supposed to pay for it. So: build the real shipping rig (72°, 25.1 m, 40° FOV),
        /// point it at a 2340x1080 phone, and project the marks through it. If the ring that is
        /// meant to make Max findable is itself a few pixels wide, the whole readability argument
        /// for YT-82 collapses and both tickets need revisiting.
        /// </summary>
        [UnityTest]
        public IEnumerator TheAnchorsAreLegibleOnASixInchPhone_AtTheRealCameraDistance()
        {
            const int PhoneW = 2340, PhoneH = 1080;   // a typical 6" handset, landscape

            SpawnActor(Team.Enemy, new Vector3(0f, 0.7f, 0f), radius: 0.4f);   // a rusher's footprint
            yield return null;
            yield return null;

            var rt = new RenderTexture(PhoneW, PhoneH, 16);
            var camGo = new GameObject("Phone Camera");
            var cam = camGo.AddComponent<Camera>();
            try
            {
                cam.targetTexture = rt;                 // pixelWidth/Height now report the phone
                cam.fieldOfView = 40f;                  // the shipping lens
                cam.transform.position =
                    MaxWorlds.CameraRig.FixedAngleCameraRig.ComputeOffset(25.1f, 72f);
                cam.transform.rotation = Quaternion.Euler(72f, 0f, 0f);

                Assert.AreEqual(PhoneW, cam.pixelWidth, "the camera isn't rendering at phone size");

                var ring = VisibleMarks("AnchorRing").Single();
                float radius = ring.transform.localScale.x * 0.5f;   // Show() sets scale = diameter
                Vector3 p = ring.transform.position;

                // Project the ring's own diameter across the screen.
                float px = Vector2.Distance(
                    cam.WorldToScreenPoint(p + Vector3.left * radius),
                    cam.WorldToScreenPoint(p + Vector3.right * radius));

                Debug.Log($"[YT-85] rusher anchor ring is {px:0} px wide on a {PhoneW}x{PhoneH} phone " +
                          $"({px / PhoneH * 100f:0.0}% of screen height).");

                // 24 px is about the floor for a shape whose OUTLINE has to be seen, not just its
                // presence — below that the annulus's rim is a couple of pixels and aliases into a
                // dotted line, which is precisely the failure this ring exists to prevent.
                Assert.Greater(px, 24f,
                    $"the anchor ring is only {px:0} px across on a 6-inch phone — it cannot do the " +
                    "job it was added for, and YT-82's pull-back is no longer paid for");
            }
            finally
            {
                Object.Destroy(camGo);
                rt.Release();
                Object.Destroy(rt);
            }
        }

        [UnityTest]
        public IEnumerator TheMarksNeverCastOrReceiveShadowsThemselves()
        {
            // A ground decal that casts a shadow is a ground decal that has a shadow under it.
            SpawnActor(Team.Enemy, Vector3.zero);
            yield return null;
            yield return null;

            foreach (var mark in Layer().GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off, mark.shadowCastingMode,
                    $"'{mark.name}' casts a shadow");
                Assert.IsFalse(mark.receiveShadows, $"'{mark.name}' receives shadows");
            }
        }
    }
}
