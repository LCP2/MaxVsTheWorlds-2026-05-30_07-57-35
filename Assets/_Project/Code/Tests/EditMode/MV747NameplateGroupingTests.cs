using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-747: six Salvage Crabs bunched on World 3 drew three overlapping "SALVAGE CRAB" strings
    /// on top of each other and on top of Max's own health bar — <see cref="WorldHealthBarDeclutter"/>'s
    /// existing cluster-lift pass only staggers overlapping bars' HEIGHT, it never actually collapses
    /// them into one plate. One test covers every EditMode acceptance criterion here rather than one
    /// test per criterion (testing policy MV-465 rule 1): the grouping, the split, the draw-order
    /// guarantee and the plate cap are all facets of the SAME nameplate-declutter rewrite, not
    /// independent regressions that should have been separate tickets.
    ///
    /// Must fail to COMPILE on 1bdfceb (the commit before this ticket's fix) — none of
    /// <see cref="WorldHealthBar.ResolveGroups"/>, <see cref="WorldHealthBar.SortingOrder"/>, or
    /// <see cref="WorldHealthBar.Attach"/>'s <c>isPlayerBar</c>/<c>groupable</c> parameters existed
    /// there, same "proven to fail" idiom MV-744's own new test used.
    /// </summary>
    public sealed class MV747NameplateGroupingTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private sealed class FakeUnit : MonoBehaviour, IHealthReadout
        {
            public string Name = "SALVAGE CRAB";
            public float Hp = 100f;
            public float MaxHp = 100f;
            public bool Alive = true;
            public float HealthNormalized => MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
            public float HealthCurrent => Hp;
            public string ReadoutName => Name;
            public bool IsAlive => Alive;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        /// <summary>WorldHealthBar's OnEnable (which adds the bar to the static registry
        /// ResolveGroups/ResolveClutter scan) never fires as a side effect of AddComponent outside
        /// Play mode — same documented gap WorldHealthBarTests/PulseLaserTests/ReplicatorTests and
        /// friends already work around by invoking a component's lifecycle methods directly via
        /// reflection instead of relying on Unity to call them.</summary>
        private static readonly MethodInfo OnEnableMethod =
            typeof(WorldHealthBar).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private WorldHealthBar Register(WorldHealthBar bar)
        {
            OnEnableMethod.Invoke(bar, null);
            return bar;
        }

        private WorldHealthBar NewCrab(Vector3 position)
        {
            var go = new GameObject("Crab");
            go.transform.position = position;
            _spawned.Add(go);
            var unit = go.AddComponent<FakeUnit>();
            return Register(WorldHealthBar.Attach(go, unit, heightAboveCentre: 1.15f, worldWidth: 1.1f,
                                                  alwaysShow: true, groupable: true));
        }

        /// <summary>internal static — same reflection idiom WorldHealthBarNameplateTests/WorldHealthBarTests
        /// already use for WorldHealthBar's other non-public members (no InternalsVisibleTo back to
        /// the EditMode test assembly).</summary>
        private static void ResolveGroups(float groupRadius, int plateCap, Vector3 referencePosition)
        {
            var m = typeof(WorldHealthBar).GetMethod("ResolveGroups", BindingFlags.NonPublic | BindingFlags.Static);
            m.Invoke(null, new object[] { groupRadius, plateCap, referencePosition });
        }

        private static void Refresh(WorldHealthBar bar)
        {
            var m = typeof(WorldHealthBar).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(bar, null);
        }

        /// <summary>Simulates a real frame: every bar's own (individual) LateUpdate runs BEFORE
        /// WorldHealthBarDeclutter's (DefaultExecutionOrder 500) does — that ordering is what lets an
        /// alwaysShow bar hidden by a PREVIOUS resolve (a non-leader group member, or one the cap
        /// culled) reactivate itself every frame before this frame's grouping re-decides it, instead
        /// of staying stuck hidden once its group/position changes. Neither LateUpdate fires outside
        /// Play mode, so both steps are invoked directly here, in that same order.</summary>
        private void ResolveFrame(float groupRadius, int plateCap, Vector3 referencePosition)
        {
            foreach (var go in _spawned)
            {
                var bar = go == null ? null : go.GetComponent<WorldHealthBar>();
                if (bar != null) Refresh(bar);
            }
            ResolveGroups(groupRadius, plateCap, referencePosition);
        }

        private static string NameOf(WorldHealthBar bar)
        {
            var f = typeof(WorldHealthBar).GetField("_nameText", BindingFlags.NonPublic | BindingFlags.Instance);
            return ((UnityEngine.UI.Text)f.GetValue(bar)).text;
        }

        private static int NumberOf(WorldHealthBar bar)
        {
            var f = typeof(WorldHealthBar).GetField("_numberText", BindingFlags.NonPublic | BindingFlags.Instance);
            return int.Parse(((UnityEngine.UI.Text)f.GetValue(bar)).text);
        }

        [Test]
        public void SameKindClusterCollapses_SplitsWhenSeparated_MaxAlwaysOnTop_AndCapsToNearest()
        {
            // ---- AC1: six same-kind crabs seeded within a tight 4 m-radius cluster (every pairwise
            // distance here is <= ~4.47 m, comfortably inside DefaultGroupRadius) must draw exactly
            // one nameplate, labelled with the count, with the group's summed HP.
            var positions = new[]
            {
                new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f),
                new Vector3(0f, 0f, 2f), new Vector3(2f, 0f, 2f), new Vector3(4f, 0f, 2f),
            };
            var crabs = new WorldHealthBar[6];
            float expectedSum = 0f;
            for (int i = 0; i < 6; i++)
            {
                crabs[i] = NewCrab(positions[i]);
                float hp = 50f + i;   // distinct per crab, so "the sum" is actually provable
                ((FakeUnit)crabs[i].GetComponent<FakeUnit>()).Hp = hp;
                expectedSum += hp;
            }

            ResolveFrame(WorldHealthBar.DefaultGroupRadius, WorldHealthBar.DefaultPlateCap, Vector3.zero);

            int visible = 0;
            WorldHealthBar leader = null;
            foreach (var c in crabs)
                if (c.Showing) { visible++; leader = c; }

            Assert.AreEqual(1, visible, "six same-kind crabs bunched together must draw exactly one nameplate");
            Assert.AreEqual("SALVAGE CRAB x6", NameOf(leader),
                "the merged plate must read the kind name and the member count");
            Assert.AreEqual(Mathf.CeilToInt(expectedSum), NumberOf(leader),
                "the merged plate's HP must be the SUM of its members, not just the leader's own");

            // ---- AC2: move three of the six 15 m away. They stay just as close to EACH OTHER as
            // before (still 2 m apart) but now far from the other three, so the single plate must
            // split into two, each reading a count of three.
            for (int i = 0; i < 3; i++)
                crabs[i].transform.position += new Vector3(15f, 0f, 0f);

            ResolveFrame(WorldHealthBar.DefaultGroupRadius, WorldHealthBar.DefaultPlateCap, Vector3.zero);

            int visibleAfterSplit = 0;
            foreach (var c in crabs)
            {
                if (!c.Showing) continue;
                visibleAfterSplit++;
                Assert.AreEqual("SALVAGE CRAB x3", NameOf(c),
                    "each half of the split cluster must show its own count of three");
            }
            Assert.AreEqual(2, visibleAfterSplit, "moving half the cluster 15 m away must split one plate into two");

            // ---- AC3: an enemy plate must never draw over Max's own bar — assert the ORDERING, not
            // the on-screen geometry, exactly as the acceptance criterion specifies.
            var maxGo = new GameObject("Max");
            _spawned.Add(maxGo);
            var maxUnit = maxGo.AddComponent<FakeUnit>();
            maxUnit.Name = "MAX";
            var maxBar = Register(WorldHealthBar.Attach(maxGo, maxUnit, heightAboveCentre: 1.65f, worldWidth: 2.1f,
                                                        alwaysShow: true, isPlayerBar: true));

            WorldHealthBar stillVisibleCrab = System.Array.Find(crabs, c => c.Showing);
            Assert.Greater(maxBar.SortingOrder, stillVisibleCrab.SortingOrder,
                "Max's health bar must always win the draw order over an enemy nameplate");

            // ---- AC4: more candidate plates than the cap — only the ones nearest the reference
            // point may draw; the cull must pick the FARTHEST ones, not an arbitrary subset.
            int extra = WorldHealthBar.DefaultPlateCap + 3;
            var farCrabs = new WorldHealthBar[extra];
            for (int i = 0; i < extra; i++)
            {
                farCrabs[i] = NewCrab(new Vector3(1000f + i * 20f, 0f, 0f));
                ((FakeUnit)farCrabs[i].GetComponent<FakeUnit>()).Name = $"FAR {i}"; // distinct kind: no cross-merging
            }

            ResolveFrame(WorldHealthBar.DefaultGroupRadius, WorldHealthBar.DefaultPlateCap, Vector3.zero);

            int totalVisible = 0;
            foreach (var go in _spawned)
            {
                var bar = go == null ? null : go.GetComponent<WorldHealthBar>();
                if (bar != null && bar != maxBar && bar.Showing) totalVisible++;
            }
            Assert.AreEqual(WorldHealthBar.DefaultPlateCap, totalVisible,
                "more candidate plates than the cap must draw exactly the cap's worth, not fewer or more");
            Assert.IsTrue(stillVisibleCrab.Showing,
                "a near plate must keep drawing once far-away crowds exist elsewhere in the field");
            Assert.IsFalse(farCrabs[extra - 1].Showing,
                "the farthest-from-reference plate must be the one the cap culls, not an arbitrary one");

            // ---- AC5: the underlying separation MATH — EnemySeparation.Push/Steer, the exact
            // kind-agnostic primitive RobotEnemy.TickChase calls for every EnemyKind — must resolve
            // six enemies bunched at (effectively) one point out to at least their own body-separation
            // distance. World 3's eight kinds (MV-746) are World 1's own EnemyKind values wearing a
            // different DisplayName/skin (EnemyArchetype.WithOverride never touches ColliderRadius),
            // and neither EnemySeparation nor SeparationGrid reference Kind anywhere, so this already
            // covers every World 3 kind with zero kind-specific code — nothing here COULD special-case
            // one world's robots even if it wanted to.
            //
            // Sub-millimetre stagger, not bit-identical positions: Push has its own documented
            // exact-coincidence no-op (EnemySeparationTests.CoincidentNeighbour_IsIgnored_NotANaN, a
            // deliberate NaN-safety guard for a direction that doesn't exist) which is a different,
            // pre-existing case from this ticket's bug — and no real spawn ever places two robots at
            // bit-identical floats anyway, so this is "the same point" in every way a player could see.
            var pilePositions = new Vector3[6];
            for (int i = 0; i < 6; i++) pilePositions[i] = new Vector3(i * 0.001f, 0f, 0f);

            float minSeparation = EnemySeparation.DefaultMinDistance;
            var pileNeighbours = new List<Vector3>();
            for (int tick = 0; tick < 5000; tick++)
            {
                var next = new Vector3[6];
                bool anyPush = false;
                for (int i = 0; i < 6; i++)
                {
                    pileNeighbours.Clear();
                    for (int j = 0; j < 6; j++)
                        if (j != i) pileNeighbours.Add(pilePositions[j]);

                    Vector3 push = EnemySeparation.Push(pilePositions[i], pileNeighbours, minSeparation);
                    if (push.sqrMagnitude > 1e-6f) anyPush = true;
                    next[i] = pilePositions[i] + EnemySeparation.Steer(Vector3.zero, push) * 0.05f;
                }
                pilePositions = next;
                if (!anyPush) break;   // every pair already clears minSeparation — converged
            }

            for (int i = 0; i < 6; i++)
                for (int j = i + 1; j < 6; j++)
                {
                    float dist = Vector3.Distance(pilePositions[i], pilePositions[j]);
                    Assert.GreaterOrEqual(dist, minSeparation - 1e-3f,
                        $"enemies {i} and {j} ended {dist:F3} m apart after the separation system ticked " +
                        $"— inside the {minSeparation:F2} m body-separation distance");
                }
        }
    }
}
