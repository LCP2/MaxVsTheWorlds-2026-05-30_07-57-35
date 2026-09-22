using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-896 (Lee, 2026-09-22, live build): Sentinels following Max onto a raised World 2 deck settle
    /// at the wrong height — hovering off the deck's own edge, or appearing above a wall. MV-864 already
    /// fixed <see cref="Sentinel.SnapToLevelSurface"/>/<see cref="MapData.SnapToWalkableSurface"/> for
    /// a15_deck1's authoring shape (a15/a13, an MV-697 level&gt;0 overlay zone sharing its floor's own
    /// footprint) but never for a10_deck1/a11_deck1/a12_deck1's shape (MV-692: a Deck entity authored
    /// directly inside its OWN level-0 area, no overlay) — those three decks' containing zone is
    /// <c>level == 0</c>, so <c>SnapToWalkableSurface</c>'s old <c>zone.level == 0</c> branch read them
    /// as plain floor and left XZ completely unclamped. See the fix comment for the exact quoted
    /// failure and all four decks' numbers.
    ///
    /// One consolidated EditMode test (testing policy MV-465, Rule 1): builds World 2 through the real
    /// <see cref="WorldMapLoader"/>/<see cref="MapRuntime"/> path (so this proves the actual shipped
    /// config, not a hand-built fixture) and, for a Sentinel whose follow target stands on each of
    /// a10_deck1/a11_deck1/a12_deck1/a15_deck1, asserts the RESOLVED value <see cref="Sentinel.SnapToLevelSurface"/>
    /// itself returns (Tier 2: a resolved value, never an authored constant) for a follow-step candidate
    /// 2.5 m off the deck's own centreline in Z — its own narrow (3 m-deep) axis, the exact shape a
    /// standoff/sidestep/separation step computes when holding station beside Max rather than directly
    /// behind him along the deck's long axis: inside the deck's own rect in XZ, within 0.1 m of the
    /// deck's own top Y, and never inside any real <see cref="StructuralWall"/> collider's bounds.
    ///
    /// Deliberately does NOT drive this through <c>TickMovement</c>'s own <c>CharacterController.SafeMove</c>
    /// — a sentinel already correctly resolved to the deck's edge and a candidate beyond it are on
    /// OPPOSITE sides of the deck's own solid MV-852 parapet (every one of these four decks is authored
    /// <c>"walled": true</c>), so a swept recovery move would legitimately be blocked by that parapet
    /// exactly the way MV-624's own tests already prove SafeMove blocks a wall — a confound that has
    /// nothing to do with this ticket's own defect (the RESOLUTION math, not collision). Calling
    /// <c>SnapToLevelSurface</c> directly asserts the one function this ticket actually fixes.
    /// </summary>
    public sealed class MV896SentinelDeckFollowHeightTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo SnapToLevelSurfaceMethod =
            typeof(Sentinel).GetMethod("SnapToLevelSurface", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly string[] DeckIds = { "a10_deck1", "a11_deck1", "a12_deck1", "a15_deck1" };

        [SetUp]
        [TearDown]
        public void Clear()
        {
            Sentinel.DestroyAllActive();
            Sentinel.ResetRegistry();
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            DevTuning.Reset();
        }

        [Test]
        public void FollowStepCandidateOffADecksCentrelineResolvesOntoTheDeckNotOffItOrOnAWall()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");
            Assert.IsNotNull(SnapToLevelSurfaceMethod, "Sentinel.SnapToLevelSurface went missing — this ticket's own fix target can't be reflected into");

            var root = new GameObject("MV896 World2 Root").transform;
            GameObject pathGo = null;
            var spawned = new List<GameObject>();

            try
            {
                pathGo = new GameObject("MV896-backyard-path");
                var path = pathGo.AddComponent<BackyardPath>();
                BackyardPathMapField.SetValue(path, map);

                MapRuntime.Build(map, root);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                List<Collider> wallColliders = root.GetComponentsInChildren<StructuralWall>(true)
                    .Select(w => w.GetComponent<Collider>())
                    .Where(c => c != null && c.enabled)
                    .ToList();
                Assert.IsNotEmpty(wallColliders, "World 2 must have built at least one StructuralWall for this check to mean anything");

                var failures = new List<string>();

                foreach (string deckId in DeckIds)
                {
                    MapEntity deck = map.Entity(deckId);
                    Assert.IsNotNull(deck, $"MV-896: world2_config.json must still author '{deckId}'");

                    var followGo = new GameObject($"Max-{deckId}");
                    spawned.Add(followGo);
                    followGo.transform.position = new Vector3(deck.x, deck.height, deck.z);

                    var sentinelGo = new GameObject($"Sentinel-{deckId}");
                    spawned.Add(sentinelGo);
                    var sentinel = sentinelGo.AddComponent<Sentinel>();
                    sentinel.Init(new Vector3(deck.x, deck.height, deck.z),
                        maxHp: 999f, range: 7f, fireInterval: 0.6f,
                        moveSpeed: 3f, standoffDistance: AbilityTuning.DefaultSentinelStandoffDistance,
                        followTarget: followGo.transform);

                    float offsetZ = AbilityTuning.DefaultSentinelStandoffDistance;
                    var candidate = new Vector3(deck.x, deck.height, deck.z + offsetZ);
                    var p = (Vector3)SnapToLevelSurfaceMethod.Invoke(sentinel, new object[] { candidate });

                    float halfW = deck.width * 0.5f, halfD = deck.depth * 0.5f;

                    if (p.x < deck.x - halfW || p.x > deck.x + halfW || p.z < deck.z - halfD || p.z > deck.z + halfD)
                        failures.Add($"{deckId}: resolved position {p} is outside the deck's own rect " +
                                     $"x[{deck.x - halfW:0.##},{deck.x + halfW:0.##}] z[{deck.z - halfD:0.##},{deck.z + halfD:0.##}]");

                    if (Mathf.Abs(p.y - deck.height) > 0.1f)
                        failures.Add($"{deckId}: resolved Y {p.y:0.##} is not within 0.1m of the deck's own top ({deck.height})");

                    Collider hitWall = wallColliders.FirstOrDefault(w => w.bounds.Contains(p));
                    if (hitWall != null)
                        failures.Add($"{deckId}: resolved position {p} lands inside wall collider '{hitWall.name}'");
                }

                Assert.IsTrue(failures.Count == 0, "MV-896 sentinel deck-follow failures:\n" + string.Join("\n", failures));
            }
            finally
            {
                Sentinel.DestroyAllActive();
                foreach (GameObject go in spawned) if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(root.gameObject);
                if (pathGo != null) Object.DestroyImmediate(pathGo);
            }
        }
    }
}
