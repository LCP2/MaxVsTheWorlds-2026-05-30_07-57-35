using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Intro;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.Save;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1471 (the one new test, Rule 1): GRIDLOCK, killed through her real damage path in the City's
    /// a30 (the MV-1454 setup), plays the shared BossDeathSequence instead of vanishing. Tier 2: resolved
    /// renderer visibility, collider state, pickups. Guards MV-1471.
    /// </summary>
    public sealed class MV1471GridlockDeathTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ytgame-mv1471-tests");
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
            SaveSystem.DirectoryOverride = _dir;
            SaveSystem.ActiveSlot = 0;
            SaveSystem.Save(0, new SaveSlotData { HasData = true, DisplayName = "TEST", WorldIndex = WorldCatalog.IndexOf(WorldIds.City) });
            Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Clean();
            Reset();
            SaveSystem.ResetForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static void Reset()
        {
            DevTuning.Reset();
            EnemyNavigation.Reset();
            BossCensus.Reset();
            RobotEnemy.ResetRegistry();
            Pickup.ResetRegistry();
            Time.timeScale = 1f;
        }

        private static void Clean()
        {
            foreach (var t in new[] { typeof(BackyardPath), typeof(BossVictoryPayoff), typeof(WorldFinaleGate),
                                      typeof(WorldJoinSequence), typeof(PickupDirector), typeof(ResultScreen), typeof(RunTracker),
                                      typeof(GridlockRig) })
                foreach (var o in Object.FindObjectsByType(t, FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (o != null) Object.DestroyImmediate(((Component)o).gameObject);
            foreach (var p in GameObject.FindGameObjectsWithTag("Player")) Object.DestroyImmediate(p);
            foreach (var g in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(g.gameObject);
        }

        private const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;

        private static object Call(object target, string name, params object[] args) =>
            target.GetType().GetMethod(name, All).Invoke(target, args);

        private static void Subscribe(Component c) { Call(c, "OnDisable"); Call(c, "OnEnable"); }

        private static bool BodyVisible(GridlockRig rig) =>
            rig.GetComponentsInChildren<MeshRenderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy);

        [Test]
        public void GridlockDeath_PlaysSharedSequence_BodyStaysUntilItEnds_NoDamageNoBlock_CoreAtFinale()
        {
            var player = new GameObject("MV1471 Max") { tag = "Player" };
            player.AddComponent<CharacterController>();
            player.AddComponent<PlayerController>();
            var health = player.AddComponent<PlayerHealth>();
            MethodInfo healthAwake = typeof(PlayerHealth).GetMethod("Awake", All);
            healthAwake?.Invoke(health, null);

            var pathGo = new GameObject("MV1471 BackyardPath");
            var path = pathGo.AddComponent<BackyardPath>();
            Call(path, "Awake");
            Physics.SyncTransforms();
            Call(path.ExitGate, "Awake");

            WorldConfig cfg = path.Cfg;
            int finalArea = cfg.dials.areaCount;
            var payoff = Object.FindFirstObjectByType<BossVictoryPayoff>();
            Assert.IsNotNull(payoff);
            Subscribe(payoff);
            var gate = Object.FindFirstObjectByType<WorldFinaleGate>();
            Call(gate, "Awake");
            Subscribe(gate);
            PickupDirector.EnsureInstalled();

            var boss = pathGo.GetComponentsInChildren<GridlockBoss>(true).First();
            Call(boss, "Awake");
            var rig = Object.FindObjectsByType<GridlockRig>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(r => r.Boss == boss);
            Subscribe(rig);

            WorldArea a30 = cfg.AreaByIndex(finalArea);
            player.transform.position = new Vector3(a30.XMin + 4f, 0.1f, a30.CenterXz.y);
            Physics.SyncTransforms();

            Call(boss, "Wake");
            foreach (Pylon p in Object.FindObjectsByType<Pylon>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(p => p.Feeds == "gridlock"))
            {
                p.Build();
                p.TakeDamage(new DamageInfo(100000f, p.transform.position, Vector3.down, Team.Player));
            }
            for (int i = 0; i < 400 && BossCensus.DeadCountInArea(finalArea) == 0; i++)
                boss.TakeDamage(new DamageInfo(100000f, boss.transform.position, Vector3.down, Team.Player));
            Assert.AreEqual(1, BossCensus.DeadCountInArea(finalArea), "GRIDLOCK must die through TakeDamage");

            var seq = typeof(GridlockRig).GetField("_deathSeq", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(rig);
            Assert.IsNotNull(seq, "GRIDLOCK must start the shared BossDeathSequence on death");

            // Dead: no enabled blocking controller, and no damage to Max standing on her while it plays.
            Assert.IsFalse(boss.GetComponent<CharacterController>().enabled, "no enabled blocking collider after death");
            player.transform.position = boss.transform.position;
            Physics.SyncTransforms();
            float hpBefore = health.HealthCurrent;
            for (int i = 0; i < 20; i++) boss.Tick(0.1f);
            Assert.AreEqual(hpBefore, health.HealthCurrent, 0f, "a dead GRIDLOCK must deal no damage");

            for (int i = 0; i < 10; i++) Call(rig, "Tick", 0.1f);   // 1.0 s
            Assert.IsTrue(BodyVisible(rig), "body renderers must still be enabled 1.0 s after death");

            Call(payoff, "TickPendingWeaponCore", BossDeathSequence.FinaleTime);   // 1.6 s
            Assert.IsTrue(Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).Any(p => p.Kind == PickupKind.WeaponCore),
                "a Weapon Core must be present at 1.6 s");

            for (int i = 0; i < 13; i++) Call(rig, "Tick", 0.1f);   // 2.3 s
            Assert.IsFalse(BodyVisible(rig), "body renderers must be disabled by 2.3 s");
            Assert.IsFalse(boss.gameObject.activeInHierarchy, "the boss body is hidden once the sequence ends");
        }
    }
}
