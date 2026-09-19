using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-821: World 2's deck rails (<c>MapRuntime.BuildDeckRail</c>, 1.5 m tall, collider stripped)
    /// read as a walled block even though Max walks straight under and off them — the only deck
    /// collider is a 0.15 m top slab, so everything below 2.35 m is open floor. Lee's rule: anywhere
    /// Max can walk must look walkable. Fails on 302e10e: the rail stands 1.5 m tall (over the
    /// ticket's own 0.10 m cap) and no support-post renderer exists at all.
    ///
    /// One consolidated test (testing policy v2, MV-465), asserting RESOLVED values only (Rule 2,
    /// Tier 2) against World 2's own shipped a21 (an ordinary, non-walled deck): (a) nothing built for
    /// a deck edge rises more than 0.10 m above the deck top; (b) every deck has post renderers at each
    /// corner and at no more than 4 m spacing along each edge; (c) nothing but those posts (and the
    /// deliberate flush ground-shadow decal) occupies the open volume between the floor and the slab
    /// underside inside the deck's own footprint; (d) the deck colliders are untouched.
    ///
    /// Retargeted by MV-852 (World 2 re-layout): a3 no longer authors any deck at all (its ramps/decks
    /// were removed so the only way up is the new Replicator door into a12), and every deck this ticket
    /// touches (a12/a15/a16/a17/a18/a14/a19/a20) is now deliberately `walled` — a real 1.0 m parapet,
    /// not the open 0.10 m band this test guards. a21 is untouched by MV-852 and stays a plain, unwalled
    /// deck, so it's still a faithful stand-in for "every deck NOT authored `walled`" everywhere else
    /// in the game.
    /// </summary>
    public sealed class MV821DeckWalkwayTests
    {
        private const float MaxEdgeRise = 0.10f;
        private const float MaxPostSpacing = 4.0f;
        private const float MaxPostSize = 0.2f + 0.01f; // a hair of slack for float rounding
        private const float InfillEpsilon = 0.05f; // above this a renderer no longer reads as the flush ground-shadow decal

        [Test]
        public void WorldTwoArea21Decks_ReadAsOpenWalkwaysWithColliderUntouched()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            List<DeckSlab> a21Decks = MapGeometry.Decks(map).Where(d => d.Id.StartsWith("a21_deck")).ToList();
            Assert.IsNotEmpty(a21Decks, "World 2's a21 must author at least one deck for this test to mean anything");

            var host = new GameObject("MV821 host").transform;
            try
            {
                MapRuntime.Build(map, host);

                var failures = new List<string>();
                Transform[] allChildren = host.GetComponentsInChildren<Transform>(true);

                foreach (DeckSlab deck in a21Decks)
                {
                    // (d) the deck's own top-collider slab is byte-for-byte untouched.
                    Transform body = allChildren.FirstOrDefault(t => t.name == deck.Id);
                    Assert.IsNotNull(body, $"{deck.Id}: deck body was never built");
                    BoxCollider collider = body.GetComponent<BoxCollider>();
                    Assert.IsNotNull(collider, $"{deck.Id}: deck body must carry its top-collider slab");
                    Vector3 colliderCenter = body.position + collider.center;
                    Assert.AreEqual(deck.Size.x, collider.size.x, 0.001f, $"{deck.Id}: collider width changed");
                    Assert.AreEqual(deck.Size.y, collider.size.y, 0.001f, $"{deck.Id}: collider thickness changed");
                    Assert.AreEqual(deck.Size.z, collider.size.z, 0.001f, $"{deck.Id}: collider depth changed");
                    Assert.AreEqual(deck.Center.x, colliderCenter.x, 0.001f, $"{deck.Id}: collider x moved");
                    Assert.AreEqual(deck.Center.y, colliderCenter.y, 0.001f, $"{deck.Id}: collider height moved");
                    Assert.AreEqual(deck.Center.z, colliderCenter.z, 0.001f, $"{deck.Id}: collider z moved");

                    float halfW = deck.Size.x * 0.5f, halfD = deck.Size.z * 0.5f;
                    float underside = deck.TopY - MapGeometry.DeckThickness;
                    var footprint = new Rect(deck.Center.x - halfW, deck.Center.z - halfD, deck.Size.x, deck.Size.z);

                    var built = allChildren.Where(t => t.name.StartsWith(deck.Id + "_"))
                        .Select(t => (name: t.name, r: t.GetComponent<Renderer>()))
                        .Where(p => p.r != null)
                        .ToList();
                    Assert.IsNotEmpty(built, $"{deck.Id}: no edge/post/beam geometry was built at all");

                    // (a) nothing built for this deck's edge rises more than 0.10 m above the deck top.
                    foreach (var (name, r) in built)
                    {
                        float riseAboveTop = r.bounds.max.y - deck.TopY;
                        if (riseAboveTop > MaxEdgeRise + 0.001f)
                            failures.Add($"{name}: rises {riseAboveTop:F3} m above the deck top (max {MaxEdgeRise:F2} m)");
                    }

                    // (b) support posts at every corner and at <= 4 m spacing along each edge.
                    var posts = built.Where(p => p.name.Contains("_post")).ToList();
                    Assert.IsNotEmpty(posts, $"{deck.Id}: expected support-post renderers");

                    foreach (var (name, r) in posts)
                        if (r.bounds.size.x > MaxPostSize || r.bounds.size.z > MaxPostSize)
                            failures.Add($"{name}: post footprint {r.bounds.size.x:F2}x{r.bounds.size.z:F2} exceeds 0.2 m square");

                    var postXZ = posts.Select(p => new Vector2(p.r.bounds.center.x, p.r.bounds.center.z)).ToList();
                    Vector2[] corners =
                    {
                        new Vector2(deck.Center.x - halfW, deck.Center.z - halfD),
                        new Vector2(deck.Center.x + halfW, deck.Center.z - halfD),
                        new Vector2(deck.Center.x + halfW, deck.Center.z + halfD),
                        new Vector2(deck.Center.x - halfW, deck.Center.z + halfD),
                    };
                    foreach (Vector2 corner in corners)
                        if (!postXZ.Any(p => Vector2.Distance(p, corner) < 0.1f))
                            failures.Add($"{deck.Id}: no support post at corner ({corner.x:F1},{corner.y:F1})");

                    var edges = new (float from, float to, float fixedCoord, bool alongX)[]
                    {
                        (deck.Center.x - halfW, deck.Center.x + halfW, deck.Center.z - halfD, true),
                        (deck.Center.x - halfW, deck.Center.x + halfW, deck.Center.z + halfD, true),
                        (deck.Center.z - halfD, deck.Center.z + halfD, deck.Center.x - halfW, false),
                        (deck.Center.z - halfD, deck.Center.z + halfD, deck.Center.x + halfW, false),
                    };
                    foreach (var edge in edges)
                    {
                        List<float> onEdge = postXZ
                            .Where(p => edge.alongX ? Mathf.Abs(p.y - edge.fixedCoord) < 0.1f : Mathf.Abs(p.x - edge.fixedCoord) < 0.1f)
                            .Select(p => edge.alongX ? p.x : p.y)
                            .OrderBy(v => v)
                            .ToList();
                        for (int i = 1; i < onEdge.Count; i++)
                        {
                            float gap = onEdge[i] - onEdge[i - 1];
                            if (gap > MaxPostSpacing + 0.01f)
                                failures.Add($"{deck.Id}: post spacing {gap:F2} m exceeds {MaxPostSpacing:F1} m on an edge");
                        }
                    }

                    // (c) nothing else built for this deck (other than posts, and the deliberate flush
                    // ground-shadow decal) occupies the open volume between the floor and the slab underside.
                    foreach (var (name, r) in built)
                    {
                        if (name.Contains("_post") || name.Contains("_ground_shadow")) continue;

                        bool overlapsFootprintXZ = r.bounds.min.x < footprint.xMax && r.bounds.max.x > footprint.xMin
                                                 && r.bounds.min.z < footprint.yMax && r.bounds.max.z > footprint.yMin;
                        bool intrudesUnderside = r.bounds.min.y < underside - 0.001f && r.bounds.max.y > InfillEpsilon;

                        if (overlapsFootprintXZ && intrudesUnderside)
                            failures.Add($"{name}: occupies the open volume under {deck.Id} between the floor and the slab underside");
                    }
                }

                Assert.IsEmpty(failures, $"MV-821 deck-walkway violations ({failures.Count}):\n" + string.Join("\n", failures));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
