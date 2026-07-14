using System.Collections;
using System.Linq;
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

        private Transform Find(string name) =>
            _rigHost.GetComponentsInChildren<Transform>(includeInactive: true)
                    .FirstOrDefault(t => t.name == name);

        // ------------------------------------------------------------------ he is a person

        /// <summary>
        /// The ticket, in one assertion: the hero of the game is not a blob.
        ///
        /// Named parts, because a renderer count alone would pass on twenty-five cubes in a heap. Each
        /// of these is load-bearing for the silhouette at 72°: the HOOD is what makes two shoulders
        /// read as a hoodie from overhead, the PACK and the SPANNER are the lumps that stop him being
        /// symmetrical, the HAIR is the single biggest thing the camera can see of him, and the TANK is
        /// what makes the thing in his hands a WATER gun rather than a gun.
        /// </summary>
        [UnityTest]
        public IEnumerator MaxIsAKid_NotACapsule()
        {
            yield return InstallRig();

            foreach (string part in new[] { "Chest", "Waist", "Hood", "Collar", "Shoulder", "Hair",
                                            "Skull", "Pack", "Spanner", "Belt", "Gun", "Tank",
                                            "Barrel", "Sole" })
            {
                Assert.IsNotNull(Find(part),
                    $"Max has no '{part}'. Every one of these carries part of his silhouette at the " +
                    "only camera angle this game has.");
            }

            var renderers = _rigHost.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            Assert.That(renderers.Length, Is.GreaterThan(20),
                "Max is made of almost nothing. He is supposed to be a kid, not a better capsule.");
        }

        /// <summary>
        /// The goggles are pushed up on his forehead, and they are the only thing on him that is small
        /// and bright. On a body seen from almost overhead, the forehead is the only part of a face
        /// there is — so this is the whole of Max's face, and it had better be at the front and at the
        /// top of his head, not buried in his hair.
        /// </summary>
        [UnityTest]
        public IEnumerator TheGogglesAreOnHisForehead()
        {
            yield return InstallRig();

            var skull = Find("Skull");
            var lens = Find("LensL");

            Assert.IsNotNull(lens, "Max has no goggles. He is a tinkerer; they are his face.");

            Assert.That(lens.position.z, Is.GreaterThan(skull.position.z),
                "The goggles are behind the centre of his head. They are supposed to be on his brow.");
            Assert.That(lens.position.y, Is.GreaterThan(skull.position.y),
                "The goggles have slipped below the middle of his face. They are PUSHED UP (GDD §9).");
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

        /// <summary>
        /// Both hands are on the gadget, and the sleeves reach them.
        ///
        /// The sleeves have no pose of their own — they are stretched between the shoulder and whichever
        /// hand is where, every frame — and this is what proves that actually connects. A hand floating
        /// next to its own gun is the single most obvious way a rig like this breaks, and it breaks
        /// silently: the gadget still points the right way, so nothing else in the game notices.
        /// </summary>
        [UnityTest]
        public IEnumerator HisHandsNeverLeaveTheGadget()
        {
            yield return InstallRig();
            yield return null;

            var gun = Find("Gun");

            foreach (string hand in new[] { "HandL", "HandR" })
            {
                var h = Find(hand);
                Assert.IsNotNull(h, $"Max has no '{hand}'.");
                Assert.That(Vector3.Distance(h.position, gun.position), Is.LessThan(0.35f),
                    $"'{hand}' is not on the gadget. Max is holding it with nothing.");
            }

            // And the sleeve spans shoulder to hand — its far end lands ON the hand it is reaching for.
            foreach (var (arm, hand) in new[] { ("ArmL", "HandL"), ("ArmR", "HandR") })
            {
                var a = Find(arm);
                var h = Find(hand);

                // The sleeve is a box scaled along its own Y, centred between the two — so its ends are
                // half a length either side of its centre. One of them has to be the hand.
                float half = a.lossyScale.y * 0.5f;
                float reach = Vector3.Distance(a.position, h.position);

                Assert.That(reach, Is.EqualTo(half).Within(0.05f),
                    $"'{arm}' does not reach '{hand}': the sleeve is {half * 2f:F2} m long but the hand " +
                    $"is {reach:F2} m from its centre. The arm has come off the gun.");
            }
        }

        /// <summary>He starts at the hip. The gadget is only presented while the aim stick is actually
        /// pushed, and an untouched controller is the state the game spends most of its time in.</summary>
        [UnityTest]
        public IEnumerator HeCarriesTheGadgetAtTheHipUntilHeAims()
        {
            yield return InstallRig();
            yield return null;

            Assert.That(Rig.AimPose, Is.EqualTo(0f).Within(0.05f),
                "Max is presenting the gadget with nobody aiming it.");

            var gun = Find("Gun");
            Assert.That(gun.position.y, Is.LessThan(MaxRig.BarrelHeight(1f)),
                "The gadget is already up at firing height while Max is just standing there.");
        }
    }
}
