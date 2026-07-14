using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The map engine against the real actors (YT-89). The EditMode tests prove the map's SHAPE is
    /// right; these prove the map is what actually puts the level together.
    ///
    /// Every actor here starts parked at the origin — deliberately in the wrong place — and no gate is
    /// ever dragged into the factory's slot. If they end up where the map says, and if destroying the
    /// factory opens the gate anyway, then the level's positions and its mission wiring both came out
    /// of the data. That is the whole ticket, asserted.
    /// </summary>
    public sealed class MapPlayTests
    {
        private GameObject _path, _player, _hutch, _gate, _boss;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject go in new[] { _path, _player, _hutch, _gate, _boss })
                if (go != null) Object.Destroy(go);
            yield return null;
        }

        /// <summary>Builds the scene's actors — all at the origin — then loads the map on top of them.</summary>
        private IEnumerator BuildLevelFromTheMap()
        {
            _player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _player.name = "Max (Greybox)";
            _player.tag = "Player";
            _player.AddComponent<CharacterController>();

            _hutch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutch.name = "Mower Hutch";
            _hutch.AddComponent<MowerHutch>();          // RequireComponent brings the EnemySpawner

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

        [UnityTest]
        public IEnumerator TheMapPutsEveryActorWhereItSaysTheyStand()
        {
            yield return BuildLevelFromTheMap();

            MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);
            Assert.IsNotNull(map);

            AssertStandsWhereTheMapSays(map, "max_start", _player);
            AssertStandsWhereTheMapSays(map, "mower_hutch", _hutch);
            AssertStandsWhereTheMapSays(map, "boss_gate", _gate);
            AssertStandsWhereTheMapSays(map, "big_bermuda", _boss);
        }

        /// <summary>The gate is as wide as the doorway it fills — read off the link, not typed in — so
        /// widening the way through can never leave a gap beside the thing that seals it.</summary>
        [UnityTest]
        public IEnumerator TheGateSealsTheDoorwayItFills()
        {
            yield return BuildLevelFromTheMap();

            MapData map = MapLibrary.Load(MapLibrary.BackyardSlice);
            MapEntity gate = map.First(EntityKind.Gate);

            Assert.AreEqual(MapRuntime.SealWidth(map, gate), _gate.transform.localScale.x, 1e-3,
                "the gate is not as wide as its doorway — there is a sliver to squeeze through");
        }

        /// <summary>The mission, proved from data: nothing wires this factory to this gate except the
        /// map's <c>opensOn</c>. The serialized slot the scene used to rely on is left empty on
        /// purpose — if it were doing the work, this test could not tell the difference.</summary>
        [UnityTest]
        public IEnumerator KillingTheFactoryOpensTheGate_WiredOnlyByTheMap()
        {
            yield return BuildLevelFromTheMap();

            var hutch = _hutch.GetComponent<MowerHutch>();
            var door = _gate.GetComponent<Collider>();

            Assert.IsTrue(hutch.IsAlive, "the factory should start alive");
            Assert.IsTrue(door.enabled, "the boss gate should start closed");

            hutch.TakeDamage(new DamageInfo(100000f, hutch.transform.position, Vector3.forward, Team.Player));
            for (int i = 0; i < 3; i++) yield return null;

            Assert.IsFalse(hutch.IsAlive, "the factory survived a lethal hit");
            Assert.IsFalse(door.enabled,
                "the boss gate never opened — the map's opensOn did not reach the factory, so the run " +
                "cannot be finished");
        }

        /// <summary>Guards the previous test's premise. If someone quietly re-wires the gate through
        /// the scene slot again, the test above would still pass while proving nothing — so assert the
        /// slot really is empty until the map fills it.</summary>
        [UnityTest]
        public IEnumerator TheFactorysGateSlot_IsFilledByTheMap_NotByHand()
        {
            _hutch = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _hutch.AddComponent<MowerHutch>();
            yield return null;

            FieldInfo slot = typeof(MowerHutch).GetField("gate", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNull(slot.GetValue(_hutch.GetComponent<MowerHutch>()),
                "a freshly built factory already has a gate — this test can no longer tell whether the " +
                "map wired it");
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
