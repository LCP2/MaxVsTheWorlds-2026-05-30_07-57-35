using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The map engine against the real actors (YT-89, YT-92). The EditMode tests prove the map's SHAPE
    /// is right; these prove the map is what actually puts the level together.
    ///
    /// Every adopted actor here starts parked at the origin — deliberately in the wrong place — and no
    /// gate is ever dragged into a factory's slot. The factories are not built by the fixture at all:
    /// the map builds those, which is the half of the engine YT-92 added. If the actors end up where
    /// the map says, and if the run's mission wiring works anyway, then the level's positions and its
    /// objectives both came out of the data. That is the whole ticket, asserted.
    /// </summary>
    public sealed class MapPlayTests
    {
        private GameObject _path, _player, _gate, _boss, _strayHutch;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject go in new[] { _path, _player, _gate, _boss, _strayHutch })
                if (go != null) Object.Destroy(go);

            yield return null;
        }

        /// <summary>Builds the scene's one-of actors — all at the origin — then loads the map on top of
        /// them. The factories arrive with the map.</summary>
        private IEnumerator BuildLevelFromTheMap()
        {
            _player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _player.name = "Max (Greybox)";
            _player.tag = "Player";
            _player.AddComponent<CharacterController>();

            _gate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _gate.name = "SubZone Gate";
            _gate.AddComponent<SubZoneGate>();

            _boss = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _boss.name = "Big Bermuda";
            _boss.AddComponent<BigBermudaBoss>();

            yield return null;   // let the actors wake up where they are: nowhere

            // ...and now the map has its say. BackyardPath.Awake runs on construction.
            _path = new GameObject("Backyard Path", typeof(BackyardPath));
            yield return null;
            Physics.SyncTransforms();
            yield return null;
        }

        private static MapData Shipped() => MapLibrary.Load(MapLibrary.BackyardSlice);

        [UnityTest]
        public IEnumerator TheMapPutsEveryActorWhereItSaysTheyStand()
        {
            yield return BuildLevelFromTheMap();

            MapData map = Shipped();
            Assert.IsNotNull(map);

            AssertStandsWhereTheMapSays(map, "max_start", _player);
            AssertStandsWhereTheMapSays(map, "boss_gate", _gate);
            AssertStandsWhereTheMapSays(map, "big_bermuda", _boss);
        }

        /// <summary>
        /// The engine builds a factory for every factory the map authors (YT-92) — which is what a
        /// second one needs, and it is the thing the old engine could not do at all: it adopted the one
        /// hutch the scene owned, so a map with two factories got one hutch, teleported twice.
        /// </summary>
        [UnityTest]
        public IEnumerator TheMapBuildsAFactoryForEveryOneItAuthors()
        {
            yield return BuildLevelFromTheMap();

            MapData map = Shipped();
            var authored = MapValidation.Kind(map, EntityKind.Factory);

            Assert.AreEqual(authored.Count, FactoryCensus.Total,
                "the level does not have as many factories as the map authors");
            Assert.GreaterOrEqual(FactoryCensus.Total, 2, "the run has fewer than two sources of pressure");

            for (int i = 0; i < authored.Count; i++)
            {
                MowerHutch hutch = FactoryCensus.All[i];
                Assert.IsNotNull(hutch, $"factory {i} was never built");

                Assert.AreEqual(authored[i].x, hutch.transform.position.x, 0.01f,
                    $"'{authored[i].id}' is at the wrong X");
                Assert.AreEqual(authored[i].z, hutch.transform.position.z, 0.01f,
                    $"'{authored[i].id}' is at the wrong Z");

                // Each one is a whole factory, not a body: its own spawner, its own mouth, its own
                // stream of robots. Two factories sharing a spawner would be one factory with two
                // bodies.
                Assert.IsNotNull(hutch.GetComponent<EnemySpawner>(),
                    $"'{authored[i].id}' has no spawner — it is a box, not a source");
                Assert.IsTrue(hutch.IsAlive, $"'{authored[i].id}' was born dead");
            }
        }

        /// <summary>
        /// Both factories are WORKING factories — each one is emitting its own stream of robots, out of
        /// its own mouth, into its own room. That is the ticket's acceptance, and "it has a spawner
        /// component" is not the same claim: a second factory that stands there looking like a factory
        /// and never produces anything is exactly the failure this would ship.
        ///
        /// Robots are matched to the factory they came out of by proximity — the mouth puts them a few
        /// metres from the body (YT-70) — so a stream that all came from one shed cannot pass.
        /// </summary>
        [UnityTest]
        public IEnumerator BothFactoriesActuallyEmitRobots()
        {
            yield return BuildLevelFromTheMap();

            // The cadence starts at 1.8 s and the fixture has already burned a few frames; give both
            // sheds enough time to have put something on the field.
            float t = 0f;
            while (t < 5f) { t += Time.deltaTime; yield return null; }

            foreach (MowerHutch hutch in FactoryCensus.All)
            {
                var spawner = hutch.GetComponent<EnemySpawner>();
                Assert.Greater(spawner.LiveCount, 0,
                    $"'{hutch.name}' has not emitted a single robot — it is scenery shaped like a factory");

                // …and what it emitted is standing next to IT, not next to the other one.
                foreach (RobotEnemy robot in hutch.GetComponentsInChildren<RobotEnemy>())
                {
                    if (!robot.gameObject.activeInHierarchy) continue;   // pooled, not yet alive

                    float fromMine = Flat(robot.transform.position - hutch.transform.position);
                    Assert.Less(fromMine, 12f,
                        $"a robot from '{hutch.name}' is {fromMine:0} m from it — it did not come out of " +
                        "this factory's mouth");
                }
            }
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>
        /// A hand-placed hutch stands down (YT-92). The scene carried one from the very first scaffold,
        /// with its own serialized numbers quietly overriding the code's; the map owns the factories
        /// now, and a leftover would be a third one standing in the wrong room, spawning robots.
        /// </summary>
        [UnityTest]
        public IEnumerator AHandPlacedFactory_StandsDown_SoTheLevelHasOnlyTheOnesTheMapAuthors()
        {
            _strayHutch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _strayHutch.name = "Mower Hutch (hand-placed)";
            _strayHutch.transform.position = new Vector3(0f, 1f, 0f);
            _strayHutch.AddComponent<MowerHutch>();

            yield return BuildLevelFromTheMap();

            Assert.IsFalse(_strayHutch.activeInHierarchy,
                "the scene's hand-placed factory is still standing — the level has a source the map " +
                "never authored");

            Assert.AreEqual(MapValidation.Kind(Shipped(), EntityKind.Factory).Count, FactoryCensus.Total,
                "the census counted the retired hutch");
        }

        /// <summary>The gate is as wide as the doorway it fills — read off the link, not typed in — so
        /// widening the way through can never leave a gap beside the thing that seals it.</summary>
        [UnityTest]
        public IEnumerator TheGateSealsTheDoorwayItFills()
        {
            yield return BuildLevelFromTheMap();

            MapData map = Shipped();
            MapEntity gate = map.First(EntityKind.Gate);

            Assert.AreEqual(MapRuntime.SealWidth(map, gate), _gate.transform.localScale.x, 1e-3,
                "the gate is not as wide as its doorway — there is a sliver to squeeze through");
        }

        /// <summary>
        /// The mission, proved from data: every factory, then the gate. Nothing wires these factories
        /// to this gate except the map's <c>opensOn</c>, and nothing tells the gate it takes four keys
        /// except the fact that the map named four — including the central garden shed, which is a
        /// real factory now too (YT-185), not a landmark the gate can ignore.
        ///
        /// The first kill NOT opening the gate is the assertion that matters. That is the difference
        /// between a run with a build-up and the one the playtest found — where you were through the
        /// gate and at the boss before the fight had started (YT-92).
        /// </summary>
        [UnityTest]
        public IEnumerator TheGateOpensOnTheLastFactory_NotTheFirst()
        {
            yield return BuildLevelFromTheMap();

            var door = _gate.GetComponent<Collider>();
            var gate = _gate.GetComponent<SubZoneGate>();
            Assert.IsTrue(door.enabled, "the boss gate should start closed");
            Assert.AreEqual(4, gate.Keys, "the gate is not waiting on all four factories");

            yield return Destroy(FactoryCensus.All[0]);

            Assert.IsTrue(door.enabled,
                "the gate opened on the FIRST factory. Three are still standing and still spawning, " +
                "and the player can walk past them to the boss.");
            Assert.AreEqual(3, gate.KeysRemaining, "the gate did not count the first kill");

            yield return Destroy(FactoryCensus.All[1]);

            Assert.IsTrue(door.enabled,
                "the gate opened on the SECOND factory. Two are still standing and still " +
                "spawning, and the player can walk past them to the boss.");
            Assert.AreEqual(2, gate.KeysRemaining, "the gate did not count the second kill");

            yield return Destroy(FactoryCensus.All[2]);

            Assert.IsTrue(door.enabled,
                "the gate opened on the THIRD factory. The fourth is still standing and still " +
                "spawning, and the player can walk past it to the boss.");
            Assert.AreEqual(1, gate.KeysRemaining, "the gate did not count the third kill");

            yield return Destroy(FactoryCensus.All[3]);

            Assert.IsFalse(door.enabled,
                "the gate never opened — with every factory down, the run cannot be finished");
            Assert.AreEqual(0, gate.KeysRemaining, "the gate did not count the last kill");
        }

        /// <summary>Big Bermuda sleeps through the first kill. A boss that woke on it would come
        /// through the gate while a factory was still pumping robots at the player's back.</summary>
        [UnityTest]
        public IEnumerator TheBossSleepsUntilTheLastFactoryFalls()
        {
            yield return BuildLevelFromTheMap();

            var boss = _boss.GetComponent<BigBermudaBoss>();
            Assert.IsFalse(boss.Engaged, "the boss is awake before anything has been destroyed");

            yield return Destroy(FactoryCensus.All[0]);
            Assert.IsFalse(boss.Engaged, "the boss woke on the first factory, with three still up");

            yield return Destroy(FactoryCensus.All[1]);
            Assert.IsFalse(boss.Engaged, "the boss woke on the second factory, with two still up");

            yield return Destroy(FactoryCensus.All[2]);
            Assert.IsFalse(boss.Engaged, "the boss woke on the third factory, with one still up");

            yield return Destroy(FactoryCensus.All[3]);
            Assert.IsTrue(boss.Engaged, "the yard is clear and the boss never woke up");
        }

        /// <summary>Guards the wiring tests' premise. If someone quietly re-wires a gate through the
        /// serialized slot again, those tests would still pass while proving nothing — so assert a
        /// freshly built factory really has an empty slot until the map fills it.</summary>
        [UnityTest]
        public IEnumerator TheFactorysGateSlot_IsFilledByTheMap_NotByHand()
        {
            _strayHutch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _strayHutch.AddComponent<MowerHutch>();
            yield return null;

            FieldInfo slot = typeof(MowerHutch).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNull(slot.GetValue(_strayHutch.GetComponent<MowerHutch>()),
                "a freshly built factory already has a gate — the wiring tests can no longer tell " +
                "whether the map wired it");
        }

        private static IEnumerator Destroy(MowerHutch hutch)
        {
            hutch.TakeDamage(new DamageInfo(100000f, hutch.transform.position, Vector3.forward, Team.Player));
            for (int i = 0; i < 3; i++) yield return null;

            Assert.IsFalse(hutch.IsAlive, "the factory survived a lethal hit");
        }

        private static void AssertStandsWhereTheMapSays(MapData map, string id, GameObject actor)
        {
            MapEntity e = map.Entity(id);
            Assert.IsNotNull(e, $"the map has no entity '{id}'");

            Assert.AreEqual(e.x, actor.transform.position.x, 0.01f, $"'{id}' is at the wrong X");
            Assert.AreEqual(e.z, actor.transform.position.z, 0.01f,
                $"'{id}' is at z={actor.transform.position.z}, but the map puts it at z={e.z}");
        }
    }
}
