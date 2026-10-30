using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Factories;
using MaxWorlds.Pickups;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1320 (ticket AC1): "build an area with one secret cover piece (hp 50); deal 50 damage; a
    /// Supercell and 4 PowerCells exist at its centre; collect the Supercell; SecretCensus.Found is 1
    /// and survives a save/restore round-trip, and the restore places no second cache." Must fail to
    /// COMPILE on the pre-fix base commit: no <see cref="WorldConfig"/>-level <c>WorldCover.secret</c>
    /// field, no <see cref="SecretCensus"/> type and no <see cref="Pickup.SecretId"/> property exist
    /// there — the strongest form of Rule 1's "fails first" proof. See the fix comment for the captured
    /// failure output.
    ///
    /// Real entry points throughout: a hand-authored <see cref="WorldConfig"/> (same construction idiom
    /// as <c>MV1298DestructibleCoverTests.BuildWorld</c>) carrying one secret cover piece, loaded through
    /// the real <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/> pipeline, driving the real
    /// <see cref="DestructibleCover.TakeDamage"/> break path and the real <c>PickupDirector.Collect</c>
    /// (invoked via the same reflection idiom <c>MV1038PooledPickupRegateTests</c> already uses —
    /// <c>Pickup.OnEnable</c>'s own registry is unreliable under <c>-batchmode -nographics</c>, the same
    /// reasoning <c>ChallengePad.SupercellsAwarded</c>'s own doc comment gives, so this test reads
    /// <c>PickupDirector</c>'s own live list directly instead). "Survives a save/restore round-trip" is
    /// driven by the real <see cref="SecretCensus.FoundIds"/>/<see cref="SecretCensus.ApplyCheckpointFoundIds"/>
    /// and <see cref="DestructibleCover.DestroyedIds"/>/<see cref="DestructibleCover.ApplyCheckpointDestroyedIds"/>
    /// pairs, in the same order <c>MaxWorlds.Save.SaveSystem.RestoreCheckpoint</c> calls them, against a
    /// freshly-rebuilt map — same "re-load through the real pipeline, not a snapshot shortcut" idiom
    /// <c>MV1319RepairBayTests</c> uses.
    ///
    /// ONE new test (MV-465 Rule 1). Tier 2 (Rule 2): every assertion reads a resolved runtime value — a
    /// built pickup's own <see cref="Pickup.Kind"/>/position/<see cref="Pickup.SecretId"/>,
    /// <see cref="SecretCensus.Found"/>'s own resolved count — never an authored constant, never a
    /// rendered pixel.
    /// // Guards MV-1320
    /// </summary>
    public sealed class MV1320SecretCacheTests
    {
        // Same "distinctive far-off origin" idiom MV1298DestructibleCoverTests uses — EditMode tests
        // share one physics scene for the whole cc-verify run with no per-test reset.
        private const float OriginX = 63014f;
        private const float OriginZ = 27586f;

        // Comfortably covers the cache's own 0.9 m scatter ring (DestructibleCover.SecretScatterRadius)
        // while staying well clear of anything else this isolated world ever places.
        private const float NearRadius = 1.6f;

        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            DestructibleCover.ResetRegistry();
            SecretCensus.Reset();
            PickupWallet.Reset();
            DestroyAllPickupsAndDirectors();
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            DestroyAllPickupsAndDirectors();
            DestructibleCover.ResetRegistry();
            SecretCensus.Reset();
            PickupWallet.Reset();
        }

        private static void DestroyAllPickupsAndDirectors()
        {
            foreach (var p in Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(p.gameObject);
            foreach (var d in Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
            Pickup.ResetRegistry();
        }

        /// <summary>Stub (entry, empty) -> "a1" (one hp-50 secret cover piece) — same two-area skeleton
        /// <c>MV1298DestructibleCoverTests.BuildWorld</c> uses.</summary>
        private static WorldConfig BuildWorld() => new WorldConfig
        {
            world = "MV-1320 Test World",
            dials = new WorldDials
            {
                areaCount = 1, baseThreat = 0f, threatGrowth = 0f,
                pacingRhythm = new[] { 1f }, powerupCadence = 99,
            },
            areas = new[]
            {
                new WorldArea
                {
                    id = "stub", index = 0, role = "entry",
                    origin = new WorldAreaOrigin { x = OriginX - 2f, z = OriginZ - 6f },
                    size = new WorldAreaSize { w = 4f, d = 6f },
                },
                new WorldArea
                {
                    id = "a1", index = 1, role = "normal",
                    origin = new WorldAreaOrigin { x = OriginX - 10f, z = OriginZ },
                    size = new WorldAreaSize { w = 20f, d = 20f },
                    cover = new[]
                    {
                        new WorldCover
                        {
                            id = "a1_secret1", x = OriginX, z = OriginZ + 10f,
                            width = 2f, height = 1.5f, depth = 1f, shape = "box", hp = 50f, secret = true,
                        },
                    },
                },
            },
            gates = new[]
            {
                new WorldGate
                {
                    id = "g0", width = 3f, opensWith = "start",
                    from = new WorldGateEndpoint { area = "stub", wall = "N", pos = 0.5f },
                    to = new WorldGateEndpoint { area = "a1", wall = "S", pos = 0.5f },
                },
            },
        };

        [Test]
        public void BreakingASecret_PlacesItsCache_CollectingMarksFound_AndRestoreNeverDuplicatesIt() // Guards MV-1320
        {
            WorldConfig cfg = BuildWorld();
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason), loadReason);

            _root = new GameObject("MV-1320 Root");
            MapBuild built = MapRuntime.Build(map, _root.transform);
            Assert.AreEqual(1, built.Cover.Count, "setup failure: fixture must build exactly one cover piece");

            DestructibleCover cover = built.Cover[0].Body.GetComponent<DestructibleCover>();
            Assert.IsNotNull(cover, "setup failure: an hp-50 secret cover piece must carry a DestructibleCover component");

            // AddComponent's own Awake never runs outside Play mode (DestructibleCover.Build's own doc
            // comment) — build it explicitly, same idiom MV1298DestructibleCoverTests already uses.
            cover.Build();
            Assert.IsTrue(cover.IsAlive, "setup failure: a freshly built cover piece must start alive");

            PickupDirector director = PickupDirector.EnsureInstalled();
            Vector2 centreXz = new Vector2(cover.transform.position.x, cover.transform.position.z);

            // === AC1: 50 damage on a 50 HP secret piece breaks it and places 1 Supercell + 4 PowerCells
            // at its centre ===
            cover.TakeDamage(new DamageInfo(50f, cover.transform.position, Vector3.down, Team.Player));
            Assert.IsFalse(cover.IsAlive, "MV-1320 AC1: 50 damage on a 50 HP secret piece must break it");

            List<Pickup> nearCentre = LivePickupsNear(director, centreXz);
            Assert.AreEqual(1, Count(nearCentre, PickupKind.Supercell),
                "MV-1320 AC1: breaking a secret must place exactly one Supercell at its centre");
            Assert.AreEqual(4, Count(nearCentre, PickupKind.PowerCell),
                "MV-1320 AC1: breaking a secret must place exactly 4 PowerCells (Parts) at its centre");

            Pickup supercell = Find(nearCentre, PickupKind.Supercell);
            Assert.AreEqual(cover.Id, supercell.SecretId,
                "MV-1320 AC1: the placed Supercell must carry the secret's own id");

            // === Collecting the Supercell marks the secret FOUND ===
            InvokeCollect(director, supercell);
            Assert.AreEqual(1, SecretCensus.Found(0),
                "MV-1320 AC1: collecting a secret's Supercell must mark it FOUND");

            // === Save/restore round-trip: capture BEFORE the fresh rebuild below, since MapRuntime.Build
            // resets both SecretCensus and DestructibleCover's own registry on every build (the same "a
            // level loaded a second time must count its own, not the previous level's ghosts" reasoning
            // every census in this codebase already follows). ===
            string[] foundIds = SecretCensus.FoundIds();
            string[] destroyedIds = DestructibleCover.DestroyedIds();
            Assert.AreEqual(1, foundIds.Length, "setup failure: exactly one secret must have recorded FOUND");
            Assert.AreEqual(1, destroyedIds.Length, "setup failure: exactly one cover piece must have recorded broken");

            int pickupCountBeforeRestore = LivePickupsNear(director, centreXz).Count;

            MapBuild rebuilt = MapRuntime.Build(map, _root.transform);
            DestructibleCover freshCover = rebuilt.Cover[0].Body.GetComponent<DestructibleCover>();
            freshCover.Build();
            Assert.IsTrue(freshCover.IsAlive,
                "setup failure: a freshly rebuilt secret piece must start alive before the checkpoint restore applies");

            // Same order MaxWorlds.Save.SaveSystem.RestoreCheckpoint calls these in — SecretCensus
            // restores FIRST so DestructibleCover.ApplyCheckpointDestroyedIds already sees this secret as
            // found and refuses to re-place its cache.
            SecretCensus.ApplyCheckpointFoundIds(foundIds);
            DestructibleCover.ApplyCheckpointDestroyedIds(destroyedIds);

            Assert.IsFalse(freshCover.IsAlive,
                "MV-1320 AC1: a checkpoint restore must re-apply the broken state onto the matching piece");
            Assert.AreEqual(1, SecretCensus.Found(0), "MV-1320 AC1: FOUND must survive the save/restore round-trip");

            int pickupCountAfterRestore = LivePickupsNear(director, centreXz).Count;
            Assert.AreEqual(pickupCountBeforeRestore, pickupCountAfterRestore,
                "MV-1320 AC1: a restore of an already-FOUND secret must place no second cache");
        }

        private static List<Pickup> LivePickupsNear(PickupDirector director, Vector2 centreXz)
        {
            var live = (IList)typeof(PickupDirector)
                .GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(director);
            var result = new List<Pickup>();
            foreach (object o in live)
            {
                var p = (Pickup)o;
                if (p == null) continue;
                Vector2 pXz = new Vector2(p.transform.position.x, p.transform.position.z);
                if (Vector2.Distance(pXz, centreXz) <= NearRadius) result.Add(p);
            }
            return result;
        }

        private static int Count(List<Pickup> pickups, PickupKind kind)
        {
            int n = 0;
            foreach (Pickup p in pickups) if (p.Kind == kind) n++;
            return n;
        }

        private static Pickup Find(List<Pickup> pickups, PickupKind kind)
        {
            foreach (Pickup p in pickups) if (p.Kind == kind) return p;
            return null;
        }

        private static void InvokeCollect(PickupDirector director, Pickup pickup)
        {
            var liveField = typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance);
            var live = (IList)liveField.GetValue(director);
            int index = live.IndexOf(pickup);
            Assert.GreaterOrEqual(index, 0, "the collected pickup must still be live on the director");
            typeof(PickupDirector).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { index, pickup });
        }
    }
}
