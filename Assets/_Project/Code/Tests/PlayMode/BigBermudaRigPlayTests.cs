using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The Backyard boss is the otherworldly BROOD-HULK now (YT-150) — an alien chitin carrier — and the
    /// body says what the fight is doing.
    ///
    /// PlayMode, because every claim here is about time: it builds the coil over a wind-up, the core
    /// changes colour on a phase, the side hatches vent on a cadence, the thing dies over half a second.
    /// None of that is a property you can read off a struct — which is the whole reason the wind-up tell
    /// could be dead for the life of the fight and no EditMode test noticed.
    /// </summary>
    public sealed class BigBermudaRigPlayTests
    {
        private GameObject _boss;
        private GameObject _player;
        private GameObject _rigHost;
        private GameObject _hutch;

        private BigBermudaBoss Boss => _boss.GetComponent<BigBermudaBoss>();
        private BigBermudaRig Rig => _rigHost.GetComponent<BigBermudaRig>();

        [SetUp]
        public void SetUp()
        {
            // The boss exactly as Stage27BossScaffold bakes it into Backyard_Slice: a cube on a
            // CharacterController, scaled 3.5 x 3 x 3.5. The rig replaces the cube's VISUAL only.
            _boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boss.name = "Big Bermuda";
            _boss.transform.position = new Vector3(0f, 2f, 33f);
            _boss.transform.localScale = new Vector3(3.5f, 3f, 3.5f);
            _boss.AddComponent<BigBermudaBoss>();

            // The brain does not tick without something to chase — TickFight returns early on a null
            // target — so the fight never leaves Reposition and no tell would ever fire.
            _player = new GameObject("Max") { tag = "Player" };
            _player.transform.position = new Vector3(0f, 0f, 20f);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in new[] { _rigHost, _boss, _player, _hutch })
                if (go != null) Object.Destroy(go);
        }

        /// <summary>Self-installs at AfterSceneLoad in the game; that moment is long gone inside a test,
        /// so stand it up by hand — which is the point of the code-driven rule: it can be.</summary>
        private IEnumerator InstallRig()
        {
            _rigHost = new GameObject("BigBermudaRig");
            _rigHost.AddComponent<BigBermudaRig>();
            yield return null;
        }

        /// <summary>
        /// Kill the yard's factories — the LAST of them, since YT-92, is what wakes the boss. The census
        /// is wiped first because it is static and a test run is one process.
        /// </summary>
        private IEnumerator Wake()
        {
            FactoryCensus.Reset();

            _hutch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutch.name = "Mower Hutch";
            _hutch.transform.position = new Vector3(0f, 1f, 0f);
            var hutch = _hutch.AddComponent<MowerHutch>();
            yield return null;

            hutch.TakeDamage(new DamageInfo(100000f, _hutch.transform.position, Vector3.forward, Team.Player));

            // Through the 1.6 s intro and the 0.9 s wake flicker, into the fight proper.
            float t = 0f;
            while (t < 2.2f) { t += Time.deltaTime; yield return null; }
        }

        private static void Hit(BigBermudaBoss boss, float amount) =>
            boss.TakeDamage(new DamageInfo(amount, Vector3.zero, Vector3.forward, Team.Player));

        /// <summary>
        /// Run until the fight enters <paramref name="action"/>, hold there, and report the colour the
        /// ports had SETTLED on — not the brightest frame seen, because the ports ease into a phase and
        /// the green they idle in is brighter than the orange they head for (luma 0.74 vs 0.52).
        /// </summary>
        private IEnumerator SampleDuring(BossAction action, System.Action<Color> report, float timeout = 8f)
        {
            var boss = Boss;
            float t = 0f;

            while (boss.Action != action && t < timeout) { t += Time.deltaTime; yield return null; }
            Assert.Less(t, timeout, $"the fight never reached {action} — the tell could not be sampled.");

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

        private bool Has(string partName) =>
            _rigHost.GetComponentsInChildren<Transform>().Any(t => t.name == partName);

        // ------------------------------------------------------------------ it is the Brood-Hulk, not a cube

        /// <summary>
        /// The whole ticket, in one assertion: the boss is the otherworldly BROOD-HULK, and it has the
        /// parts a chitin carrier has. It was a possessed boiler before (YT-114), a mower before that
        /// (YT-90) and a tinted cube before that (YT-27); Lee's round-2 pick (YT-150) re-skins the
        /// silhouette off the alien carrier concept.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBossIsTheBroodHulk_NotACube()
        {
            yield return InstallRig();

            var parts = _rigHost.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(parts.Length, 20,
                "the Brood-Hulk has almost no parts. A boss is the biggest, meanest silhouette on the " +
                "field; a handful of boxes is still a crate.");

            // Thorax + head read as the alien body; the ocular core, glands and spine-seam are the face
            // and the tell; the two hatches + brood cavity are the carrier's signature — the swarm doors.
            foreach (var required in new[]
                     { "Thorax", "Head", "OcularCore", "GlandL", "GlandR", "SpineSeam",
                       "HatchL", "HatchR", "BroodCore" })
            {
                Assert.IsTrue(Has(required),
                    $"the Brood-Hulk has no '{required}'. The thorax and shell make it a carrier, the " +
                    "hatches make it disgorge, and the core makes it a character.");
            }

            yield return null;
        }

        /// <summary>
        /// The signature of the pick (YT-150): functional LEFT and RIGHT side hatches. This asserts they
        /// are real, hinged, MIRRORED parts — not one door, not painted seams — because YT-157 flings the
        /// swarm out of BOTH sides and the open shell is the spawn telegraph.
        /// </summary>
        [UnityTest]
        public IEnumerator ItHasTwoMirroredSideHatches()
        {
            yield return InstallRig();

            Assert.IsTrue(Has("HatchL") && Has("HatchR"),
                "a carrier with one door disgorges to one side. The spawn attack (YT-157) uses both flanks.");

            // The hatch pivots sit on the spine (x ≈ 0) so the shells hinge open OUT to the sides. Their
            // shells are offset to opposite flanks — that mirror is what reads as 'it split down the back'.
            var shellL = _rigHost.GetComponentsInChildren<Transform>().First(t => t.name == "HatchLShell");
            var shellR = _rigHost.GetComponentsInChildren<Transform>().First(t => t.name == "HatchRShell");
            Assert.Less(shellL.position.x, _rigHost.transform.position.x,
                "the left shell is not on the left.");
            Assert.Greater(shellR.position.x, _rigHost.transform.position.x,
                "the right shell is not on the right.");
        }

        /// <summary>
        /// A walker, not a floater: it stands and walks on FOUR chitin legs. An alien invader could have
        /// hovered in on an anti-grav field; the pick keeps it grounded, and legs — not a hover — are how a
        /// player reads "it moves around the arena on its own".
        /// </summary>
        [UnityTest]
        public IEnumerator ItStandsOnLegs_NotRailWheels()
        {
            yield return InstallRig();

            Assert.AreEqual(4, _rigHost.GetComponentsInChildren<Transform>().Count(t => t.name.StartsWith("Leg")),
                "a walker needs its legs. Wheels or bogies would put it back on the rails the direction " +
                "explicitly took it off.");
            Assert.AreEqual(4, _rigHost.GetComponentsInChildren<Transform>().Count(t => t.name == "Foot"),
                "the legs have no feet to plant.");
        }

        /// <summary>
        /// SQUAT AND WIDE, from Lee's on-camera review. The first cut was tall and read small — on a 72°
        /// camera height foreshortens, so the mass has to be in the WIDTH. This pins the proportion so a
        /// future tweak cannot quietly stretch it tall again: it must dwarf the ~1–2 m robots by footprint
        /// and be wider than it is tall. Measured off the real renderers, not the constants.
        /// </summary>
        [UnityTest]
        public IEnumerator ItReadsSquatAndWide_NotTall()
        {
            yield return InstallRig();

            var bounds = new Bounds(_rigHost.transform.position, Vector3.zero);
            foreach (var r in _rigHost.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

            float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
            Assert.Greater(footprint, 3.2f,
                "the boiler's footprint is too small to dwarf the robots. Width is what fills screen at " +
                "the 72° angle — a big footprint is the 'that is the boss' read, not height.");
            Assert.Greater(footprint, bounds.size.y,
                "it is taller than it is wide. A tall silhouette foreshortens away on the angled camera " +
                "and reads small; Lee's review cut it down to a squat, heavy, wider-than-tall mass.");
        }

        /// <summary>
        /// A primitive from CreatePrimitive carries Unity's BUILT-IN default material, which has no URP
        /// subshader and draws MAGENTA in a build while looking perfectly correct in the editor. It is
        /// how the factory's core shipped (YT-38) and the boss's zones shipped (YT-61), and this machine
        /// is thirty-odd primitives.
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

        /// <summary>Nothing on the machine may be shot or walked into. The boss's own controller and box
        /// are the hitbox; a collider on a leg would silently eat water aimed at the boss, and Max would
        /// bump into a boiler gameplay does not believe is there.</summary>
        [UnityTest]
        public IEnumerator NoPartOfTheMachineCanBeShot()
        {
            yield return InstallRig();

            var colliders = _rigHost.GetComponentsInChildren<Collider>(includeInactive: true);
            Assert.IsEmpty(colliders,
                $"the machine is carrying {colliders.Length} collider(s). Every one is a shot that never " +
                "reaches the boss.");
        }

        /// <summary>
        /// The machine belongs to this rig and nothing else. This is the bug the fight was really about:
        /// two scene-wide sweeps re-material anything they recognise — CharacterSkinDirector claims every
        /// renderer under an IDamageable, RuntimeSurfaceDirector repaints everything else BY SHAPE. Either
        /// one getting hold of a part gives it a SECOND writer on its property block, and script order
        /// picks what the player sees. That is precisely how the wind-up tell died.
        /// </summary>
        [UnityTest]
        public IEnumerator NoSceneDirectorClaimsTheMachine()
        {
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
                        $"CharacterSkinDirector claimed '{r.name}'. It will rewrite that part's colour " +
                        "every LateUpdate, and the wind-up tell this rig writes in LateUpdate becomes a " +
                        "coin toss on script order — which is how the tell died in the first place.");

                    Assert.IsNull(r.GetComponent<SurfaceSkinned>(),
                        $"RuntimeSurfaceDirector repainted '{r.name}'. It classifies by SHAPE: the drum " +
                        "is a cylinder, so the boss is now made of drainpipes.");
                }
            }
            finally
            {
                Object.Destroy(skins.gameObject);
                Object.Destroy(surfaces.gameObject);
            }
        }

        // ------------------------------------------------------------------ reading the fight off it

        /// <summary>It stands beyond the gate from the first frame. It should look dead, not switched
        /// off — and the moment the last factory dies, it opens its ports.</summary>
        [UnityTest]
        public IEnumerator TheMachineSleeps_UntilTheFactoryDies()
        {
            yield return InstallRig();
            yield return null;

            Assert.Less(Luma(Rig.EyeColor), 0.02f,
                "its ports are already lit and the factory is still standing. The wake-up is the beat " +
                "that makes the boss an event.");
            Assert.IsFalse(Rig.Running, "it is running before anything has woken it.");

            yield return Wake();

            Assert.IsTrue(Rig.Running, "the factory is dead and the boss has not woken up.");
            Assert.Greater(Luma(Rig.EyeColor), 0.15f, "it woke up with its ports still out.");
        }

        /// <summary>
        /// THE REGRESSION GUARD. The charge wind-up has to reach the screen.
        ///
        /// The boss wrote an orange warn colour into its property block on every phase change since
        /// YT-27, and CharacterSkin overwrote that same block every frame with the flat body colour. The
        /// single most important read in the fight — "it is about to charge, MOVE" — never rendered a
        /// pixel until YT-90. This proves it survives the boiler re-theme.
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
                "miss, and it has to be the orange every robot already taught the player.");
            Assert.Greater(windup.r, windup.b + 0.4f, "the wind-up is not warm.");

            // Idle is AMBER now, not green, so the wind-up cannot be told apart by 'r rose'. The tell is
            // the gold COOLING toward orange — the green channel drops out.
            Assert.Greater(idle.g - windup.g, 0.2f,
                "the machine looks the same winding up as idling. The amber idle has to cool to a hotter " +
                "orange on the wind-up — a telegraph you cannot tell apart from doing nothing is none.");

            Assert.Greater(Rig.Pressure, 0.5f,
                "the boiler is barely building pressure as it commits to a charge. The pressure IS the " +
                "threat — the governor screaming and the boiler glowing is a boiler about to blow.");
        }

        /// <summary>Phase 2 has to be legible BETWEEN attacks, not only in the half-second it is
        /// committing to one. The player needs to know the fight got worse while deciding what to do.</summary>
        [UnityTest]
        public IEnumerator PhaseTwo_IsLegibleEvenWhenItIsNotAttacking()
        {
            yield return InstallRig();
            yield return Wake();

            Color idle = Color.black;
            yield return SampleDuring(BossAction.Reposition, c => idle = c);
            Assert.Greater(idle.g, 0.4f, "it is not idling in its own amber — the furnace glow is gone.");

            // Past the enrage threshold — asked of the tuning, because the fight's health is a knob now
            // (YT-94) and a hardcoded number stops enraging the day it moves.
            Hit(Boss, BossTuning.Health * (BossTuning.EnrageThreshold + 0.1f));

            yield return null;   // Enraged is recomputed in the brain's Tick, which runs in Update
            Assert.IsTrue(Boss.Enraged, "the boss did not enrage on a hit that took it past the threshold.");

            Color enragedIdle = Color.black;
            yield return SampleDuring(BossAction.Reposition, c => enragedIdle = c);

            // Phase 1 idles amber (high g); phase 2 idles RED (low g). Discriminate on the green channel
            // dropping out — that is what "it got worse" looks like at a glance.
            Assert.Less(enragedIdle.g, 0.35f,
                "it idles the same amber in phase 2 as phase 1. 'It got worse' has to be visible while " +
                "the player is still deciding what to do about it — the gold has to go red.");
            Assert.Greater(enragedIdle.r, enragedIdle.g + 0.3f, "phase-2 idle is not the hot red it should be.");
        }

        /// <summary>
        /// The SPAWN TELL (YT-150 + YT-157). The two side hatches open on the brood cadence — the shell
        /// splits, the cavity floods, the swarm spills from the flanks — and the OPEN shell is the spawn
        /// telegraph. Shut before it wakes; opening during the fight. (YT-157's real spawn drives them for
        /// real later; today the art-side cadence is what makes the mechanic visible for Lee's eye.)
        /// </summary>
        [UnityTest]
        public IEnumerator TheSideHatches_VentAsTheSpawnTell()
        {
            yield return InstallRig();
            yield return null;

            Assert.Less(Rig.HatchOpen, 0.05f, "the hatches are open before it has even woken.");

            yield return Wake();

            // The brood cadence opens the shells to disgorge. Give it a couple of cycles of real fight time
            // — the vent waits out any charge it is committing to, so this is deliberately generous.
            float peak = 0f, t = 0f;
            while (t < 14f)
            {
                peak = Mathf.Max(peak, Rig.HatchOpen);
                if (peak > 0.6f) break;
                t += Time.deltaTime;
                yield return null;
            }

            Assert.Greater(peak, 0.6f,
                "the side hatches never opened. The open shell is the spawn telegraph (YT-157) — if it " +
                "never vents, the player never sees the swarm coming.");
        }

        /// <summary>
        /// It has to actually die on screen. The boss deactivates its own GameObject on the frame it
        /// dies, and the result screen freezes the game (timeScale = 0) that same frame. This machine is
        /// not parented to the boss, so it outlives it and topples — on UNSCALED time, or it would be
        /// frozen solid before it moved.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMachineComesApart_WhenTheBossDies()
        {
            yield return InstallRig();
            yield return Wake();

            HudSignals.EmitBossDefeated();
            Assert.IsFalse(Rig.Running, "the boss is dead and the machine is still running.");

            float scale = Time.timeScale;
            Time.timeScale = 0f;
            try
            {
                float t = 0f;
                while (_rigHost.activeSelf && t < 1.5f) { t += Time.unscaledDeltaTime; yield return null; }

                Assert.IsFalse(_rigHost.activeSelf,
                    "the machine is still standing half a second after it died — and the game is paused, " +
                    "so it never will not be. The death throe is on scaled time.");
            }
            finally
            {
                Time.timeScale = scale;
            }
        }
    }
}
