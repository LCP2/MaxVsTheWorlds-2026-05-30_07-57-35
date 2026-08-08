using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.UI;
using MaxWorlds.Upgrades;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The gated-arena mechanic against a real built gate (v0.5 recut spec §1, WV-222):
    /// <see cref="AreaGateTests"/> proves the map format/validator accept the entity kind;
    /// this proves the BUILT <see cref="AreaGate"/> actually blocks, takes only primary damage, and
    /// opens on schedule.
    ///
    /// Built directly through <see cref="MapRuntime.Build"/> from an inline fixture rather than
    /// through <see cref="MapLibrary"/>/<c>backyard_slice.json</c> — this is a reusable engine
    /// capability landing with its own tested map, not a cutover of the shipped one (Lee's boss-fight
    /// call on WV-222 is still open for a follow-up ticket).
    /// </summary>
    public sealed class AreaGatePlayTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            DevTuning.Reset();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            DevTuning.Reset();
            yield return null;
        }

        /// <summary>Two rooms sealed by one area gate — built fresh each call so a test that overrides
        /// <see cref="DevTuning"/> beforehand gets a gate sized off that override.</summary>
        private IEnumerator BuildTwoAreas(float doorway = 4f)
        {
            var map = new MapData
            {
                name = "Two Areas", wallHeight = 3f, wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "area1", type = "entry", x = 0f, z = -10f, width = 20f, depth = 20f },
                    new MapZone { id = "area2", type = "open",  x = 0f, z =  10f, width = 20f, depth = 20f },
                },
                links = new[] { new MapLink { from = "area1", to = "area2", doorway = doorway, gate = "gate1" } },
                entities = new[]
                {
                    new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = -10f },
                    new MapEntity { id = "gate1", kind = "areaGate", x = 0f, z = 0f, height = 3f, depth = 0.6f },
                },
            };

            _root = new GameObject("AreaGate Test Root");
            _built = MapRuntime.Build(map, _root.transform);
            _map = map;
            yield return null;
        }

        private MapBuild _built;
        private MapData _map;

        private AreaGate Gate() => _built.Actors["gate1"].GetComponent<AreaGate>();

        private static DamageInfo Hit(float amount, DamageSource source, Team attacker = Team.Player) =>
            new DamageInfo(amount, Vector3.zero, Vector3.forward, attacker, source: source);

        [UnityTest]
        public IEnumerator TheGateIsBuiltAlive_AndBlocksThePassage()
        {
            yield return BuildTwoAreas();

            AreaGate gate = Gate();
            Assert.IsNotNull(gate, "the map named an areaGate entity but built no AreaGate component");
            Assert.IsTrue(gate.IsAlive, "the gate was born already broken");
            Assert.IsFalse(gate.IsOpen);

            var col = gate.GetComponent<Collider>();
            Assert.IsTrue(col.enabled, "a fresh area gate should start closed");
        }

        [UnityTest]
        public IEnumerator TheGateSealsTheDoorwayItFills()
        {
            yield return BuildTwoAreas(doorway: 6f);

            MapEntity entity = _map.Entity("gate1");
            Assert.AreEqual(MapRuntime.SealWidth(_map, entity), Gate().transform.localScale.x, 1e-3,
                "the area gate is not as wide as its doorway — there is a sliver to squeeze through");
        }

        [UnityTest]
        public IEnumerator NonPrimaryDamage_DoesNothing()
        {
            yield return BuildTwoAreas();
            AreaGate gate = Gate();

            gate.TakeDamage(Hit(9999f, DamageSource.SecondaryWeapon));
            gate.TakeDamage(Hit(9999f, DamageSource.Ability));
            gate.TakeDamage(Hit(9999f, DamageSource.Unspecified));

            Assert.IsTrue(gate.IsAlive, "a Water Balloon (or an untagged hit) broke a gate only the primary should break");
            Assert.AreEqual(1f, gate.Normalized, 1e-4);
        }

        [UnityTest]
        public IEnumerator SustainedPrimaryFire_BreaksTheGateAtExactlyItsHp_NotBefore()
        {
            yield return BuildTwoAreas();
            AreaGate gate = Gate();
            float maxHp = gate.MaxHp;

            gate.TakeDamage(Hit(maxHp - 1f, DamageSource.PrimaryWeapon));
            Assert.IsTrue(gate.IsAlive, "the gate broke before it was actually out of HP");
            Assert.IsTrue(gate.GetComponent<Collider>().enabled, "the collider dropped early");

            gate.TakeDamage(Hit(1f, DamageSource.PrimaryWeapon));
            Assert.IsFalse(gate.IsAlive);
            Assert.IsTrue(gate.IsOpen, "the gate has zero HP but never opened");
            Assert.IsFalse(gate.GetComponent<Collider>().enabled, "an open gate still blocks the doorway");
            Assert.AreEqual(0f, gate.HealthNormalized, 1e-4, "a destroyed gate should report zero health");
            Assert.AreEqual(0f, gate.HealthCurrent, 1e-4, "a destroyed gate should report zero health");
        }

        [UnityTest]
        public IEnumerator TheDefaultBreakTime_Is4SecondsOfBaseTierPrimaryFire()
        {
            yield return BuildTwoAreas();

            // Spec §1: "sustained fire breaks it in ~gateBreakSeconds (default 4 s)". AssumedPrimaryDps
            // is the primary's own authored base rate (WaterBlaster: damagePerTick 4 / fireInterval 0.1).
            Assert.AreEqual(ArenaTuning.DefaultGateBreakSeconds * AreaGate.AssumedPrimaryDps, Gate().MaxHp, 1e-3);
        }

        [UnityTest]
        public IEnumerator MovingTheGateBreakSecondsSlider_RetunesAFreshlyBuiltGate()
        {
            DevTuning.GateBreakSeconds = 2f;
            yield return BuildTwoAreas();

            // Moving the Settings-panel slider is meant to change how long a gate takes, not just sit
            // there unread (WV-234's settings existed before this ticket made anything consume them).
            Assert.AreEqual(2f * AreaGate.AssumedPrimaryDps, Gate().MaxHp, 1e-3);
        }

        [UnityTest]
        public IEnumerator GateRequiresClear_DefaultsOff_AndDamageAppliesImmediately()
        {
            yield return BuildTwoAreas();
            AreaGate gate = Gate();

            Assert.IsFalse(gate.RequiresClear, "gateRequiresClear should default off (spec §1)");
            gate.TakeDamage(Hit(10f, DamageSource.PrimaryWeapon));
            Assert.Less(gate.Normalized, 1f, "the gate ignored a hit it had no reason to reject");
        }

        [UnityTest]
        public IEnumerator GateRequiresClear_RejectsDamageOnlyWhileItsRoomHookSaysNotClear()
        {
            DevTuning.GateRequiresClear = 1f;
            yield return BuildTwoAreas();
            AreaGate gate = Gate();

            Assert.IsTrue(gate.RequiresClear);

            // No robot-room system wired yet (WV-223) — an unwired hook must not deadlock the gate.
            gate.TakeDamage(Hit(10f, DamageSource.PrimaryWeapon));
            Assert.Less(gate.Normalized, 1f, "an area gate with nothing wired to RoomClear should behave as if clear");

            float beforeHook = gate.Normalized;
            gate.RoomClear = () => false;
            gate.TakeDamage(Hit(10f, DamageSource.PrimaryWeapon));
            Assert.AreEqual(beforeHook, gate.Normalized, 1e-4, "damage applied while the room hook said 'not clear'");

            gate.RoomClear = () => true;
            gate.TakeDamage(Hit(10f, DamageSource.PrimaryWeapon));
            Assert.Less(gate.Normalized, beforeHook, "damage was still rejected once the room hook said 'clear'");
        }

        [UnityTest]
        public IEnumerator FriendlyFire_AnEnemyTaggedHit_IsRejectedEvenIfMislabelledPrimary()
        {
            yield return BuildTwoAreas();
            AreaGate gate = Gate();

            gate.TakeDamage(Hit(9999f, DamageSource.PrimaryWeapon, attacker: Team.Enemy));

            Assert.IsTrue(gate.IsAlive, "the gate's own team took damage from 'itself'");
            Assert.AreEqual(1f, gate.Normalized, 1e-4);
        }

        [UnityTest]
        public IEnumerator OpeningTheGate_FiresOpenedExactlyOnce()
        {
            yield return BuildTwoAreas();
            AreaGate gate = Gate();

            int opened = 0;
            gate.Opened += () => opened++;

            gate.TakeDamage(Hit(gate.MaxHp, DamageSource.PrimaryWeapon));
            gate.TakeDamage(Hit(50f, DamageSource.PrimaryWeapon)); // after death — must be a no-op

            Assert.AreEqual(1, opened, "Opened should fire exactly once, not once per hit past zero HP");
        }

        [UnityTest]
        public IEnumerator AFreshGate_CarriesAnAlwaysShownHealthBar()
        {
            yield return BuildTwoAreas();
            AreaGate gate = Gate();

            var bar = gate.GetComponent<WorldHealthBar>();
            Assert.IsNotNull(bar, "MV-265: a gate needs a health indicator so the player can tell it's breakable");
            Assert.IsTrue(bar.Showing, "the gate's bar should be visible before it has ever been hit");
        }

        [UnityTest]
        public IEnumerator DamagingTheGate_DepletesTheReadoutItsHealthBarBindsTo()
        {
            yield return BuildTwoAreas();
            AreaGate gate = Gate();
            float maxHp = gate.MaxHp;

            gate.TakeDamage(Hit(maxHp * 0.5f, DamageSource.PrimaryWeapon));

            // WorldHealthBar reads IHealthReadout.HealthNormalized every frame — asserting this is
            // what actually drives the bar's fill without reaching into its private UI internals.
            Assert.AreEqual(0.5f, gate.HealthNormalized, 1e-3,
                "the gate's readout should track the damage its bar is meant to show");
        }

        // ---------------------------------------------------------------- full-width hit test (MV-302)

        /// <summary>
        /// Pins MV-302 part B: the old hit test fed the SPRAY cone/line-of-sight check the gate's
        /// centre-point transform.position — so a wide gate only took damage dead-on, and either end
        /// was untouchable. Built directly (not through MapRuntime) so the geometry is exact: a real
        /// WaterBlaster fires straight down its default forward axis (+Z) at a gate parked well off to
        /// one SIDE, so its CENTRE sits far outside the narrow spray cone while its NEAR edge still
        /// straddles the axis Max is actually aiming down.
        /// </summary>
        [UnityTest]
        public IEnumerator FiringAtTheEdgeOfAWideGate_StillDamagesIt()
        {
            UpgradeState.Reset();
            WeaponSystemState.Reset();

            var maxGo = new GameObject("Max");
            maxGo.tag = "Player";
            maxGo.AddComponent<CharacterController>();
            var blaster = maxGo.AddComponent<WaterBlaster>();
            yield return null;   // Awake builds the weapon's sub-objects

            // Same shape MapRuntime.BuildAreaGate gives every area gate (wide local X, thin local Z),
            // parked off-axis: its centre is ~33 degrees off Max's forward (the base cone is only 8
            // degrees either side), but its near edge crosses X=0 — the line Max is aiming straight down.
            var gateGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gateGo.transform.position = new Vector3(2.6f, 0f, 4f);
            gateGo.transform.localScale = new Vector3(6f, 3f, 0.6f);
            var gate = gateGo.AddComponent<AreaGate>();
            yield return null;

            float startHp = gate.HealthCurrent;

            blaster.SetFiring(true);
            yield return new WaitForSeconds(0.3f);   // several ticks at the default 0.1 s interval
            blaster.SetFiring(false);

            Assert.That(gate.HealthCurrent, Is.LessThan(startHp),
                "firing at the near edge of a wide, off-axis gate did nothing — only a hit on its " +
                "centre point ever registered (MV-302)");

            Object.Destroy(gateGo);
            Object.Destroy(maxGo);
            UpgradeState.Reset();
            WeaponSystemState.Reset();
        }

        [UnityTest]
        public IEnumerator OpeningTheGate_HingesItSwingOpen()
        {
            yield return BuildTwoAreas();
            AreaGate gate = Gate();

            Vector3 closedPosition = gate.transform.position;
            Quaternion closedRotation = gate.transform.rotation;
            float halfWidth = gate.transform.localScale.x * 0.5f;

            gate.TakeDamage(Hit(gate.MaxHp, DamageSource.PrimaryWeapon));

            // Let the hinge swing run to completion (well past its authored duration).
            yield return new WaitForSeconds(0.6f);

            float swept = Quaternion.Angle(closedRotation, gate.transform.rotation);
            Assert.Greater(swept, 80f,
                "a destroyed gate should visibly hinge open past 90 degrees, not just drop its collider");

            // A rigid rotation around a nearby edge shouldn't fling the body far from where it stood —
            // catches a mis-picked pivot (e.g. rotating around the world origin instead of the gate's
            // own edge) without pinning the exact swept position.
            float displacement = Vector3.Distance(closedPosition, gate.transform.position);
            Assert.Less(displacement, halfWidth * 2f + 0.5f,
                "the gate should swing on its own edge, not translate away from the doorway");
        }
    }
}
