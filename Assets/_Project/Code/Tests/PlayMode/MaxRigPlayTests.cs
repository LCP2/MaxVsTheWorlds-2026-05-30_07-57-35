using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Max is a kid now, and not a capsule (YT-95).
    ///
    /// PlayMode, because the claims that matter are claims about a rig standing in a live scene with
    /// the game's two material directors sweeping over it every frame. None of that is a property you
    /// can read off a struct — and the way this rig dies is precisely by being quietly repainted by
    /// something else, which only happens once the game is running.
    /// </summary>
    public sealed class MaxRigPlayTests
    {
        private GameObject _max;
        private GameObject _rigHost;

        private MaxRig Rig => _rigHost.GetComponent<MaxRig>();

        [SetUp]
        public void SetUp()
        {
            // Max exactly as Stage34PlayerScaffold bakes him into Backyard_Slice: a capsule on a
            // CharacterController, tagged Player, with a cube "Nose" stuck on the front so you could
            // tell which way the capsule was pointing.
            _max = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _max.name = "Max (Greybox)";
            _max.tag = "Player";
            _max.transform.position = new Vector3(0f, 1f, -3f);

            var cc = _max.AddComponent<CharacterController>();
            cc.height = EnemyArchetype.PlayerHeight;
            cc.radius = EnemyArchetype.PlayerRadius;

            _max.AddComponent<PlayerController>();
            _max.AddComponent<PlayerHealth>();

            var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nose.name = "Nose";
            nose.transform.SetParent(_max.transform, worldPositionStays: false);
            nose.transform.localPosition = new Vector3(0f, 0.4f, 0.55f);
            nose.transform.localScale = new Vector3(0.25f, 0.25f, 0.6f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rigHost != null) Object.Destroy(_rigHost);
            if (_max != null) Object.Destroy(_max);
        }

        /// <summary>Self-installs at AfterSceneLoad in the game; that moment is long gone inside a test,
        /// so stand it up by hand — which is the point of the code-driven rule: it can be.</summary>
        private IEnumerator InstallRig()
        {
            _rigHost = new GameObject("MaxRig");
            _rigHost.AddComponent<MaxRig>();
            yield return null;
        }

        // ------------------------------------------------------------------ he is a person

        /// <summary>
        /// The ticket, in one assertion: the hero of the game is not a blob.
        ///
        /// MV-451: the body is generated geometry now (<see cref="MaxBody"/>), built from parts that
        /// are all generically named "Part" (see <see cref="CharacterPart"/>) — there is no "Chest" or
        /// "Hood" to look up by name any more. A renderer count is what is left to assert without
        /// hand-editing the approved design source's own coordinates to expose named landmarks it does
        /// not return.
        /// </summary>
        [UnityTest]
        public IEnumerator MaxIsAKid_NotACapsule()
        {
            yield return InstallRig();

            var renderers = _rigHost.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            Assert.That(renderers.Length, Is.GreaterThan(20),
                "Max is made of almost nothing. He is supposed to be a kid, not a better capsule.");
        }

        // ------------------------------------------------------------------ he fits the game

        /// <summary>
        /// He is the biggest thing in the yard that is not the boss, and he still fits inside the
        /// hitbox the robots are actually hitting.
        ///
        /// The rule (YT-74, and it is written into EnemyArchetype): nothing in the swarm may out-size
        /// Max. The rusher stands 1.4 m and the bruiser 1.15 m, and a hero who has just been rebuilt as
        /// a realistically-proportioned twelve-year-old would quietly become the SMALLEST actor on the
        /// field — which is the exact readability failure this ticket exists to fix, arriving from the
        /// other direction.
        /// </summary>
        [UnityTest]
        public IEnumerator HeOutSizesTheSwarm_AndStillFitsHisOwnHitbox()
        {
            yield return InstallRig();
            yield return null;

            var renderers = _rigHost.GetComponentsInChildren<MeshRenderer>();
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            float height = b.max.y;

            Assert.That(height, Is.GreaterThan(EnemyArchetype.Rusher.ColliderHeight),
                $"Max stands {height:F2} m and the rusher stands {EnemyArchetype.Rusher.ColliderHeight:F2} m. " +
                "Nothing in the swarm may out-size Max.");

            Assert.That(height, Is.LessThanOrEqualTo(EnemyArchetype.PlayerHeight),
                $"Max stands {height:F2} m and his own capsule is {EnemyArchetype.PlayerHeight:F2} m. " +
                "He is sticking out of the top of the thing the robots collide with.");

            // And his feet are ON the lawn, not hovering over it or sunk into it. The ground ring
            // (YT-85) is drawn flat at y = 0 and a kid floating over his own ring is worse than a
            // capsule.
            Assert.That(b.min.y, Is.EqualTo(0f).Within(0.06f),
                $"Max's lowest point is at y = {b.min.y:F3}. His shoes are not on the ground.");
        }

        /// <summary>The greybox goes. Its COLLIDERS stay — the CharacterController is what the robots
        /// hit and what Max walks the yard with, and only the visual is this ticket's to change.</summary>
        [UnityTest]
        public IEnumerator TheCapsuleAndItsNoseAreGone()
        {
            yield return InstallRig();

            foreach (var r in _max.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                Assert.IsFalse(r.enabled,
                    $"the greybox '{r.name}' is still drawing. Max is now standing inside his own " +
                    "placeholder.");
            }

            Assert.IsNotNull(_max.GetComponent<CharacterController>(),
                "the rig took Max's CharacterController with it. He is no longer a thing that can be " +
                "hit or that can walk.");
        }

        // ------------------------------------------------------------------ he belongs to this rig

        /// <summary>
        /// A primitive from CreatePrimitive carries Unity's BUILT-IN default material, which has no URP
        /// subshader and draws MAGENTA in a player build while looking perfectly correct in the editor.
        /// It is how the factory's core shipped (YT-38) and how the boss's damage zones shipped (YT-61),
        /// and Max is thirty-odd primitives.
        /// </summary>
        [UnityTest]
        public IEnumerator NoPartOfMaxShipsMagenta()
        {
            yield return InstallRig();
            yield return null;   // let both scene directors take a sweep at him

            foreach (var r in _rigHost.GetComponentsInChildren<MeshRenderer>(includeInactive: true))
            {
                Assert.IsNotNull(r.sharedMaterial, $"'{r.name}' has no material at all — it draws nothing.");

                string shader = r.sharedMaterial.shader.name;
                Assert.That(shader,
                    Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds").Or.StartWith("Sprites"),
                    $"'{r.name}' is wearing '{shader}'. A primitive's default material has no URP " +
                    "subshader: this part is magenta in the build and correct in the editor.");
            }
        }

        /// <summary>Nothing on Max may be shot or walked into. His CharacterController is the hitbox;
        /// a collider on the backpack would silently eat water a player aimed past him, and the robots
        /// would bump into a spanner that gameplay does not believe is there.</summary>
        [UnityTest]
        public IEnumerator NoPartOfMaxCanBeShot()
        {
            yield return InstallRig();

            var colliders = _rigHost.GetComponentsInChildren<Collider>(includeInactive: true);
            Assert.IsEmpty(colliders,
                $"Max is carrying {colliders.Length} extra collider(s). Every one of them is a shot " +
                "that never reaches whatever the player was aiming at.");
        }

        /// <summary>
        /// THE TEST THAT PROTECTS THE RIG.
        ///
        /// Max is an IDamageable, and CharacterSkinDirector claims every MeshRenderer under one and
        /// repaints it flat hoodie-red in LateUpdate. If this rig were parented to him — the obvious
        /// thing to do, and the thing a future change will absently try — his hair, his jeans, his
        /// skin, and the water in his tank would all be claimed and all turn the same shade of orange,
        /// one frame after they were built. RuntimeSurfaceDirector would separately repaint the
        /// backpack as a paving stone, having classified it by shape.
        ///
        /// The rig therefore hangs off no damageable at all and follows Max instead. This is the
        /// assertion that stops anyone quietly undoing that.
        /// </summary>
        [UnityTest]
        public IEnumerator NoSceneDirectorClaimsMax()
        {
            // Both directors, running, exactly as they do in the game.
            var skins = new GameObject("CharacterSkins").AddComponent<CharacterSkinDirector>();
            var surfaces = new GameObject("RuntimeSurfaces").AddComponent<RuntimeSurfaceDirector>();

            try
            {
                yield return InstallRig();
                yield return null;
                yield return null;   // both sweep every Update; two frames is more than enough

                foreach (var r in _rigHost.GetComponentsInChildren<MeshRenderer>())
                {
                    Assert.IsNull(r.GetComponent<CharacterSkin>(),
                        $"CharacterSkinDirector claimed '{r.name}'. It will now rewrite that part flat " +
                        "hoodie-red every LateUpdate — Max's hair, his skin and the water in his tank " +
                        "all become the same colour as his jumper.");

                    Assert.IsNull(r.GetComponent<SurfaceSkinned>(),
                        $"RuntimeSurfaceDirector claimed '{r.name}'. It classifies by SHAPE, so it has " +
                        "just decided part of a twelve-year-old is a paving stone.");
                }
            }
            finally
            {
                Object.Destroy(skins.gameObject);
                Object.Destroy(surfaces.gameObject);
            }
        }

        // ------------------------------------------------------------------ he moves like a person

        /// <summary>He stands on the lawn under his own capsule, facing where Max faces. His transform's
        /// y is his capsule's CENTRE, a metre up, so a rig that copied it would float.</summary>
        [UnityTest]
        public IEnumerator HeStandsWhereMaxStands()
        {
            yield return InstallRig();

            // PARK HIM FIRST. Two things will otherwise move Max out from under this test before it can
            // read him, and neither has anything to do with the rig: a CharacterController overrides a
            // direct write to its own transform on its next Move(), and PlayerController turns him back
            // toward his facing every Update at 720°/s. Both go off — the claim here is that the rig
            // follows Max, not that Unity's controller can be teleported.
            _max.GetComponent<CharacterController>().enabled = false;
            _max.GetComponent<PlayerController>().enabled = false;

            _max.transform.SetPositionAndRotation(new Vector3(7f, 1f, -12f), Quaternion.Euler(0f, 143f, 0f));
            yield return null;

            Assert.That(_rigHost.transform.position.x, Is.EqualTo(7f).Within(0.01f));
            Assert.That(_rigHost.transform.position.z, Is.EqualTo(-12f).Within(0.01f));
            Assert.That(_rigHost.transform.eulerAngles.y, Is.EqualTo(143f).Within(0.5f),
                "Max is not facing the way the kid is facing.");

            // And he stands ON the lawn. Max's own origin is his capsule's CENTRE — a metre up, and it
            // drifts with gravity and the controller's skin width — so a rig that copied his y would
            // float a metre over his own ground ring.
            Assert.That(_rigHost.transform.position.y, Is.EqualTo(0f).Within(0.01f),
                $"Max is standing at y = {_rigHost.transform.position.y:F2}. The rig took his capsule's " +
                "centre for his feet.");
            Assert.That(_max.transform.position.y, Is.GreaterThan(0.5f),
                "the fixture is wrong: Max's origin is supposed to be a metre off the ground, so this " +
                "test is no longer proving the rig ignores it.");
        }

        // MV-451 FLAG FOR LEE: HisHandsNeverLeaveTheGadget and the gun-position half of
        // HeCarriesTheGadgetAtTheHipUntilHeAims are removed here, not adapted. MaxBody.Build bakes the
        // gadget and both arms into ONE fused static mesh under a single root with no "Gun"/"ArmL"/
        // "ArmR"/"HandL"/"HandR" transforms of its own to find or move — by design (see MaxBody's own
        // "elbows are explicit" and "blaster is off the midline" doc comments, which describe a single
        // authored pose, not a runtime-posable rig). MaxRig.TickGadget/PoseArm still compute _aim and
        // the shoulder/hand math every frame (AimPose, BarrelHeight and MaxRigTests all still hold),
        // but per INTEGRATION-v2.md's own instruction there is nothing left for that computation to
        // visually drive: the gadget no longer visibly rises when Max aims. That is a real behaviour
        // change from the class doc's "you can see the gun come up before a drop of water leaves it"
        // and this ticket's own scope (rename, delete-and-delegate, wheels) has no coordinate to fix it
        // with — extending MaxBody to expose a posable gun root is a design call for MV-453 (Max detail
        // pass), not a fidelity bug this ticket can hand-edit its way out of.

        /// <summary>He starts at the hip. The gadget is only presented while the aim stick is actually
        /// pushed, and an untouched controller is the state the game spends most of its time in. The
        /// visible gun-position half of this claim is gone — see the flag above.</summary>
        [UnityTest]
        public IEnumerator HeCarriesTheGadgetAtTheHipUntilHeAims()
        {
            yield return InstallRig();
            yield return null;

            Assert.That(Rig.AimPose, Is.EqualTo(0f).Within(0.05f),
                "Max is presenting the gadget with nobody aiming it.");
        }
    }
}
