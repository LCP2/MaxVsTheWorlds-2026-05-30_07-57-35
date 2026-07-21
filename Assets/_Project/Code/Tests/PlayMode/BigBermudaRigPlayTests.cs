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
    /// The Backyard boss is a possessed BOILER-MACHINE now (YT-114), and the machine says what the
    /// fight is doing.
    ///
    /// PlayMode, because every claim here is about time: the boiler builds pressure over a wind-up, the
    /// ports change colour on a phase, the thing dies over half a second. None of that is a property you
    /// can read off a struct — which is the whole reason the wind-up tell could be dead for the life of
    /// the fight and no EditMode test noticed.
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

        // ------------------------------------------------------------------ it is a boiler, not a cube

        /// <summary>
        /// The whole ticket, in one assertion: the boss is a possessed BOILER-MACHINE, and it has the
        /// parts a boiler has. It was a mower before (YT-90) and a tinted cube before that (YT-27); Lee's
        /// Level-1 direction re-pitches the silhouette off the boiler-locomotive concept.
        /// </summary>
        [UnityTest]
        public IEnumerator TheBossIsAPossessedBoiler_NotACube()
        {
            yield return InstallRig();

            var parts = _rigHost.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(parts.Length, 20,
                "the boiler has almost no parts. A boss is the biggest, meanest silhouette on the field; " +
                "a handful of boxes is still a crate.");

            // Belly + waist + drum + shoulder + cap read as a tall round boiler; the ports are the face;
            // the stack and governor are what make it a possessed BOILER and not a water tank.
            foreach (var required in new[]
                     { "Belly", "Waist", "Drum", "Shoulder", "Cap", "Stack", "Governor", "EyeBig", "EyeL", "EyeR" })
            {
                Assert.IsTrue(Has(required),
                    $"the boiler has no '{required}'. The drum and dome are what make it a boiler, the " +
                    "stack and governor make it a machine, and the ports make it a character.");
            }

            yield return null;
        }

        /// <summary>
        /// The "not on rails" decision, made testable: it stands and walks on FOUR legs. The concept is a
        /// rail locomotive; Lee's one change is that this one is free-roaming, and legs — not bogies — are
        /// how a player reads "it moves where it likes".
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
