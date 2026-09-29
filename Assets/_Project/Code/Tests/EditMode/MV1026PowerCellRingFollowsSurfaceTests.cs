using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1026 — Lee, TestFlight, World 2, 2026-09-29: "Some power cells don't show now. I just see the
    /// orange circle base." Root cause, read from `main` `967d1be`: <c>PickupArtDirector.ShowRing</c>
    /// pinned every ring to a fixed <c>y=0</c> floor plane, discarding the pickup's own surface height —
    /// since MV-1001 put a deck drop's PROP at the deck's own height, a deck drop's ring stayed on the
    /// floor below while its prop sat on the deck above, and the two read as two separate things on
    /// screen (case iv below, the one the ticket names as guaranteed to fail). Separately,
    /// <c>BuildArtState</c> called <c>HideGreybox</c> even when <c>Build</c> returned null art, which
    /// would leave nothing but the ring for any kind <c>WeaponPartArt.Build</c> ever refused.
    ///
    /// EditMode only, against World 2's real shipped config (`world2_config.json`) — same
    /// WorldLibrary/WorldMapLoader/BackyardPath._map seeding idiom as
    /// <see cref="MV1001PickupDeckHeightAndLevelTests"/> and <see cref="MV898GroundRingSurfaceHeightTests"/>
    /// (the sibling fix for the exact same fixed-floor-plane bug on <c>GroundAnchorVfx</c>). Reflection
    /// drives <c>PickupDirector.SpawnDrop</c>/<c>RetireCell</c>/<c>Update</c> and
    /// <c>PickupArtDirector.Awake</c>/<c>OnEnable</c>/<c>Update</c> directly — same idiom as
    /// <see cref="MV988PickupGreyboxRegateTests"/> and <see cref="MV626PickupArtCachingTests"/>.
    ///
    /// One consolidated test (MV-465 Rule 1) walking all six authored cases for both
    /// <see cref="PickupKind.PowerCellSecondary"/> (the reported kind) and <see cref="PickupKind.PowerCell"/>
    /// (for comparison, per the ticket). Each case gets its own fresh <c>PickupDirector</c>/
    /// <c>PickupArtDirector</c>/Max so a magneto pull or a walk-over collect in one case can never perturb
    /// a pickup left over from another. Tier 2 throughout: every assertion reads a resolved value off the
    /// real built hierarchy (a renderer's bounds, a ring's world position) — never an authored constant,
    /// never a rendered pixel.
    ///
    /// Fails on 967d1be — see the fix comment for the exact quoted failure (case iv, the deck drop: the
    /// ring resolves to y=Lift while the pickup/art sit at deckHeight+FloatHeight).
    /// </summary>
    // Guards MV-1026
    public sealed class MV1026PowerCellRingFollowsSurfaceTests
    {
        private static readonly FieldInfo BackyardPathMapField =
            typeof(BackyardPath).GetField("_map", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly float RingLiftValue =
            (float)typeof(PickupArtDirector).GetField("RingLift", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static readonly float ScatterRadiusValue =
            (float)typeof(PickupDirector).GetField("ScatterRadius", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private GameObject _pathGo;

        [SetUp]
        public void SetUp()
        {
            EnemyNavigation.Reset();
            PickupWallet.Reset();
            RigState.Reset();
            RigBoard.ResetForTests();
            Pickup.ResetRegistry();
            foreach (var d in UnityEngine.Object.FindObjectsByType<PickupDirector>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(d.gameObject);
            foreach (var a in UnityEngine.Object.FindObjectsByType<PickupArtDirector>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(a.gameObject);
            foreach (var p in UnityEngine.Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(p.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            if (_pathGo != null) { UnityEngine.Object.DestroyImmediate(_pathGo); _pathGo = null; }
            foreach (var p in UnityEngine.Object.FindObjectsByType<Pickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(p.gameObject);
            EnemyNavigation.Reset();
            PickupWallet.Reset();
            RigState.Reset();
            RigBoard.ResetForTests();
            Pickup.ResetRegistry();
        }

        [Test]
        public void RingAndArtStayOnTheSameSurface_AcrossEveryDropAndReusePath() // Guards MV-1026
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.IsNotNull(BackyardPathMapField, "BackyardPath._map went missing — EnemyNavigation.Map can't be seeded");

            MapEntity deck = map.Entity("a12_deck1");
            Assert.IsNotNull(deck, "MV-1026: world2_config.json must still author a12's own deck");
            Assert.Greater(deck.height, 0f,
                "setup failure: a12_deck1 must be elevated above the floor for this to be a floor/deck test at all");
            float halfDepth = deck.depth * 0.5f;

            _pathGo = new GameObject("MV1026-backyard-path");
            var path = _pathGo.AddComponent<BackyardPath>();
            BackyardPathMapField.SetValue(path, map);

            RigBoard.UseWorld(1);   // World 2 board — both e_mag and e_cmg live here (case vi needs each)

            foreach (PickupKind kind in new[] { PickupKind.PowerCellSecondary, PickupKind.PowerCell })
            {
                string magnetoNodeId = kind == PickupKind.PowerCellSecondary ? "e_cmg" : "e_mag";

                RunCase(map, kind, "(i) a fresh drop on the floor", (director, max) =>
                {
                    Pickup p = SpawnDropAt(director, kind, new Vector3(100f, 0f, 100f));
                    Assert.IsNotNull(p, "setup failure: SpawnDrop refused a fresh floor drop");
                    return p;
                });

                RunCase(map, kind, "(ii) a pooled reuse after a walk-over collect", (director, max) =>
                {
                    Pickup p = SpawnDropAt(director, kind, new Vector3(-100f, 0f, 100f));
                    Assert.IsNotNull(p, "setup failure: SpawnDrop refused the first drop");
                    max.transform.position = p.transform.position;
                    InvokePrivate(director, "Update");
                    Assert.IsFalse(p.gameObject.activeSelf, "setup failure: the walk-over collect never fired");

                    p.Place(new Vector3(-100f, 0f, 105f));   // the pooled reuse under test
                    EnsureRegistered(p);
                    return p;
                });

                RunCase(map, kind, "(iii) a pooled reuse after RetireCell (cap eviction / lifetime)", (director, max) =>
                {
                    Pickup p = SpawnDropAt(director, kind, new Vector3(100f, 0f, -100f));
                    Assert.IsNotNull(p, "setup failure: SpawnDrop refused the first drop");
                    List<Pickup> live = LiveList(director);
                    int index = live.IndexOf(p);
                    Assert.That(index, Is.GreaterThanOrEqualTo(0), "setup failure: the drop never joined _live");

                    typeof(PickupDirector).GetMethod("RetireCell", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(director, new object[] { index, p });
                    Assert.IsFalse(p.gameObject.activeSelf, "setup failure: RetireCell never deactivated the pickup");

                    p.Place(new Vector3(100f, 0f, -95f));   // the pooled reuse under test
                    EnsureRegistered(p);
                    return p;
                });

                RunCase(map, kind, "(iv) a drop from a robot killed on a deck", (director, max) =>
                {
                    Pickup p = SpawnDropAt(director, kind, new Vector3(deck.x, deck.height, deck.z));
                    Assert.IsNotNull(p, "setup failure: SpawnDrop refused the deck drop");
                    return p;
                });

                RunCase(map, kind, "(v) a drop scattered off a deck edge (ScatterRadius)", (director, max) =>
                {
                    // A death near the deck's own edge, then the real per-drop scatter offset
                    // (PickupDirector.ScatterRadius — the same one OnRobotDied applies) pushed purely
                    // along Z, past the deck's own authored rect. The death Y survives the scatter
                    // untouched (OnRobotDied's offset never touches Y) — only XZ moves.
                    float nearEdgeZ = deck.z + halfDepth - 0.3f;
                    var scattered = new Vector3(deck.x, deck.height, nearEdgeZ + ScatterRadiusValue);
                    Assert.Greater(Mathf.Abs(scattered.z - deck.z), halfDepth,
                        "test setup: the scattered drop must land outside the deck's own rect in Z");

                    Pickup p = SpawnDropAt(director, kind, scattered);
                    Assert.IsNotNull(p, "setup failure: SpawnDrop refused the scattered drop");
                    return p;
                });

                RunCase(map, kind, "(vi) a drop while Magneto pull is active", (director, max) =>
                {
                    RigState.UnlockCategory("ENERGY");
                    Assert.IsTrue(RigState.AcquireCap("e_cel"), "sanity: e_cel must be acquirable once ENERGY is unlocked");
                    Assert.IsTrue(RigState.AcquireCap("e_mag"), "sanity: e_mag must be acquirable once e_cel is owned");
                    if (magnetoNodeId == "e_cmg")
                        Assert.IsTrue(RigState.AcquireCap("e_cmg"), "sanity: e_cmg must be acquirable once e_mag is owned");

                    // 2.5m out: inside the level-1 pull radius (3m base) but outside the 1.4m walk-over
                    // CollectRadius, so only the Magneto pull branch is under test.
                    var spawnPos = new Vector3(2.5f, 0f, 0f);
                    Pickup p = SpawnDropAt(director, kind, spawnPos);
                    Assert.IsNotNull(p, "setup failure: SpawnDrop refused the drop");
                    max.transform.position = Vector3.zero;

                    Vector3 before = p.transform.position;
                    InvokePrivate(director, "Update");
                    Assert.Greater(Vector3.Distance(p.transform.position, before), 0f,
                        $"setup failure: {magnetoNodeId} at level 1 never pulled the pickup — case (vi) needs " +
                        "real motion to mean anything");
                    return p;
                });
            }
        }

        /// <summary>Builds a fresh <see cref="PickupDirector"/>/<see cref="PickupArtDirector"/>/Max trio,
        /// runs <paramref name="scenario"/> against them to produce the pickup under test, drives one
        /// <c>PickupArtDirector.Update()</c> to resolve its ring, asserts the case, then tears the trio
        /// down — so a magneto pull or a walk-over collect from one case can never perturb a pickup a
        /// previous case already asserted against.</summary>
        private static void RunCase(MapData map, PickupKind kind, string caseLabel,
            Func<PickupDirector, GameObject, Pickup> scenario)
        {
            var directorGo = new GameObject("MV1026 PickupDirector");
            PickupDirector director = directorGo.AddComponent<PickupDirector>();

            var artGo = new GameObject("MV1026 PickupArt");
            PickupArtDirector artDirector = artGo.AddComponent<PickupArtDirector>();
            InvokePrivate(artDirector, "Awake");
            InvokePrivate(artDirector, "OnEnable");

            var maxGo = new GameObject("MV1026 Max") { tag = "Player" };
            maxGo.transform.position = new Vector3(1000f, 0f, 1000f);   // out of every case's way by default

            string label = $"{kind} {caseLabel}";
            try
            {
                Pickup pickup = scenario(director, maxGo);
                Assert.IsNotNull(pickup, $"setup failure [{label}]: the scenario produced no pickup");
                Assert.AreEqual(kind, pickup.Kind, $"setup failure [{label}]: wrong pickup kind under test");

                InvokePrivate(artDirector, "Update");

                AssertRingFollowsArt(artDirector, pickup, map, label);
            }
            finally
            {
                // MV-1026: DestroyImmediate does not reliably fire OnDisable synchronously under
                // -batchmode -runTests (the same quirk EnsureRegistered's doc comment names for OnEnable),
                // so the manual OnEnable above needs a manual, matching OnDisable here or its
                // Pickup.Registered subscription leaks past this case into every later fixture in the
                // run. -= on an already-unsubscribed handler is a harmless no-op, so this is safe even on
                // the runs where Unity does fire OnDisable naturally.
                InvokePrivate(artDirector, "OnDisable");
                UnityEngine.Object.DestroyImmediate(maxGo);
                UnityEngine.Object.DestroyImmediate(artGo);
                UnityEngine.Object.DestroyImmediate(directorGo);   // takes every pickup parented under it too
                Pickup.ResetRegistry();
                PickupWallet.Reset();
            }
        }

        private static void AssertRingFollowsArt(PickupArtDirector artDirector, Pickup pickup, MapData map, string label)
        {
            object state = ArtStateFor(artDirector, pickup);
            Assert.IsNotNull(state, $"MV-1026 [{label}]: the pickup never got an ArtState — BuildArtState didn't run");

            Transform art = ArtOf(state);
            Assert.IsNotNull(art, $"MV-1026 [{label}]: no art child was built for this drop");

            MeshRenderer[] renderers = art.GetComponentsInChildren<MeshRenderer>(true);
            Assert.Greater(renderers.Length, 0, $"MV-1026 [{label}]: the art carries no MeshRenderer to check");

            Bounds artBounds = renderers[0].bounds;
            foreach (MeshRenderer r in renderers)
            {
                Assert.IsTrue(r.gameObject.activeInHierarchy,
                    $"MV-1026 [{label}]: art renderer '{r.name}' is not active in hierarchy");
                Assert.IsTrue(r.enabled, $"MV-1026 [{label}]: art renderer '{r.name}' is disabled");
                artBounds.Encapsulate(r.bounds);
            }

            float surfaceY = map.SurfaceHeightAt(pickup.transform.position);

            Assert.That(artBounds.center.y, Is.GreaterThanOrEqualTo(surfaceY + 0.1f),
                $"MV-1026 [{label}]: art bounds centre y ({artBounds.center.y:0.###}) must sit at least " +
                $"0.1m above the surface ({surfaceY:0.###})");

            GroundRing ring = RingOf(state);
            Assert.IsNotNull(ring, $"MV-1026 [{label}]: no ring was ever placed for this pickup");

            float xzDistance = Vector2.Distance(
                new Vector2(artBounds.center.x, artBounds.center.z),
                new Vector2(ring.transform.position.x, ring.transform.position.z));
            Assert.That(xzDistance, Is.LessThanOrEqualTo(0.3f),
                $"MV-1026 [{label}]: the art (XZ {artBounds.center.x:0.##},{artBounds.center.z:0.##}) sits " +
                $"{xzDistance:0.###}m from its own ring (XZ {ring.transform.position.x:0.##},{ring.transform.position.z:0.##})");

            float expectedRingY = surfaceY + RingLiftValue;
            Assert.That(ring.transform.position.y, Is.EqualTo(expectedRingY).Within(0.01f),
                $"MV-1026 [{label}]: ring y ({ring.transform.position.y:0.###}) must equal the pickup's own " +
                $"surface height ({surfaceY:0.###}) + RingLift ({RingLiftValue})");
        }

        private static Pickup SpawnDropAt(PickupDirector director, PickupKind kind, Vector3 pos)
        {
            var p = (Pickup)typeof(PickupDirector).GetMethod("SpawnDrop", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, new object[] { kind, pos, default(MaxWorlds.Upgrades.PartKind), default(AbilityKind) });
            if (p != null) EnsureRegistered(p);
            return p;
        }

        /// <summary>Unity does not reliably fire a freshly-<c>AddComponent</c>'d MonoBehaviour's
        /// <c>OnEnable</c> synchronously under <c>-batchmode -runTests -testPlatform EditMode</c> — the
        /// same quirk <see cref="MV626PickupArtCachingTests"/>'s own doc comment names, worked around
        /// there by invoking <c>Pickup.OnEnable</c> by hand. Without this, a freshly-created pickup never
        /// joins <see cref="Pickup.Active"/>, so <c>PickupArtDirector.Update()</c>'s
        /// <c>foreach (var pickup in Pickup.Active)</c> loop never reaches it and no ring ever gets
        /// built — a gap in this test's own harness, not the production bug under test (a live game
        /// always reaches this through a real scene, where OnEnable fires normally). Idempotent: skips
        /// the call entirely when the pickup is already registered, so it can never double-add into the
        /// list <c>OnDisable</c> only removes one entry from.</summary>
        private static void EnsureRegistered(Pickup pickup)
        {
            if (!Pickup.Active.Contains(pickup))
                InvokePrivate(pickup, "OnEnable");
        }

        private static List<Pickup> LiveList(PickupDirector director) =>
            (List<Pickup>)typeof(PickupDirector).GetField("_live", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);

        private static void InvokePrivate(object target, string methodName) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static object ArtStateFor(PickupArtDirector director, Pickup pickup)
        {
            var dict = (IDictionary)typeof(PickupArtDirector)
                .GetField("_artState", BindingFlags.NonPublic | BindingFlags.Instance)
                .GetValue(director);
            return dict.Contains(pickup) ? dict[pickup] : null;
        }

        private static Transform ArtOf(object artState) =>
            (Transform)artState.GetType().GetField("Art").GetValue(artState);

        private static GroundRing RingOf(object artState) =>
            (GroundRing)artState.GetType().GetField("Ring").GetValue(artState);
    }
}
