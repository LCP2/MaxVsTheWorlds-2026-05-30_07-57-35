using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1478: a Parts Cache per City area. One test (Rule 1), resolved state only (Tier 2): builds the real City through
    /// MapRuntime.Build and drives the real cache, robots, replicator and PickupDirector.
    ///   AC1 - owned robots alive => SEALED and a 100-damage hit does nothing; the last owned one dies => UNSEALED within 0.5 s;
    ///         a robot of another area standing inside the area does not keep it sealed.
    ///   AC2 - a producer (the area's Replicator) alive keeps it SEALED after the garrison is dead.
    ///   AC3 - damage to 66% / 33% / 0% breaks lock 1 / 2 / 3; at 0 HP exactly the model's Parts for the area are spawned;
    ///         killing robots in the City spawns 0 Parts; World 2 (Stormdrain) kills still drop Parts.
    /// Fails on the base commit: PartsCache does not exist (the test does not compile), and robots there drop Parts.
    /// </summary>
    public sealed class MV1478PartsCacheTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _host;
        private GameObject _pickupGo;
        private GameObject _areaGo;
        private AreaAccumulationDirector _area;

        [SetUp]
        public void SetUp()
        {
            PickupWallet.Reset();
            RocketCrateCensus.Reset();
            Pickup.ResetRegistry();
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            foreach (var a in Object.FindObjectsByType<AreaAccumulationDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(a.gameObject);
            // Robots / producers other fixtures left standing would be counted as this area's (the cache counts the whole scene).
            foreach (var r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(r.gameObject);
            foreach (var r in Object.FindObjectsByType<Replicator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(r.gameObject);
            foreach (var c in Object.FindObjectsByType<AssemblyCradle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(c.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            if (_pickupGo != null)
            {
                var pd = _pickupGo.GetComponent<PickupDirector>();
                if (pd != null) typeof(PickupDirector).GetMethod("OnDisable", NonPublicInstance).Invoke(pd, null);
            }
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(r.gameObject);
            if (_pickupGo != null) Object.DestroyImmediate(_pickupGo);
            if (_areaGo != null) Object.DestroyImmediate(_areaGo);
            if (_host != null) Object.DestroyImmediate(_host);
            PickupWallet.Reset();
            RocketCrateCensus.Reset();
            Pickup.ResetRegistry();
        }

        private RobotEnemy NewRobot(string name, int areaIndex, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.position = pos;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance).Invoke(e, null);   // runs ResetState(); the area stamp must follow it
            e.SetAreaIndex(areaIndex);
            return e;
        }

        private static void Kill(IDamageable d, Vector3 at) =>
            d.TakeDamage(new DamageInfo(1_000_000f, at, Vector3.forward, Team.Player));

        private static int PartsValueOnGround()
        {
            int value = 0;
            foreach (Pickup p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (p.Kind == PickupKind.Supercell) value += PickupWallet.SupercellCellValue;
                else if (p.Kind == PickupKind.PowerCell) value += 1;
            }
            return value;
        }

        private static void Settle(PartsCache cache)
        {
            cache.InvalidateCensus();
            for (int i = 0; i < 5; i++) cache.Tick(0.1f);   // 0.5 s of ticks
        }

        [Test]
        public void CityCache_SealsWhileTheAreaIsAlive_UnsealsWhenClear_BreaksThreeLocks_AndPaysTheModelsParts()
        {
            int city = WorldCatalog.IndexOf(WorldIds.City);
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _host = new GameObject("MV1478 Host");
            MapRuntime.Build(map, _host.transform);

            _pickupGo = new GameObject("MV1478 PickupDirector");
            var pickups = _pickupGo.AddComponent<PickupDirector>();
            typeof(PickupDirector).GetMethod("OnEnable", NonPublicInstance).Invoke(pickups, null);

            _areaGo = new GameObject("MV1478 Area Accumulation");
            _area = _areaGo.AddComponent<AreaAccumulationDirector>();
            _area.ConfigureWorld(null, city);
            var filled = (System.Collections.Generic.HashSet<int>)typeof(AreaAccumulationDirector)
                .GetField("_filledAreas", NonPublicInstance).GetValue(_area);
            filled.Add(1);
            filled.Add(2);

            int baseline = PartsValueOnGround();   // the map itself places a few authored pickups; only what the test causes counts
            PartsCache a1 = PartsCache.All.Single(c => c.Id == "a1_partscache1");
            PartsCache a2 = PartsCache.All.Single(c => c.Id == "a2_partscache1");
            // area 2's one relay (a2_rep1 at 21.5, 38.5 in city_config.json); MapRuntime does not stamp its Id, WorldRunner does.
            Replicator rep = Object.FindObjectsByType<Replicator>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OrderBy(r => Vector2.Distance(new Vector2(r.transform.position.x, r.transform.position.z), new Vector2(21.5f, 38.5f)))
                .First();
            rep.Build();           // Awake does not run on AddComponent outside Play mode
            rep.SetAreaIndex(2);   // what WorldRunner stamps in a real run

            // ---- AC1: three owned robots alive => SEALED, a 100-damage hit does nothing.
            Vector3 a1Pos = a1.transform.position;
            RobotEnemy[] owned =
            {
                NewRobot("a1_r1", 1, a1Pos + new Vector3(4f, 0f, 0f)),
                NewRobot("a1_r2", 1, a1Pos + new Vector3(-4f, 0f, 0f)),
                NewRobot("a1_r3", 1, a1Pos + new Vector3(0f, 0f, 4f)),
            };
            RobotEnemy visitor = NewRobot("a2_visitor", 2, a1Pos + new Vector3(0.5f, 0f, 0.5f));   // stands inside area 1

            Settle(a1);
            Assert.AreEqual(PartsCache.CacheState.Sealed, a1.State, "3 owned robots alive");
            Assert.AreEqual(3, a1.RobotsLeft, "only the area's own robots count (the area-2 visitor does not)");
            float maxHp = a1.Health;
            a1.TakeDamage(new DamageInfo(100f, a1Pos, Vector3.forward, Team.Player));
            Assert.AreEqual(maxHp, a1.Health, 0.001f, "a sealed cache absorbs a 100-damage hit");
            Assert.AreEqual(PartsCache.LockCount, a1.LocksLeft, "a sealed cache loses no lock");

            // Robots in the City drop no Parts (rockets still drop); the explicit Brute kill is a large kill.
            foreach (RobotEnemy r in owned) Kill(r, r.transform.position);
            DropSignals.EmitRobotDied(a1Pos, EnemyKind.Brute);
            Assert.AreEqual(baseline, PartsValueOnGround(), "killing robots in a Parts Cache world spawns 0 Parts");

            // ---- AC1: the last owned one is dead => UNSEALED within 0.5 s, even though the visitor still lives.
            Assert.IsTrue(visitor.IsAlive, "precondition: the area-2 visitor is still alive");
            Settle(a1);
            Assert.AreEqual(PartsCache.CacheState.Unsealed, a1.State, "the visitor from area 2 must not keep area 1's cache sealed");

            // ---- AC2: area 2's only robot (the visitor) is now the whole garrison; kill it. The replicator is alive => still SEALED.
            Kill(visitor, visitor.transform.position);
            Settle(a2);
            Assert.AreEqual(PartsCache.CacheState.Sealed, a2.State, "a live producer keeps the cache sealed after the garrison is dead");
            Assert.IsTrue(a2.ProducerAlive, "the area's replicator is a producer");
            StringAssert.Contains("DESTROY THE SOURCE", a2.LabelText);
            Kill(rep, rep.transform.position);
            Settle(a2);
            Assert.AreEqual(PartsCache.CacheState.Unsealed, a2.State, "garrison and producer gone");

            // ---- AC3: 66% / 33% / 0% break lock 1 / 2 / 3; at 0 HP exactly the model's Parts spill out.
            baseline = PartsValueOnGround();   // destroying the relay paid its own (unchanged) shed cache
            int expected = WorldEconomyModel.CachePartsForArea(city, 1);
            Assert.Greater(expected, 0, "the model gives area 1 a positive Parts amount");
            a1.TakeDamage(new DamageInfo(maxHp * 0.34f, a1Pos, Vector3.forward, Team.Player));
            Assert.AreEqual(2, a1.LocksLeft, "at 66% lock 1 breaks");
            a1.TakeDamage(new DamageInfo(maxHp * 0.33f, a1Pos, Vector3.forward, Team.Player));
            Assert.AreEqual(1, a1.LocksLeft, "at 33% lock 2 breaks");
            Assert.AreEqual(baseline, PartsValueOnGround(), "nothing has spilled before the last lock");
            a1.TakeDamage(new DamageInfo(maxHp, a1Pos, Vector3.forward, Team.Player));
            Assert.AreEqual(0, a1.LocksLeft, "at 0 HP lock 3 breaks");
            Assert.AreEqual(PartsCache.CacheState.Opened, a1.State, "0 HP bursts the cache");
            Assert.AreEqual(expected, PartsValueOnGround() - baseline, "exactly the model's Parts for area 1 are spawned");

            // ---- AC3: World 2 is unchanged - a kill there still drops Parts.
            foreach (Pickup p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            _area.ConfigureWorld(null, WorldCatalog.IndexOf(WorldIds.Stormdrain));
            DropSignals.EmitRobotDied(Vector3.zero, EnemyKind.Brute);
            Assert.Greater(PartsValueOnGround(), 0, "a World 2 kill still drops Parts");
        }
    }
}
