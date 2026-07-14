using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// Big Bermuda is a machine now, and the machine says what the fight is doing (YT-90).
    ///
    /// PlayMode, because every claim here is about time: the reel spins up over a wind-up, the eyes
    /// change colour on a phase, the thing dies over half a second. None of that is a property you can
    /// read off a struct.
    /// </summary>
    public sealed class BigBermudaRigPlayTests
    {
        private GameObject _boss;
        private GameObject _player;
        private GameObject _rigHost;

        private BigBermudaBoss Boss => _boss.GetComponent<BigBermudaBoss>();
        private BigBermudaRig Rig => _rigHost.GetComponent<BigBermudaRig>();

        [SetUp]
        public void SetUp()
        {
            // The boss exactly as Stage27BossScaffold bakes it into Backyard_Slice: a cube on a
            // CharacterController, scaled 3.5 x 3 x 3.5.
            _boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boss.name = "Big Bermuda";
            _boss.transform.position = new Vector3(0f, 2f, 33f);
            _boss.transform.localScale = new Vector3(3.5f, 3f, 3.5f);
            _boss.AddComponent<BigBermudaBoss>();

            // The boss's brain does not tick without something to chase — TickFight returns early on a
            // null target — so the fight never leaves Reposition and no tell would ever fire.
            _player = new GameObject("Max") { tag = "Player" };
            _player.transform.position = new Vector3(0f, 0f, 20f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_rigHost != null) Object.Destroy(_rigHost);
            if (_boss != null) Object.Destroy(_boss);
            if (_player != null) Object.Destroy(_player);
        }

        /// <summary>Self-installs at AfterSceneLoad in the game; that moment is long gone inside a test,
        /// so stand it up by hand — which is the point of the code-driven rule: it can be.</summary>
        private IEnumerator InstallRig()
        {
            _rigHost = new GameObject("BigBermudaRig");
            _rigHost.AddComponent<BigBermudaRig>();
            yield return null;
        }

        /// <summary>Kill the factory. That is what wakes it (BigBermudaBoss.OnFactoryDestroyed).</summary>
        private IEnumerator Wake()
        {
            HudSignals.EmitFactoryDestroyed(Vector3.zero);

            // Through the 1.6 s intro and the 0.9 s wake flicker, into the fight proper.
            float t = 0f;
            while (t < 2.2f) { t += Time.deltaTime; yield return null; }
        }

        private static void Hit(BigBermudaBoss boss, float amount) =>
            boss.TakeDamage(new DamageInfo(amount, Vector3.zero, Vector3.forward, Team.Player));

        /// <summary>
        /// Run until the fight enters <paramref name="action"/>, hold there, and report the colour the
        /// eyes had SETTLED on.
        ///
        /// Settled, and not the brightest frame seen: the eyes ease into a phase's colour, so the first
        /// frames of a wind-up are still mostly the green it was idling in — and that green is BRIGHTER
        /// than the orange it is heading for (luma 0.74 vs 0.52). Sampling the brightest frame of a
        /// wind-up would faithfully report the previous phase.
        /// </summary>
        private IEnumerator SampleDuring(BossAction action, System.Action<Color> report, float timeout = 8f)
        {
            var boss = Boss;
            float t = 0f;

            while (boss.Action != action && t < timeout) { t += Time.deltaTime; yield return null; }
            Assert.Less(t, timeout, $"the fight never reached {action} — the tell could not be sampled.");

            // 0.5 s at an eye response of 9/s converges to within 1%, and every phase in the cycle —
            // even a wind-up, even enraged — lasts longer than that.
            Color settled = Rig.EyeColor;
            float held = 0f;
            while (boss.Action == action && held < 0.5f)
            {
                held += Time.deltaTime;
                settled = Rig.EyeColor;
                yield return null;
            }

            report(settled);
        }

        private static float Luma(Color c) => c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;

        // ------------------------------------------------------------------ it is not a cube

        /// <summary>
        /// The whole ticket, in one assertion: the boss is a MACHINE, and the machine has the parts a
        /// mower has. It had been a tinted cube since YT-27 — the end-cap of the Backyard and the story
        /// beat of the run, and the thing it most resembled was a crate.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBossIsAPossessedMower_NotACube()
        {
            yield return InstallRig();

            var parts = _rigHost.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(parts.Length, 10,
                "Big Bermuda has almost no parts. A boss is the biggest, meanest silhouette on the " +
                "field; a handful of boxes is still a crate.");

            foreach (var required in new[] { "Deck", "Hood", "Hopper", "Grip", "Cowl", "Reel", "EyeL", "EyeR" })
            {
                Assert.IsTrue(_rigHost.GetComponentsInChildren<Transform>().Any(t => t.name == required),
                    $"the mower has no '{required}'. The deck, the hopper and the HANDLE are what make " +
                    "this a lawnmower rather than a bulldozer; the eyes are what make it a character.");
            }

            Assert.AreEqual(4, _rigHost.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("Wheel")),
                "a mower with the wrong number of wheels.");

            // The greybox is gone — but its COLLIDERS are not. Gameplay still points at that object.
            Assert.IsFalse(_boss.GetComponent<MeshRenderer>().enabled,
                "the placeholder cube is still being drawn, inside the machine that replaced it.");
            Assert.IsNotNull(_boss.GetComponent<CharacterController>(),
                "the boss lost the controller it is hit and collided through.");
        }

        /// <summary>
        /// A primitive from CreatePrimitive carries Unity's BUILT-IN default material, which has no URP
        /// subshader and draws MAGENTA in a player build while looking perfectly correct in the editor.
        /// It is how the boss's damage zones shipped (YT-61) and how the factory's core shipped (YT-38),
        /// and this machine is twenty-two primitives.
        /// </summary>
        [UnityTest]
        public IEnumerator NoPartOfTheMachineShipsMagenta()
        {
            yield return InstallRig();
            yield return null;   // let both scene directors take a sweep at it

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

        /// <summary>
        /// Nothing on the machine may be shot or walked into. The boss's own controller and box are the
        /// hitbox; a collider on the handle would silently eat water aimed at the boss, and Max would
        /// bump into a grass catcher gameplay does not believe is there.
        /// </summary>
        [UnityTest]
        public IEnumerator NoPartOfTheMachineCanBeShot()
        {
            yield return InstallRig();

            var colliders = _rigHost.GetComponentsInChildren<Collider>(includeInactive: true);
            Assert.IsEmpty(colliders,
                $"the machine is carrying {colliders.Length} collider(s). Every one of them is a shot " +
                "that never reaches the boss.");
        }

        /// <summary>
        /// The machine belongs to this rig and to nothing else.
        ///
        /// This is the bug the ticket is really about. Two scene-wide sweeps re-material anything they
        /// recognise — CharacterSkinDirector claims every renderer under an IDamageable, and
        /// RuntimeSurfaceDirector repaints everything else BY SHAPE. If either one gets hold of a part
        /// of this machine, that part gets a SECOND writer on its property block, and script order picks
        /// which of the two the player sees. That is precisely how the boss's wind-up tell died.
        /// </summary>
        [UnityTest]
        public IEnumerator NoSceneDirectorClaimsTheMachine()
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
                        $"CharacterSkinDirector claimed '{r.name}'. It will now rewrite that part's " +
                        "colour every LateUpdate, and the wind-up tell this rig writes in LateUpdate " +
                        "will be a coin toss on script order — which is how the tell died in the first place.");

                    Assert.IsNull(r.GetComponent<SurfaceSkinned>(),
                        $"RuntimeSurfaceDirector repainted '{r.name}'. It classifies by SHAPE: the deck " +
                        "is a flat slab, so the boss is now made of paving stones.");
                }
            }
            finally
            {
                Object.Destroy(skins.gameObject);
                Object.Destroy(surfaces.gameObject);
            }
        }

        // ------------------------------------------------------------------ reading the fight off it

        /// <summary>It stands beyond the gate from the first frame of the run. It should look dead, not
        /// switched off — and the moment the factory dies, it should open its eyes.</summary>
        [UnityTest]
        public IEnumerator TheMachineSleeps_UntilTheFactoryDies()
        {
            yield return InstallRig();
            yield return null;

            Assert.Less(Luma(Rig.EyeColor), 0.02f,
                "its eyes are already lit and the factory is still standing. The wake-up is the beat " +
                "that makes the boss an event.");
            Assert.IsFalse(Rig.Running, "it is running before anything has woken it.");

            yield return Wake();

            Assert.IsTrue(Rig.Running, "the factory is dead and the boss has not woken up.");
            Assert.Greater(Luma(Rig.EyeColor), 0.15f, "it woke up with its eyes still out.");
        }

        /// <summary>
        /// THE REGRESSION. The charge wind-up has to reach the screen.
        ///
        /// BigBermudaBoss.SetTell has been writing an orange wind-up colour into the boss's property
        /// block on every phase change since YT-27 — and CharacterSkin.LateUpdate has been overwriting
        /// that same block, every frame, with the flat near-black body colour. Update writes; LateUpdate
        /// overwrites. The single most important read in the fight — "it is about to charge, MOVE" — has
        /// never once rendered a pixel.
        ///
        /// Warm, and clearly not the green it idles in. That is the same orange every telegraph in the
        /// game already uses, so the player has been taught this word by every robot in the yard.
        /// </summary>
        [UnityTest]
        public IEnumerator TheChargeWindUp_ActuallyReachesTheScreen()
        {
            yield return InstallRig();
            yield return Wake();

            Color idle = Color.black;
            yield return SampleDuring(BossAction.Reposition, c => idle = c);

            Color windup = Color.black;
            yield return SampleDuring(BossAction.ChargeWindup, c => windup = c);

            Assert.Greater(windup.r, windup.g + 0.25f,
                "the wind-up is not warm. It is the one telegraph in the fight that costs you health to " +
                "miss, and it has never rendered a pixel — CharacterSkin overwrote it every frame.");
            Assert.Greater(windup.r, windup.b + 0.4f, "the wind-up is not warm.");

            Assert.Greater(windup.r - idle.r, 0.3f,
                "the machine looks the same winding up as it does idling. A telegraph you cannot tell " +
                "apart from doing nothing is not a telegraph.");

            Assert.Greater(Rig.ReelSpin, 400f,
                "the cutting reel is barely turning as it commits to a charge. The spin-up IS the " +
                "threat: a blade at speed is the one part of this machine you must not be in front of.");
        }

        /// <summary>
        /// Phase 2 has to be legible BETWEEN attacks, not only in the half-second it is committing to
        /// one. The player needs to know the fight got worse while they are deciding what to do about it.
        /// </summary>
        [UnityTest]
        public IEnumerator PhaseTwo_IsLegibleEvenWhenItIsNotAttacking()
        {
            yield return InstallRig();
            yield return Wake();

            Color idle = Color.black;
            yield return SampleDuring(BossAction.Reposition, c => idle = c);
            Assert.Greater(idle.g, idle.r, "it is not idling in its own colour.");

            Hit(Boss, 700f);   // 1200 HP, enrage at 50%

            // A frame, because Enraged is recomputed by the brain's Tick and the brain ticks in Update.
            // Asserting on it in the same frame as the hit asks the fight a question it has not been
            // given a chance to answer.
            yield return null;
            Assert.IsTrue(Boss.Enraged, "the boss did not enrage on a hit that took it past half health.");

            Color enragedIdle = Color.black;
            yield return SampleDuring(BossAction.Reposition, c => enragedIdle = c);

            Assert.Greater(enragedIdle.r, enragedIdle.g + 0.3f,
                "it idles the same colour in phase 2 as it did in phase 1. 'It got worse' is something " +
                "the player has to be able to see while they are still deciding what to do about it.");
        }

        /// <summary>
        /// It has to actually die on screen.
        ///
        /// The boss deactivates its own GameObject on the frame it dies, and the result screen freezes
        /// the game (timeScale = 0) on that same frame. This machine is not parented to the boss, so it
        /// outlives it by half a second and comes apart — on UNSCALED time, or it would be frozen solid
        /// before it moved, which is why YT-55's defeat sequence runs unscaled too.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMachineComesApart_WhenTheBossDies()
        {
            yield return InstallRig();
            yield return Wake();

            HudSignals.EmitBossDefeated();
            Assert.IsFalse(Rig.Running, "the boss is dead and the machine is still running.");

            // The game is frozen from the frame the boss dies. If the death throe were on scaled time,
            // this loop would spin forever and the machine would hang in the air over its own wreckage.
            float scale = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                float t = 0f;
                while (_rigHost.activeSelf && t < 1.5f) { t += Time.unscaledDeltaTime; yield return null; }

                Assert.IsFalse(_rigHost.activeSelf,
                    "the machine is still standing there half a second after it died — and the game is " +
                    "paused, so it never will not be. The death throe is on scaled time.");
            }
            finally
            {
                Time.timeScale = scale;
            }
        }
    }
}
