using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1087: Lee, on device, saw Max's legs sink through World 2's channel kerbs and plank
    /// crossings around every sludge channel — <see cref="MaxWorlds.Rendering.StormdrainKit"/> built
    /// both as render-only boxes with no collider, so nothing ever stopped a mover walking straight
    /// through. Fails on base commit d96848d (current main tip, carrying the same unfixed geometry the
    /// ticket's own named base d30d293 shipped): "Channel Kerb A (under a10_sludge1) carries no collider
    /// -- Max's legs sink straight through it, the exact MV-1087 defect / Expected: not null / But was:
    /// null" — see the fix comment for the full captured output.
    ///
    /// One consolidated test (testing policy MV-465, Rule 1), asserting RESOLVED values (Rule 2, Tier
    /// 2) through the real World 2 build (<c>WorldMapLoader</c>/<c>MapRuntime</c>/
    /// <c>StormdrainDressing</c>, the same path MV801ChannelTests/MV831World2ColliderAuditTests already
    /// use) against area a10's own real, authored sludge channels: a body-sized sphere cast dropped
    /// over five real kerb pieces and two real plank-crossing decks lands on the piece's own resolved
    /// collider top; <c>MapSludgeDamage.IsInFloorSludge</c> reads false at each of those landed points
    /// and true at the owning sludge rect's own centre at floor height; and a real CharacterController
    /// (Max's own default configuration — <c>PlayerController</c> never touches <c>stepOffset</c>)
    /// walked from dry floor across one real kerb ends grounded on the far side, having peaked within
    /// 3 cm of that kerb's own resolved top.
    /// </summary>
    public sealed class MV1087ChannelWalkwayTests
    {
        [Test]
        public void ChannelKerbsAndCrossings_AreWalkableFloor_NotSludge()
        {
            WorldConfig w2cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(w2cfg, "World 2's own shipped config must load for this test to mean anything");
            Assert.IsTrue(WorldMapLoader.TryLoad(w2cfg, out MapData map, out string reason), reason);

            MapEntity[] a10Sludges = map.entities
                .Where(e => e != null && e.id != null && e.id.StartsWith("a10_sludge"))
                .ToArray();
            Assert.IsNotEmpty(a10Sludges, "a10 must author at least one sludge rect for this test to mean anything");

            var host = new GameObject("MV1087 host").transform;
            GameObject probeGo = null;
            try
            {
                MapBuild build = MapRuntime.Build(map, host);
                StormdrainDressing.Dress(host, map, build.Cover);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Transform sludgeHost = host.Find("Stormdrain Dressing/Sludge");
                Assert.IsNotNull(sludgeHost, "the sludge dressing host was never built");

                var pieces = new List<(Transform piece, MapEntity owner, bool isKerb)>();
                foreach (MapEntity sludge in a10Sludges)
                {
                    Transform tile = FindTileNear(sludgeHost, new Vector3(sludge.x, 0f, sludge.z));
                    if (tile == null) continue;
                    Transform trough = tile.Find("Channel Trough");
                    if (trough == null) continue;

                    foreach (Transform child in trough)
                    {
                        if (child.name == "Channel Kerb A" || child.name == "Channel Kerb B")
                            pieces.Add((child, sludge, true));
                        else if (child.name.StartsWith("Crossing"))
                            pieces.Add((child, sludge, false));
                    }
                }

                var allKerbs = pieces.Where(p => p.isKerb).ToList();
                var kerbs = allKerbs.Take(5).ToList();
                var crossings = pieces.Where(p => !p.isKerb).Take(2).ToList();
                Assert.GreaterOrEqual(kerbs.Count, 5,
                    $"a10's real channels only built {kerbs.Count} kerb pieces -- need at least 5 for this test to mean anything");
                Assert.GreaterOrEqual(crossings.Count, 2,
                    $"a10's real channels only built {crossings.Count} crossing decks -- need at least 2 for this test to mean anything");

                foreach (var s in kerbs.Concat(crossings))
                {
                    Collider col = s.piece.GetComponent<Collider>();
                    Assert.IsNotNull(col,
                        $"{s.piece.name} (under {s.owner.id}) carries no collider -- Max's legs sink straight " +
                        "through it, the exact MV-1087 defect");

                    Bounds b = col.bounds;
                    Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);

                    // A plain ray down the piece's own XZ centre, excluding the Cover layer -- a10's
                    // real a10_cover1 overlaps a10_sludge3's own channel at the height this probe drops
                    // from (confirmed empirically: a cast there lands on 'a10_cover1' at y=1, not the
                    // kerb), and this probe's only concern is the kerb/crossing's own collider, exactly
                    // what CoverLayer (Sightlines.cs) exists to let a cast deliberately see past.
                    int mask = CoverLayer.Exists ? ~(1 << CoverLayer.Index) : ~0;
                    bool hit = Physics.Raycast(top + Vector3.up * 1f, Vector3.down,
                        out RaycastHit hitInfo, 2f, mask, QueryTriggerInteraction.Ignore);
                    Assert.IsTrue(hit, $"a body cast straight down over {s.piece.name} (under {s.owner.id}) hit nothing");
                    Assert.AreEqual(b.max.y, hitInfo.point.y, 0.02f,
                        $"a body cast over {s.piece.name} (under {s.owner.id}) landed on " +
                        $"'{(hitInfo.collider != null ? hitInfo.collider.name : "???")}' at y={hitInfo.point.y}, " +
                        $"distance={hitInfo.distance}, not within 2cm of its own resolved top {b.max.y}");

                    bool inSludgeAtTop = MapSludgeDamage.IsInFloorSludge(map, top);
                    Assert.IsFalse(inSludgeAtTop,
                        $"{s.piece.name} (under {s.owner.id}) reads as floor sludge at its own resolved top {top} " +
                        "-- standing on it still slows/damages Max");

                    var sludgeCentreAtFloor = new Vector3(s.owner.x, 0f, s.owner.z);
                    bool inSludgeAtCentre = MapSludgeDamage.IsInFloorSludge(map, sludgeCentreAtFloor);
                    Assert.IsTrue(inSludgeAtCentre,
                        $"{s.owner.id}'s own centre at floor height {sludgeCentreAtFloor} no longer reads as " +
                        "floor sludge -- this test proves nothing");
                }

                // ---- a real CharacterController walks from dry floor, across one real kerb ----------
                // a10 authors real cover right beside some of its channels (a10_cover1 overlaps
                // a10_sludge3's own kerb footprint, confirmed empirically) -- pick the first kerb with
                // no Cover-layer collider within the probe's own approach radius, so the walk proves the
                // kerb's own step, not an unrelated collision with a nearby crate.
                int coverMask = CoverLayer.Exists ? 1 << CoverLayer.Index : 0;
                var firstKerb = allKerbs.FirstOrDefault(k =>
                    coverMask == 0 || Physics.OverlapSphere(k.piece.GetComponent<Collider>().bounds.center, 2.5f,
                        coverMask, QueryTriggerInteraction.Ignore).Length == 0);
                Assert.IsNotNull(firstKerb.piece,
                    "every real kerb a10 built sits within 2.5m of a Cover-layer collider -- none is clear enough to walk-test");
                Bounds kerbBounds = firstKerb.piece.GetComponent<Collider>().bounds;

                Vector3 sludgeCentreXZ = new Vector3(firstKerb.owner.x, 0f, firstKerb.owner.z);
                Vector3 stepAxis = kerbBounds.center - sludgeCentreXZ;
                stepAxis.y = 0f;
                stepAxis.Normalize();

                // A narrow channel's opposite kerb can sit well under 3 m away -- measure the approach/
                // landing off the kerb's OWN resolved half-width along the step axis (not a fixed 1.5 m)
                // so the probe never starts or ends embedded in the far kerb's own collider.
                float kerbHalfWidth = Mathf.Abs(Vector3.Dot(kerbBounds.extents,
                    new Vector3(Mathf.Abs(stepAxis.x), 0f, Mathf.Abs(stepAxis.z))));

                Vector3 startPos = kerbBounds.center - stepAxis * (kerbHalfWidth + 1.0f);
                startPos.y = 0.05f;
                Vector3 destPos = kerbBounds.center + stepAxis * (kerbHalfWidth + 0.5f);

                probeGo = new GameObject("MV1087 CC Probe", typeof(CharacterController));
                var cc = probeGo.GetComponent<CharacterController>();
                cc.radius = 0.3f;
                cc.height = 1.6f;
                cc.center = Vector3.up * 0.8f;
                cc.stepOffset = 0.3f; // Max's own real default -- PlayerController never touches it
                probeGo.transform.position = startPos;
                Physics.SyncTransforms();

                float peakY = probeGo.transform.position.y;
                for (int i = 0; i < 200; i++)
                {
                    Vector3 to = destPos - probeGo.transform.position; to.y = 0f;
                    if (to.magnitude < 0.05f) break;
                    // Horizontal and vertical as two separate Move calls -- same split PlayerController
                    // itself uses, so a summed vector never cancels the climb against gravity. The
                    // downward move is deliberately large (not a small per-frame gravity nudge) so the
                    // swept capsule fully re-settles every tick, rather than possibly leaving a multi-
                    // tick lag between a step-up and the next correction.
                    cc.Move(to.normalized * 0.05f);
                    cc.Move(Vector3.down * 1f);
                    peakY = Mathf.Max(peakY, probeGo.transform.position.y);
                }

                Vector3 endFlat = new Vector3(probeGo.transform.position.x, 0f, probeGo.transform.position.z);
                Vector3 destFlat = new Vector3(destPos.x, 0f, destPos.z);
                Assert.Less(Vector3.Distance(endFlat, destFlat), 0.3f,
                    $"the probe failed to cross the kerb -- it stalled at {probeGo.transform.position}");

                // A grounded CharacterController always rests skinWidth above the surface it is
                // standing on (Unity's own permanent contract, not a settling artefact -- confirmed
                // empirically: a 0.08 skinWidth probe peaked at exactly kerb-top + 0.08 every run), so
                // the resolved standing height is the kerb's own top PLUS the probe's own skinWidth.
                float resolvedStandingHeight = kerbBounds.max.y + cc.skinWidth;
                Assert.AreEqual(resolvedStandingHeight, peakY, 0.03f,
                    $"the probe's own peak height {peakY} while crossing the kerb was not within 3cm of its " +
                    $"resolved standing height {resolvedStandingHeight} (kerb top {kerbBounds.max.y} + " +
                    $"skinWidth {cc.skinWidth})");
            }
            finally
            {
                if (probeGo != null) Object.DestroyImmediate(probeGo);
                Object.DestroyImmediate(host.gameObject);
            }
        }

        private static Transform FindTileNear(Transform sludgeHost, Vector3 center)
        {
            foreach (Transform tile in sludgeHost)
            {
                var flatTile = new Vector3(tile.position.x, 0f, tile.position.z);
                var flatCenter = new Vector3(center.x, 0f, center.z);
                if (Vector3.Distance(flatTile, flatCenter) < 0.5f) return tile;
            }
            return null;
        }
    }
}
