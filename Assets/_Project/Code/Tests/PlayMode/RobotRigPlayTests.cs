using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The Backyard robots are machines now, and the two of them are opposites (YT-96).
    ///
    /// They were a capsule (the rusher) and a cube (the bruiser) since YT-36 — tinted apart by
    /// CharacterSkin, but the same two primitives every other actor stopped being weeks ago. These are
    /// PlayMode tests because the rig only stands itself up on a live GameObject: it disables a greybox,
    /// builds a body, and drives an eye from the enemy's own state.
    /// </summary>
    public sealed class RobotRigPlayTests
    {
        private GameObject _robot;
        private GameObject _dirA;
        private GameObject _dirB;

        [TearDown]
        public void TearDown()
        {
            foreach (var go in new[] { _robot, _dirA, _dirB })
                if (go != null) Object.Destroy(go);
        }

        /// <summary>A robot exactly as EnemySpawner builds its greybox stand-in: the archetype's primitive,
        /// scaled by its body scale, on a CharacterController, dropped so its feet touch the lawn.</summary>
        private static GameObject NewRobot(EnemyKind kind)
        {
            var a = EnemyArchetype.Of(kind);
            var go = GameObject.CreatePrimitive(kind == EnemyKind.Bruiser ? PrimitiveType.Cube : PrimitiveType.Capsule);
            go.name = $"RobotEnemy {kind}";
            go.transform.localScale = a.BodyScale;
            go.transform.position = new Vector3(0f, a.SpawnHeight, 0f);
            var enemy = go.AddComponent<RobotEnemy>();   // RequireComponent adds the CharacterController
            enemy.Apply(a);
            return go;
        }

        private static Transform Model(GameObject robot) => robot.transform.Find("RobotModel");

        private static Bounds ModelBounds(GameObject robot)
        {
            var renderers = Model(robot).GetComponentsInChildren<MeshRenderer>();
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------------ it is not a primitive

        [UnityTest]
        public IEnumerator TheRusherIsASkitterBot_NotACapsule()
        {
            _robot = NewRobot(EnemyKind.Rusher);
            _robot.AddComponent<RobotRig>();
            yield return null;

            var model = Model(_robot);
            Assert.IsNotNull(model, "the rusher never built a model.");

            var parts = model.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(parts.Length, 6,
                "the rusher is barely more than the capsule it replaced. A body you read at a glance " +
                "needs a silhouette, and a silhouette needs corners.");

            foreach (var required in new[] { "Pod", "Leg", "Head", "Eye", "Claw" })
                Assert.IsTrue(model.GetComponentsInChildren<Transform>().Any(t => t.name == required),
                    $"the rusher has no '{required}'. The legs and the shear-arms are what make it a " +
                    "skittering garden bot rather than a blob.");

            // The greybox is gone — but its COLLIDERS and controller are not. Gameplay still points at it.
            Assert.IsFalse(_robot.GetComponent<MeshRenderer>().enabled,
                "the placeholder capsule is still being drawn inside the robot that replaced it.");
            Assert.IsNotNull(_robot.GetComponent<CharacterController>(),
                "the robot lost the controller it is hit and collided through.");
        }

        [UnityTest]
        public IEnumerator TheBruiserIsARollerBot_NotACube()
        {
            _robot = NewRobot(EnemyKind.Bruiser);
            _robot.AddComponent<RobotRig>();
            yield return null;

            var model = Model(_robot);
            Assert.IsNotNull(model, "the bruiser never built a model.");

            foreach (var required in new[] { "Chassis", "Tread", "Visor", "Eye", "Roller", "Arm" })
                Assert.IsTrue(model.GetComponentsInChildren<Transform>().Any(t => t.name == required),
                    $"the bruiser has no '{required}'. The treads and the roller drum are what make it a " +
                    "heavy garden machine rather than a violet box.");
        }

        /// <summary>
        /// A primitive from CreatePrimitive carries Unity's BUILT-IN default material, which has no URP
        /// subshader and draws MAGENTA in a player build while looking correct in the editor. That is how
        /// the boss's damage zones shipped (YT-61) and how the factory core shipped (YT-38).
        /// </summary>
        [UnityTest]
        public IEnumerator NoPartOfEitherRobotShipsMagenta()
        {
            foreach (var kind in new[] { EnemyKind.Rusher, EnemyKind.Bruiser })
            {
                var robot = NewRobot(kind);
                robot.AddComponent<RobotRig>();
                yield return null;

                foreach (var r in Model(robot).GetComponentsInChildren<MeshRenderer>(includeInactive: true))
                {
                    Assert.IsNotNull(r.sharedMaterial, $"'{r.name}' has no material — it draws nothing.");
                    string shader = r.sharedMaterial.shader.name;
                    Assert.That(shader,
                        Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds").Or.StartWith("Sprites"),
                        $"'{r.name}' ({kind}) is wearing '{shader}': magenta in the build, correct in the editor.");
                }

                Object.Destroy(robot);
            }
        }

        /// <summary>Nothing on the model may be shot or walked into. The robot's own controller is the
        /// hitbox; a collider on a leg would silently eat water aimed at the body.</summary>
        [UnityTest]
        public IEnumerator NoPartOfTheModelCanBeShot()
        {
            _robot = NewRobot(EnemyKind.Rusher);
            _robot.AddComponent<RobotRig>();
            yield return null;

            var colliders = Model(_robot).GetComponentsInChildren<Collider>(includeInactive: true);
            Assert.IsEmpty(colliders,
                $"the model is carrying {colliders.Length} collider(s). Every one is a shot that never " +
                "reaches the robot.");
        }

        /// <summary>
        /// The model belongs to this rig and nothing else.
        ///
        /// CharacterSkinDirector claims every renderer under an IDamageable and RuntimeSurfaceDirector
        /// repaints everything else by shape. Either one getting a part means a SECOND writer on its
        /// property block, and script order decides which the player sees — which is exactly how the
        /// boss's wind-up tell shipped dead (YT-90).
        /// </summary>
        [UnityTest]
        public IEnumerator NoSceneDirectorClaimsTheModel()
        {
            _dirA = new GameObject("CharacterSkins");
            _dirA.AddComponent<CharacterSkinDirector>();
            _dirB = new GameObject("RuntimeSurfaces");
            _dirB.AddComponent<RuntimeSurfaceDirector>();

            _robot = NewRobot(EnemyKind.Bruiser);
            _robot.AddComponent<RobotRig>();
            yield return null;
            yield return null;   // both directors sweep every Update

            foreach (var r in Model(_robot).GetComponentsInChildren<MeshRenderer>())
            {
                Assert.IsNull(r.GetComponent<CharacterSkin>(),
                    $"CharacterSkinDirector claimed '{r.name}'. It will rewrite that part every LateUpdate " +
                    "and the eye tell becomes a coin toss on script order.");
                Assert.IsNull(r.GetComponent<SurfaceSkinned>(),
                    $"RuntimeSurfaceDirector repainted '{r.name}' — it classifies by shape, so the robot " +
                    "is now made of paving stones.");
            }
        }

        // ------------------------------------------------------------------ the two are opposites

        /// <summary>
        /// The whole point: a rusher and a bruiser want opposite responses, so they must read apart at a
        /// glance. The bruiser is WIDE and low; the rusher is narrow and leggy. And neither out-sizes Max
        /// (1.83 m) — a swarm of things bigger than the player reads as terrain, not enemies (YT-74).
        /// </summary>
        [UnityTest]
        public IEnumerator TheTwoKinds_HaveOppositeSilhouettes()
        {
            var rusher = NewRobot(EnemyKind.Rusher);
            rusher.AddComponent<RobotRig>();
            var bruiser = NewRobot(EnemyKind.Bruiser);
            bruiser.AddComponent<RobotRig>();
            yield return null;

            try
            {
                Bounds rb = ModelBounds(rusher);
                Bounds bb = ModelBounds(bruiser);

                Assert.Greater(bb.size.x, rb.size.x * 1.3f,
                    "the bruiser is not obviously wider than the rusher. Its whole read is 'heavy, and " +
                    "it will not go away'; the rusher's is 'quick'.");

                float rusherAspect = rb.size.y / rb.size.x;   // tall and narrow
                float bruiserAspect = bb.size.y / bb.size.x;  // low and wide
                Assert.Greater(rusherAspect, bruiserAspect,
                    "the two have the same proportions. Kite one and spend three seconds of spray on the " +
                    "other — a player who cannot tell them apart cannot play the fight.");

                foreach (var (name, model) in new[] { ("rusher", rusher), ("bruiser", bruiser) })
                    Assert.Less(ModelBounds(model).size.y, 1.83f,
                        $"the {name} out-sizes Max. A swarm of things bigger than the player reads as a " +
                        "moving wall, not as enemies (YT-74).");
            }
            finally
            {
                Object.Destroy(rusher);
                Object.Destroy(bruiser);
            }
        }

        // ------------------------------------------------------------------ the eye reads

        /// <summary>At rest the eye burns gold — lit, and clearly not the warn orange, so an awake robot
        /// does not look like one that is about to lunge. This proves the rig actually drives the eye's
        /// property block, which is where the boss's tell died before it reached the screen.</summary>
        [UnityTest]
        public IEnumerator TheEyeIsLitAndGold_AtRest()
        {
            _robot = NewRobot(EnemyKind.Rusher);
            _robot.AddComponent<RobotRig>();
            yield return null;

            var eye = Model(_robot).GetComponentsInChildren<Transform>().First(t => t.name == "Eye")
                                   .GetComponent<MeshRenderer>();
            var mpb = new MaterialPropertyBlock();
            eye.GetPropertyBlock(mpb);
            Color c = mpb.GetColor("_BaseColor");

            Assert.Greater(c.r + c.g, 1.0f, "the eye is dark — a robot with no lit eye has no tell.");
            Assert.Less(c.b, c.r, "the resting eye is not gold.");
        }
    }
}
