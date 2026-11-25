using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1460 (the one new test, Rule 1): UNDERTOW (Max's World 4 primary) fired at the first authored
    /// canister of World 4 (Reef) and World 5 (Mothership), no robot present, must drop the canister's HP
    /// and, at 0 HP, light its fuse (resolved state, Tier 2). Guards MV-1460: Undertow.FireAimedGateOnlyTick
    /// only admitted AreaGate and DestructibleCover (MV-1379), so a Team.Neutral Canister was never damaged.
    /// </summary>
    public sealed class MV1460UndertowDetonatesCanisterTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [SetUp]
        public void SetUp() { WeaponSystemState.Reset(); EnemyNavigation.Reset(); RobotEnemy.ResetRegistry(); DevMode.Reset(); DevTuning.Reset(); }

        [TearDown]
        public void TearDown() { WeaponSystemState.Reset(); EnemyNavigation.Reset(); RobotEnemy.ResetRegistry(); DevMode.Reset(); DevTuning.Reset(); }

        [TestCase("world3")]
        [TestCase("mothership")]
        public void UndertowDamagesAndDetonatesFirstAuthoredCanister(string worldKey)
        {
            if (!CoverLayer.Exists) Assert.Ignore("no Cover layer in this project");
            LogAssert_Ignore();

            string key = worldKey == "world3" ? WorldLibrary.World3 : WorldLibrary.Mothership;
            WorldConfig cfg = WorldLibrary.Load(key);
            Assert.IsNotNull(cfg, $"'{key}' must load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject("MV1460 Root");
            var undertowGo = new GameObject("MV1460 Undertow");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms();
                Assert.Greater(built.Canisters.Count, 0, $"{key}: no canister built");
                Canister canister = built.Canisters[0];
                canister.Build(); // AddComponent never fires Awake in the EditMode harness (see MV1395's own test)
                var col = canister.GetComponent<Collider>();
                Vector3 target = col != null ? col.bounds.center : canister.transform.position;
                float before = canister.HealthCurrent;

                // Stand 4 m off along the first compass direction with a clear sight line to the drum.
                Vector3 origin = Vector3.zero; bool found = false;
                foreach (Vector3 d in new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right })
                {
                    Vector3 o = target - d * 4f;
                    if (!Physics.Linecast(o, target, out RaycastHit h, CoverLayer.Mask, QueryTriggerInteraction.Ignore)
                        || h.collider == col) { origin = o; found = true; break; }
                }
                Assert.IsTrue(found, $"{key}: no clear approach to the first canister");

                WeaponSystemState.ApplyWorldLoadout(WorldCatalog.IndexOf(WorldIds.Reef));
                undertowGo.transform.SetPositionAndRotation(origin, Quaternion.LookRotation((target - origin).normalized, Vector3.up));
                var undertow = undertowGo.AddComponent<Undertow>();
                typeof(Undertow).GetMethod("Awake", NonPublicInstance).Invoke(undertow, null);
                undertow.SetFiring(true);
                DevMode.Enabled = true;
                Physics.SyncTransforms();

                var tick = typeof(Undertow).GetMethod("Tick", NonPublicInstance);
                for (int i = 0; i < 600 && canister.IsAlive; i++)
                    tick.Invoke(undertow, new object[] { undertow.FireInterval });

                Assert.Less(canister.HealthCurrent, before, $"{key}: Undertow must damage the canister (HP {canister.HealthCurrent} of {before})");
                Assert.IsTrue(canister.IsFusing, $"{key}: the canister must reach its explode (fuse) state at 0 HP");
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
                Object.DestroyImmediate(undertowGo);
                Object.DestroyImmediate(root);
            }
        }

        private static void LogAssert_Ignore() => UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
    }
}
