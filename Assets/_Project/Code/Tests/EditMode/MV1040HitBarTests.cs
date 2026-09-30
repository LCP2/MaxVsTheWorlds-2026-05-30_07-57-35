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
    /// MV-1040: Lee reported robots visibly taking damage (numbers flowing up) with no health bar
    /// showing above them. Two defects compounded: <see cref="WorldHealthBar.ResolveGroups"/> could
    /// fold a robot Max was actively hitting into a merged "NAME xN" plate led by someone else, and a
    /// Dormant robot's <c>_wantsShow</c> — never refreshed while asleep (MV-980) — could stay stuck
    /// true, keeping it a live grouping candidate at a stale position that could win leadership and
    /// hide the robot actually under fire. One test covers both halves of the same visibility fix
    /// (testing policy MV-465 Rule 1): seven same-kind robots all damaged this frame, clustered well
    /// inside the group radius, plus a Dormant same-kind robot nearby whose bar last wanted to show.
    ///
    /// Guards MV-1040. Fails on `main` (the commit before this ticket's fix): <c>ResolveGroups</c> has
    /// no hit-bar exemption yet, so the seven damaged bars and the stale Dormant candidate — all
    /// "RUSHER", all mutually within the 6 m group radius — merge into a single combined plate led by
    /// whichever candidate happens to hold the lowest instance ID. At most one of the seven damaged
    /// bars ends up showing its own plain name; the rest show nothing at all, which is exactly Lee's
    /// bug report.
    /// </summary>
    public sealed class MV1040HitBarTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private sealed class FakeUnit : MonoBehaviour, IHealthReadout
        {
            public string Name = "RUSHER";
            public float Hp = 100f;
            public float MaxHp = 100f;
            public bool Alive = true;
            public float HealthNormalized => MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
            public float HealthCurrent => Hp;
            public string ReadoutName => Name;
            public bool IsAlive => Alive;
        }

        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo GravityField =
            typeof(RobotEnemy).GetField("gravity", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RobotHealthField =
            typeof(RobotEnemy).GetField("_health", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly MethodInfo BarOnEnableMethod =
            typeof(WorldHealthBar).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BarOnDisableMethod =
            typeof(WorldHealthBar).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BarRefreshMethod =
            typeof(WorldHealthBar).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo BarLateUpdateMethod =
            typeof(WorldHealthBar).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo ResolveGroupsMethod =
            typeof(WorldHealthBar).GetMethod("ResolveGroups", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo ResolveClutterMethod =
            typeof(WorldHealthBar).GetMethod("ResolveClutter", BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly FieldInfo NameTextField =
            typeof(WorldHealthBar).GetField("_nameText", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo WantsShowField =
            typeof(WorldHealthBar).GetField("_wantsShow", BindingFlags.NonPublic | BindingFlags.Instance);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            RobotEnemy.ResetRegistry();
            EnemyNavigation.Reset();
            // Register()'s reflection-invoked OnEnable never went through Unity's real enable state
            // machine, so DestroyImmediate below won't fire a matching OnDisable to pull these bars back
            // out of WorldHealthBar's own static _active registry — do it explicitly, or every test that
            // calls ResolveGroups/ResolveClutter after this one inherits destroyed, MissingReference-
            // throwing entries (this is what broke MV747NameplateGroupingTests the first time this file
            // was written, without this cleanup).
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                var bar = go.GetComponent<WorldHealthBar>();
                if (bar != null) BarOnDisableMethod.Invoke(bar, null);
                Object.DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        private static WorldHealthBar Register(WorldHealthBar bar)
        {
            BarOnEnableMethod.Invoke(bar, null);
            return bar;
        }

        private static void Refresh(WorldHealthBar bar) => BarRefreshMethod.Invoke(bar, null);
        private static void LateUpdate(WorldHealthBar bar) => BarLateUpdateMethod.Invoke(bar, null);

        private static void ResolveGroups(float groupRadius, int plateCap, Vector3 referencePosition) =>
            ResolveGroupsMethod.Invoke(null, new object[] { groupRadius, plateCap, referencePosition });

        private static void ResolveClutter(float clusterRadius, float stackStep) =>
            ResolveClutterMethod.Invoke(null, new object[] { clusterRadius, stackStep });

        private static string NameOf(WorldHealthBar bar) =>
            ((UnityEngine.UI.Text)NameTextField.GetValue(bar)).text;

        /// <summary>A same-kind robot, freshly built and immediately damaged one point this "frame" —
        /// <c>Attach</c>'s own Build()-&gt;Refresh() establishes the full-health baseline BEFORE the Hp
        /// drop below, so the very next explicit <see cref="Refresh"/> call reads it as a fresh hit
        /// (<c>_secondsSinceTrigger</c> resets to 0), landing inside <c>TriggerHoldSeconds</c> — a live
        /// hit bar, exactly the case the fix must never group away.</summary>
        private WorldHealthBar NewDamagedRusher(Vector3 position)
        {
            var go = new GameObject("Rusher (damaged)");
            go.transform.position = position;
            _spawned.Add(go);
            var unit = go.AddComponent<FakeUnit>();
            var bar = Register(WorldHealthBar.Attach(go, unit, heightAboveCentre: 1.15f, worldWidth: 1.1f,
                                                      groupable: true));
            unit.Hp = 90f;   // one hit's worth of damage, below the 100 baseline Build() already saw
            Refresh(bar);    // "this frame's" LateUpdate-equivalent — detects the drop, triggers live
            return bar;
        }

        // Guards MV-1040
        [Test]
        public void DamagedRobotsAlwaysShowOwnBar_DormantStaleCandidateNeverWinsLeadership()
        {
            // ---- seven same-kind robots, all within 4 m of each other, all damaged this frame.
            var damaged = new WorldHealthBar[7];
            for (int i = 0; i < 7; i++)
                damaged[i] = NewDamagedRusher(new Vector3(i * 0.5f, 0f, 0f));   // span 3.0 m end to end

            // ---- a Dormant same-kind robot within 6 m of the cluster, whose bar last wanted to show
            // (it was hit, exactly like the seven above, before falling back asleep) and — being built
            // last here — the lowest instance ID of the group, the exact condition MV-980/MV-1040
            // described as able to win leadership at a stale position.
            var dormantGo = new GameObject("Rusher (dormant, stale)");
            _spawned.Add(dormantGo);
            dormantGo.transform.position = new Vector3(3.5f, 0f, 0f);   // 0.5 m past the cluster's edge
            var cc = dormantGo.AddComponent<CharacterController>();
            var robot = dormantGo.AddComponent<RobotEnemy>();
            CcField.SetValue(robot, cc);
            robot.ResetState();
            GravityField.SetValue(robot, 0f);   // no floor collider in this test — see MV963RobotHotPathPerfTests

            // Awake() (which would normally attach the robot's own bar) never fires as a side effect of
            // AddComponent outside Play mode — same documented gap Register()/OnEnable works around
            // below — so this test attaches the bar itself, exactly as RobotEnemy.Awake() does in
            // production (groupable: true, same source).
            var dormantBar = Register(WorldHealthBar.Attach(dormantGo, robot, heightAboveCentre: 1.15f,
                                                             worldWidth: 1.1f, groupable: true));

            // The robot's own last hit, before it went back to sleep — a genuine health drop below the
            // baseline Build() already saw, so this Refresh() detects it and sets _wantsShow = true,
            // exactly the "whose bar last wanted to show" state the ticket describes.
            var robotUnit = (IHealthReadout)robot;
            Assert.AreEqual("RUSHER", robotUnit.ReadoutName, "the dormant candidate must be the same kind as the cluster");
            RobotHealthField.SetValue(robot, robotUnit.HealthCurrent - 1f);
            Refresh(dormantBar);
            Assert.IsTrue((bool)WantsShowField.GetValue(dormantBar),
                "setup failure: the stale candidate must want to show before it falls Dormant");

            robot.BeginDormant();
            Assert.IsTrue(robot.IsDormant, "setup failure: the stale candidate must actually be Dormant");

            // This frame's LateUpdate for the now-Dormant robot — MV-980's early return, which must
            // also (MV-1040) drop it as a grouping candidate instead of leaving last frame's answer.
            LateUpdate(dormantBar);

            // ---- resolve the frame exactly as WorldHealthBarDeclutter does: groups, then clutter.
            ResolveGroups(WorldHealthBar.DefaultGroupRadius, WorldHealthBar.DefaultPlateCap, Vector3.zero);
            ResolveClutter(1.7f, 0.5f);

            for (int i = 0; i < damaged.Length; i++)
            {
                Assert.IsTrue(damaged[i].Showing, $"damaged robot {i} must show its own bar while it is being hit");
                Assert.AreEqual("RUSHER", NameOf(damaged[i]),
                    $"damaged robot {i} must show its own plain name, never folded into a merged 'x N' plate");
            }

            Assert.IsFalse(dormantBar.Showing,
                "a stale Dormant candidate must never draw a plate — MV-1040's whole point");
        }
    }
}
