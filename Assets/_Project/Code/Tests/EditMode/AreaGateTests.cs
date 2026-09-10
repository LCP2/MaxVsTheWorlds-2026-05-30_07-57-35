using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Feel;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The gated-arena mechanic's SHAPE (v0.5 recut spec §1, WV-222): the map format and validator
    /// treat an area gate exactly the way they treat the scene-adopted <see cref="EntityKind.Gate"/> —
    /// it seals a doorway and nothing else may stand where it stands. <see cref="AreaGatePlayTests"/>
    /// proves what the built gate actually DOES (HP, primary-only damage, opening).
    ///
    /// Landed as a reusable engine capability with its own fixture map — the shipped
    /// <c>backyard_slice.json</c> is untouched (its boss fight's fate is still Lee's call).
    /// </summary>
    public sealed class AreaGateTests
    {
        /// <summary>Two rooms sealed by one area gate — the smallest fixture that exercises the kind.</summary>
        private static MapData TwoAreas(float doorway = 4f)
        {
            return new MapData
            {
                name = "Two Areas",
                wallHeight = 3f,
                wallThickness = 1f,
                zones = new[]
                {
                    new MapZone { id = "area1", type = "entry", x = 0f, z = -10f, width = 20f, depth = 20f },
                    new MapZone { id = "area2", type = "open",  x = 0f, z =  10f, width = 20f, depth = 20f },
                },
                links = new[] { new MapLink { from = "area1", to = "area2", doorway = doorway, gate = "gate1" } },
                entities = new[]
                {
                    new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = -10f },
                    new MapEntity { id = "gate1", kind = "areaGate", x = 0f, z = 0f, height = 3f, depth = 0.6f },
                },
            };
        }

        /// <summary><paramref name="areaCount"/> rooms in a straight chain, each junction sealed by its
        /// own area gate — the literal shape spec §1 asks for ("10 sequential rooms; each next room
        /// sealed by a gate"). The last room is marked a boss zone purely so <see cref="MapValidation"/>
        /// Reachable actually walks the whole chain rather than taking the fixture's word for it.</summary>
        private static MapData SequentialAreas(int areaCount, float doorway = 4f)
        {
            var zones = new MapZone[areaCount];
            var entities = new List<MapEntity> { new MapEntity { id = "start", kind = "playerSpawn", x = 0f, z = 0f } };
            var links = new List<MapLink>();

            for (int i = 0; i < areaCount; i++)
            {
                string type = i == 0 ? "entry" : i == areaCount - 1 ? "boss" : "open";
                zones[i] = new MapZone { id = $"area{i + 1}", type = type, x = 0f, z = i * 20f, width = 20f, depth = 20f };
            }

            for (int i = 0; i < areaCount - 1; i++)
            {
                string gateId = $"gate{i + 1}";
                links.Add(new MapLink { from = $"area{i + 1}", to = $"area{i + 2}", doorway = doorway, gate = gateId });
                entities.Add(new MapEntity
                {
                    id = gateId, kind = "areaGate", x = 0f, z = i * 20f + 10f, height = 3f, depth = 0.6f,
                });
            }

            return new MapData
            {
                name = "Sequential Areas", wallHeight = 3f, wallThickness = 1f,
                zones = zones, links = links.ToArray(), entities = entities.ToArray(),
            };
        }

        [Test]
        public void Validation_AcceptsTenSequentialAreaGates()
        {
            MapData map = SequentialAreas((int)ArenaTuning.DefaultAreaCount);

            Assert.IsTrue(MapValidation.Validate(map, out string why), why);
            Assert.AreEqual(10, map.zones.Length, "spec §1 asks for 10 sequential outdoor rooms");
            Assert.AreEqual(9, MapValidation.Kind(map, EntityKind.AreaGate).Count,
                "10 rooms in a chain need 9 gates between them");
        }

        [Test]
        public void Validation_RejectsAnAreaGateThatFillsNoDoorway()
        {
            MapData map = TwoAreas();
            map.links[0].gate = "";   // 'gate1' still exists, but no link names it any more

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("area gate 'gate1' does not fill any doorway", why);
        }

        [Test]
        public void Validation_RejectsABossRoomUnreachableThroughABrokenAreaGateChain()
        {
            MapData map = SequentialAreas(3);

            // Cut the last link AND its gate — not just the link, or the failure we get is "gate2 fills
            // no doorway" rather than the unreachable-boss-room claim this test is actually after.
            var links = new List<MapLink>(map.links);
            links.RemoveAt(1);
            map.links = links.ToArray();

            var entities = new List<MapEntity>(map.entities);
            entities.RemoveAll(e => e.id == "gate2");
            map.entities = entities.ToArray();

            Assert.IsFalse(MapValidation.Validate(map, out string why));
            StringAssert.Contains("cannot be walked to", why);
        }

        [Test]
        public void AnAreaGate_IsAsWideAsTheDoorwayItFills_PlusTheWallEitherSide()
        {
            MapData map = TwoAreas(doorway: 5f);
            MapEntity gate = map.Entity("gate1");

            Assert.AreEqual(5f + map.wallThickness * 2f, MapRuntime.SealWidth(map, gate), 1e-3,
                "the area gate did not follow its doorway — it would leave a gap beside itself");
        }

        [Test]
        public void TheFormat_ReadsAreaGateCaseAndSeparatorInsensitively()
        {
            Assert.AreEqual(EntityKind.AreaGate, MapEnums.Entity("areaGate"));
            Assert.AreEqual(EntityKind.AreaGate, MapEnums.Entity("area_gate"));
            Assert.AreEqual(EntityKind.AreaGate, MapEnums.Entity("AREA-GATE"));
        }

        [Test]
        public void TheFormat_SurvivesARoundTripThroughJson()
        {
            MapData before = TwoAreas();
            MapData after = MapLibrary.Parse(MapLibrary.ToJson(before));

            Assert.IsNotNull(after);
            Assert.AreEqual(EntityKind.AreaGate, after.Entity("gate1").Kind);
        }

        // --- MV-320: gates should open away from Max, not toward him ---

        [Test]
        public void AwayFromPlayerDirection_PointsFromTheNearRoomToTheFarRoom()
        {
            MapData map = TwoAreas(); // area1 (z=-10) -> gate1 (z=0) -> area2 (z=10)
            MapEntity gate = map.Entity("gate1");

            Vector3 away = MapRuntime.AwayFromPlayerDirection(map, gate);

            Assert.Greater(away.z, 0f, "area2 sits at +Z of area1, so 'away' should point toward +Z");
        }

        [Test]
        public void AwayFromPlayerDirection_IsZeroForAnUnlinkedGate()
        {
            MapData map = TwoAreas();
            map.links[0].gate = ""; // gate1 no longer fills any doorway

            Vector3 away = MapRuntime.AwayFromPlayerDirection(map, map.Entity("gate1"));

            Assert.AreEqual(Vector3.zero, away);
        }

        [Test]
        public void SwingSign_FlipsWhenTheFarRoomSitsOnTheSameSideTheDoorDefaultsToSwingingAwayFrom()
        {
            // A positive-angle hinge swing always sweeps toward -forward (AreaGate.SwingSign's own
            // doc) — so when the far room is ahead on the +forward side, the default (+1) would swing
            // the door back into the near room and the sign must flip to -1.
            Assert.AreEqual(-1f, AreaGate.SwingSign(Vector3.forward, Vector3.forward));
        }

        [Test]
        public void SwingSign_KeepsTheDefaultWhenTheFarRoomIsAlreadyOnTheSwingsNaturalSide()
        {
            Assert.AreEqual(1f, AreaGate.SwingSign(-Vector3.forward, Vector3.forward));
        }

        [Test]
        public void SwingSign_ADoubledBackChainNeedsTheOppositeSignFromAStraightOne()
        {
            // world1_config: g1 (area1 -> area2) runs +X, g3 (area3 -> area4) runs -X on the SAME
            // E/W-wall gate orientation (forward = world +X for every E/W gate, MapRuntime.BuildAreaGate)
            // — a single hardcoded sign would get one of the two backwards.
            Vector3 ewForward = Vector3.right;

            float straight = AreaGate.SwingSign(Vector3.right, ewForward);   // g1-style: away runs +X
            float doubledBack = AreaGate.SwingSign(-Vector3.right, ewForward); // g3-style: away runs -X

            Assert.AreNotEqual(straight, doubledBack);
        }

        [Test]
        public void SwingSign_DefaultsToPositiveWithNoMapContext()
        {
            Assert.AreEqual(1f, AreaGate.SwingSign(Vector3.zero, Vector3.forward));
            Assert.AreEqual(1f, AreaGate.SwingSign(Vector3.zero, Vector3.right));
        }

        // --- MV-323: ambient spawns should be biased away from the door a room is entered through ---

        [Test]
        public void EntryDirection_PointsFromTheNearRoomToTheFarRoom()
        {
            MapData map = TwoAreas(); // area1 (z=-10) -> gate1 (z=0) -> area2 (z=10)

            Vector3 entry = MapRuntime.EntryDirection(map, "area2");

            Assert.Greater(entry.z, 0f, "area2 sits at +Z of area1, so 'entry' should point toward +Z");
        }

        [Test]
        public void EntryDirection_IsZeroWhenNoLinkLeadsIntoTheZone()
        {
            MapData map = TwoAreas();

            Vector3 entry = MapRuntime.EntryDirection(map, "area1"); // nothing links INTO area1

            Assert.AreEqual(Vector3.zero, entry);
        }

        // --- MV-378: a fresh gate has to be a REAL physical obstruction, not just a damageable prop ---

        [Test]
        public void TheBuiltGate_HasASolidNonTriggerColliderExactlyWhereMaxWouldWalk()
        {
            MapData map = TwoAreas(doorway: 4f);
            var root = new GameObject("Physical Gate Probe Root");
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                GameObject gate = built.Actors["gate1"];
                Assert.IsNotNull(gate, "the map built no gate1 actor at all");

                var col = gate.GetComponent<Collider>();
                Assert.IsNotNull(col, "the built gate carries no Collider -- nothing can ever stop Max at it");
                Assert.IsTrue(col.enabled, "the built gate's collider starts disabled");
                Assert.IsFalse(col.isTrigger,
                    "the built gate's collider is trigger-only -- a CharacterController passes straight " +
                    "through a trigger, it does not stop at one");

                // autoSyncTransforms is off project-wide (DynamicsManager.asset), so a transform moved
                // by script (exactly what Spawn() just did) is not guaranteed visible to a physics query
                // in the same frame without an explicit sync.
                Physics.SyncTransforms();
                Collider[] hits = Physics.OverlapBox(gate.transform.position, Vector3.one * 0.05f,
                                                      gate.transform.rotation);
                Assert.Contains(col, hits,
                    "a physics query at the gate's own centre does not find its collider -- the doorway " +
                    "reads as empty space to anything that queries physics there, e.g. a CharacterController");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // --- MV-571: a locked gate must still say why it's shut, not go silent ---

        /// <summary>Pins MV-571: MV-569 correctly stopped a locked gate from showing a full health
        /// bar it can never lose, but landed with no replacement message — Lee stood at a locked gate
        /// with no bar and no text and still couldn't tell why it wouldn't open. <see cref="Locked"/>,
        /// <see cref="AreaGate.SetLockProgress"/> and <see cref="AreaGate.ReadoutName"/> none touch
        /// <c>_health</c> or anything else Awake() builds, so — unlike the Open()/TakeDamage coverage
        /// this file's other tests defer to PlayMode (see the MV-386 note below) — this one is safe to
        /// assert here without a frame ever ticking.</summary>
        [Test]
        public void LockedGate_ReadoutName_ShowsShedProgress_ThenTheOrdinaryNameOnceUnlocked()
        {
            var go = new GameObject("Locked Gate Probe");
            try
            {
                var gate = go.AddComponent<AreaGate>();

                gate.Locked = true;
                gate.SetLockProgress(3, 8);
                Assert.AreEqual("SHEDS  3 / 8", gate.ReadoutName,
                    "a locked gate must tell the player how many sheds it's waiting on, not go silent");

                gate.Locked = false;
                Assert.AreEqual("GATE", gate.ReadoutName,
                    "an unlocked gate's name must be unchanged");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // --- MV-740: the gate pill leaked a raw HP figure a player cannot interpret ---

        /// <summary>Pins MV-740: Lee's first World 2 playthrough showed every gate's pill as "GATE 74"
        /// — the HP number <see cref="WorldHealthBar"/> prints under EVERY unit's name (YT-111), which
        /// makes sense on a robot but means nothing on a gate ("74 means nothing" — Lee). Awake() never
        /// runs on an AddComponent'd MonoBehaviour inside this project's EditMode harness (see the
        /// MV-386 note below), so this invokes it by reflection to get the REAL production wiring
        /// (<see cref="AreaGate.Awake"/> attaching its own <see cref="WorldHealthBar"/> with real HP,
        /// not a hand-built stand-in bar), then reads the RESOLVED, currently-visible text off the
        /// built pill — never the format string — for both an open and a locked gate.</summary>
        [Test]
        public void GatePill_NeverShowsABareNumber_OpenOrLocked()
        {
            var openGo = new GameObject("Open Gate Pill Probe");
            var lockedGo = new GameObject("Locked Gate Pill Probe");
            try
            {
                var openGate = openGo.AddComponent<AreaGate>();
                InvokeAwake(openGate);
                RefreshBar(openGo.GetComponent<WorldHealthBar>());

                var lockedGate = lockedGo.AddComponent<AreaGate>();
                InvokeAwake(lockedGate);
                lockedGate.Locked = true;
                lockedGate.SetLockProgress(3, 8);
                RefreshBar(lockedGo.GetComponent<WorldHealthBar>());

                AssertNoVisibleLabelIsABareNumber(openGo, "open gate");
                AssertNoVisibleLabelIsABareNumber(lockedGo, "locked gate");

                Assert.IsTrue(AnyVisibleLabelContains(lockedGo, "SHEDS") && AnyVisibleLabelContains(lockedGo, "3")
                    && AnyVisibleLabelContains(lockedGo, "8"),
                    "a locked gate's pill must carry its requirement in words, not go silent");
            }
            finally
            {
                Object.DestroyImmediate(openGo);
                Object.DestroyImmediate(lockedGo);
            }
        }

        /// <summary>Calls the private Awake() directly — the only way to get AreaGate's real
        /// production wiring (health, threshold collider, health-bar attach) inside this project's
        /// synchronous EditMode harness, which never invokes Awake() as a side effect of AddComponent
        /// (see the MV-386 note below, confirmed empirically for this exact class).</summary>
        private static void InvokeAwake(AreaGate gate)
        {
            var m = typeof(AreaGate).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(gate, null);
        }

        /// <summary>Invokes the private Refresh() a real frame's LateUpdate would call — same
        /// reflection idiom WorldHealthBarTests/WorldHealthBarNameplateTests already use, since
        /// LateUpdate never fires outside Play mode.</summary>
        private static void RefreshBar(WorldHealthBar bar)
        {
            var m = typeof(WorldHealthBar).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(bar, null);
        }

        /// <summary>Fails if any CURRENTLY VISIBLE Text on the built pill is ENTIRELY digits — a label
        /// token in the middle of a worded phrase ("SHEDS  3 / 8") is not what this guards against; a
        /// label that is nothing but a raw number ("74") is exactly MV-740's bug. Active-hierarchy-only
        /// (includeInactive: false) on purpose: a locked gate's bar strip is hidden, not cleared, via
        /// SetBarHiddenKeepLabel (MV-571), so a stale number sitting in an inactive, unrendered Text
        /// object is not a player-visible bug and must not fail this test.</summary>
        private static void AssertNoVisibleLabelIsABareNumber(GameObject go, string label)
        {
            foreach (Text t in go.GetComponentsInChildren<Text>(false))
                Assert.IsFalse(Regex.IsMatch(t.text.Trim(), @"^\d+$"),
                    $"{label} pill shows a bare number '{t.text}' on its own label — a player can't " +
                    "interpret it (MV-740)");
        }

        private static bool AnyVisibleLabelContains(GameObject go, string substring)
        {
            foreach (Text t in go.GetComponentsInChildren<Text>(false))
                if (t.text.Contains(substring)) return true;
            return false;
        }

        // --- MV-386: opening a gate must drop the doorway's threshold, but the physical leaf has to
        // stay solid -- MV-378's fix only ever disabled the one collider both jobs shared, so a fully
        // open gate leaf had zero collision forever. That split (AreaGate._thresholdCollider /
        // _leafCollider, see AreaGate.cs) is deliberately NOT covered by an EditMode test here: proving
        // it needs AreaGate.Awake() to have actually run (it builds the threshold collider there) and
        // TakeDamage()/Open() to fire (they need _health, also built in Awake) -- and this project's
        // synchronous EditMode harness does not run a freshly AddComponent'd MonoBehaviour's Awake()
        // within a single [Test] method (confirmed empirically while building this ticket: an Awake-
        // start Debug.Log added for diagnosis never printed, in ANY AreaGate-building EditMode test,
        // including the pre-existing, passing ones above -- they only ever happened to pass because the
        // properties they check, Collider.enabled/isTrigger, match CreatePrimitive's own defaults
        // whether or not Awake ran). This is exactly why MV-378's own author put every Open()/TakeDamage
        // assertion in AreaGatePlayTests (PlayMode) instead, never in this file -- PlayMode's
        // [UnitySetUp] yield return null actually ticks a frame first. The real regression coverage for
        // this split is PlayMode: AreaGatePlayTests.SustainedPrimaryFire_BreaksTheGateAtExactlyItsHp_NotBefore
        // now asserts leaf.enabled stays true and ThresholdObject's collider goes false after Open() --
        // CI runs it (CC_AUTONOMY.md: this worker never authors or runs PlayMode itself).

        // --- MV-759: World 2 gates read as a round portal ring with sliding double doors ---

        /// <summary>ONE consolidated EditMode test (Lee's comment on MV-759, superseding the
        /// description's five-test list per MV-465 Rule 1 — the build, the lamp/hazard state and the
        /// slide are all facets of the one feature landing together, not independent regressions).
        /// Every assertion below is a RESOLVED value: an actually-built leaf count, a real material
        /// colour read back off the lamp, a real collider's <c>enabled</c> flag, a real
        /// <see cref="Span"/> computed from the gate's own hinge geometry — never an authored constant.
        ///
        /// Uses the same two reflection idioms this file already established for AreaGate:
        /// <see cref="InvokeAwake"/> (Awake never runs on a freshly AddComponent'd MonoBehaviour in
        /// this project's synchronous EditMode harness) and a direct private-method call with an
        /// explicit step, because the new door slide is driven by <c>Time.deltaTime</c> in
        /// <see cref="AreaGate"/>'s Update() exactly like the pre-existing hinge swing, which the
        /// MV-386 note just above already established cannot tick a frame here either.</summary>
        [Test]
        public void StormdrainGateSkin_BuildsTwoLeaves_TracksLockAndOpenState_AndSlidesClearViaAnimSequence()
        {
            var go = new GameObject("Stormdrain Gate Probe");
            try
            {
                go.transform.localScale = new Vector3(4f, 3f, 0.6f);
                var gate = go.AddComponent<AreaGate>();
                InvokeAwake(gate);

                gate.ApplyStormdrainGateSkin();

                // AC1 (two leaves, not the old single slab) + AC5 (no Animator anywhere under a gate).
                var dressingRoot = (GameObject)GetPrivate(gate, "_dressingRoot");
                var leafL = (GameObject)GetPrivate(gate, "_leafL");
                var leafR = (GameObject)GetPrivate(gate, "_leafR");
                Assert.IsNotNull(dressingRoot, "ApplyStormdrainGateSkin built no dressing root");
                Assert.IsNotNull(leafL, "no left leaf was built");
                Assert.IsNotNull(leafR, "no right leaf was built");
                Assert.AreNotSame(leafL, leafR, "a World 2 gate must build TWO distinct leaves");
                Assert.IsNull(go.GetComponentInChildren<Animator>(true),
                    "no Animator may exist under the gate itself (project_animation_substrate)");
                Assert.IsNull(dressingRoot.GetComponentInChildren<Animator>(true),
                    "no Animator may exist under the gate's Stormdrain dressing (project_animation_substrate)");

                // AC4: the lamp/hazard stripe follow Locked/IsOpen, recomputed each time off those
                // existing properties -- not cached in any new field of their own.
                var lampRenderer = ((GameObject)GetPrivate(gate, "_lampGlow")).GetComponentInChildren<Renderer>();
                var hazardStripe = (GameObject)GetPrivate(gate, "_hazardStripe");

                Color closedColor = lampRenderer.sharedMaterial.GetColor("_BaseColor");

                gate.Locked = true;
                Assert.IsTrue(hazardStripe.activeSelf, "the hazard stripe must show while locked");
                Color lockedColor = lampRenderer.sharedMaterial.GetColor("_BaseColor");
                Assert.AreNotEqual(closedColor, lockedColor,
                    "the lamp must change colour the instant LockedChanged fires");

                gate.Locked = false;
                Assert.IsFalse(hazardStripe.activeSelf, "the hazard stripe must hide once unlocked");
                Assert.AreEqual(closedColor, lampRenderer.sharedMaterial.GetColor("_BaseColor"),
                    "unlocking with the gate still shut must return the lamp to its closed colour");

                // AC3: the threshold still drops the instant IsOpen flips true -- unaffected by the
                // new art riding on top of it (item 5 of the ticket).
                GameObject threshold = gate.ThresholdObject;
                Assert.IsTrue(threshold.GetComponent<Collider>().enabled, "the threshold starts enabled");

                gate.ForceOpen();

                Assert.IsFalse(threshold.GetComponent<Collider>().enabled,
                    "the threshold must still drop the instant the gate opens (MV-386), unchanged by MV-759");
                Color openColor = lampRenderer.sharedMaterial.GetColor("_BaseColor");
                Assert.AreNotEqual(closedColor, openColor, "the lamp must change colour again once open");

                // AC5 (driven by AnimSequence) + AC2 (clears the doorway once the slide completes). A
                // single huge dt finishes the sequence in one step -- AnimSequence.Progress clamps at
                // its step's duration, so this is "run the slide to completion", not a timing shortcut.
                typeof(AreaGate).GetMethod("AdvanceStormdrainSlide", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(gate, new object[] { 10f });

                Assert.IsInstanceOf<AnimSequence>(GetPrivate(gate, "_doorSlide"),
                    "the door slide must be driven by an AnimSequence (MV-684), not an Animator or a tween library");

                Span doorSpan = gate.OpenLeafSpan(alongX: true);
                AssertClearOfSpan(leafL, doorSpan, "left");
                AssertClearOfSpan(leafR, doorSpan, "right");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        private static object GetPrivate(object target, string fieldName) =>
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target);

        /// <summary>Fails if <paramref name="leaf"/>'s world-space footprint along X overlaps
        /// <paramref name="doorSpan"/> -- the range <see cref="AreaGate.OpenLeafSpan"/> reports as
        /// still covered by the gate's own (untouched) hinge-swung collision leaf, i.e. the part of the
        /// doorway a routed waypoint has to avoid. A leaf that still straddles it once the slide has
        /// finished would read as open but still visually block the doorway it just cleared.</summary>
        private static void AssertClearOfSpan(GameObject leaf, Span doorSpan, string which)
        {
            var rend = leaf.GetComponent<Renderer>();
            Bounds b = rend.bounds;
            bool overlaps = b.min.x <= doorSpan.Max && b.max.x >= doorSpan.Min;
            Assert.IsFalse(overlaps,
                $"the {which} leaf's slid-open bounds [{b.min.x:0.##}, {b.max.x:0.##}] overlap the doorway " +
                $"span {doorSpan} still covered by the gate's own hinge-swung collision leaf");
        }
    }
}
