using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Player;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1037 rewrite of MV-720's own test (the ticket's own instruction: "Rewrite
    /// MV720BossContactDamageTests so it no longer uses an unscaled cube"). MV-720 pinned that Big
    /// Bermuda's contact damage is rate-limited, but its fixture built an UNSCALED cube (localScale 1)
    /// and stood Max 0.3 m from it — a shape no real boss has. <see cref="BigBermudaBoss.TickContactDamage"/>
    /// compared its collider's raw LOCAL radius against a WORLD-space distance; <see cref="MapRuntime.BuildBoss"/>
    /// actually spawns every boss body scaled to its authored size (3.6-4.5 m in Worlds 1/3), so the
    /// true world reach was roughly double what the old check compared against — Max and a Sentinel
    /// could never get within it, and contact damage never landed, in any world, for every boss (Lee,
    /// 2026-09-30, device: "Big Bermuda sits on top of Max and a Sentinel and does no damage").
    ///
    /// Fails on base commit 2400f3e: a real World 1 boss's collider (local radius 0.5 x 3.6 authored
    /// scale = 1.8 m world) compared against the old ~1.0 m "reach" never triggers, so Max's health
    /// (the first assertion below) never moves within the 1.2 s window and the test fails there.
    ///
    /// Built through the REAL production route (<see cref="MapRuntime.Build"/> off world1_config's
    /// "a12_boss1" and world3_config's "anchorhead", so each boss carries its own authored size and
    /// rig) with a real Max (<see cref="CharacterController"/> + <see cref="PlayerController"/> +
    /// <see cref="PlayerHealth"/>) and a real deployed <see cref="Sentinel"/>, both driven into the
    /// boss by their own real <see cref="CharacterController.Move"/> (via <see cref="CharacterControllerMotion.SafeMove"/>)
    /// until the engine itself blocks them — never by computing a position from the same radius
    /// arithmetic the fix uses, which would only re-check that arithmetic against itself.
    ///
    /// EditMode only, reflection-driven for <c>Awake</c>/<c>TickContactDamage</c> (repo convention —
    /// this worker never authors PlayMode tests, and neither never runs as a side effect of a plain
    /// <c>AddComponent</c> outside Play mode).
    /// </summary>
    public sealed class MV720BossContactDamageTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly MethodInfo TickContactDamageMethod =
            typeof(BigBermudaBoss).GetMethod("TickContactDamage", NonPublicInstance);

        private static readonly MethodInfo ComputeSplitMotionMethod =
            typeof(PlayerController).GetMethod("ComputeSplitMotion", NonPublicInstance);

        private const float Dt = 1f / 60f;

        /// <summary>How far Max/the Sentinel start from the boss before being driven in — short enough
        /// that the straight approach never crosses unrelated map geometry, same "start close" idiom
        /// <c>MV1022MobileShedStandoffTests</c> already relies on for this exact shape of problem (a
        /// real, map-built body with real collision).</summary>
        private const float ApproachStartDistance = 3f;

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", NonPublicInstance).Invoke(component, null);

        private static void InvokeTickContactDamage(BigBermudaBoss boss, float dt) =>
            TickContactDamageMethod.Invoke(boss, new object[] { dt });

        /// <summary>A <see cref="CharacterController"/>'s radius, converted from its own LOCAL space
        /// into world units via <paramref name="t"/>'s lossy scale — the same conversion the MV-1037
        /// fix itself uses, but only ever applied here to a REAL, already-fitted collider read off the
        /// finished build, never to derive a placement (see the class doc above).</summary>
        private static float WorldRadius(CharacterController cc, Transform t)
        {
            Vector3 scale = t.lossyScale;
            return cc.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        /// <summary>Drives <paramref name="cc"/> straight at <paramref name="targetPos"/> (flat) at
        /// <paramref name="speed"/> for <paramref name="seconds"/> of simulated time — ample to close
        /// <see cref="ApproachStartDistance"/> and then sit blocked against the boss's own real
        /// collider for the remainder, via the engine's own swept <see cref="CharacterController.Move"/>,
        /// not a computed stop point.</summary>
        private static void WalkFlatToward(CharacterController cc, Transform mover, Transform target, float speed, float seconds)
        {
            int ticks = Mathf.CeilToInt(seconds / Dt);
            for (int i = 0; i < ticks; i++)
            {
                Vector3 to = target.position - mover.position; to.y = 0f;
                Vector3 dir = to.sqrMagnitude > 1e-6f ? to.normalized : Vector3.zero;
                CharacterControllerMotion.SafeMove(cc, dir * speed * Dt);
                Physics.SyncTransforms();
            }
        }

        private void RunScenario(string worldKey, string bossId)
        {
            WorldConfig cfg = WorldLibrary.Load(worldKey);
            Assert.IsNotNull(cfg, $"{worldKey} failed to load — see the error log above.");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = new GameObject($"MV720 Root {worldKey}");
            GameObject maxGo = null, sentinelGo = null;
            try
            {
                MapBuild built = MapRuntime.Build(map, root.transform);
                Physics.SyncTransforms(); // autoSyncTransforms is off project-wide (DynamicsManager.asset)

                Assert.IsTrue(built.Actors.TryGetValue(bossId, out GameObject bossGo) && bossGo != null,
                    $"{worldKey}'s boss ('{bossId}') was not built");
                var boss = bossGo.GetComponent<BigBermudaBoss>();
                Assert.IsNotNull(boss, $"{bossId} did not build as a BigBermudaBoss");
                var bossCc = bossGo.GetComponent<CharacterController>();
                Assert.IsNotNull(bossCc, $"{bossId} carries no CharacterController");

                Vector3 bossPos = bossGo.transform.position;

                // --- real Max, tagged Player so the boss's own AcquireTarget (in Awake, below) finds
                // him, approaching from +X. ---
                maxGo = new GameObject("MV720 Max", typeof(CharacterController), typeof(PlayerController)) { tag = "Player" };
                maxGo.transform.position = new Vector3(bossPos.x + ApproachStartDistance, 0f, bossPos.z);
                var player = maxGo.GetComponent<PlayerController>();
                InvokeAwake(player);
                var maxHealth = maxGo.AddComponent<PlayerHealth>();
                maxHealth.Initialize(); // Awake is not a reliable side effect of AddComponent outside Play mode
                var maxCc = maxGo.GetComponent<CharacterController>();

                // --- a real deployed Sentinel, approaching from -X so it never fights Max for the same
                // lane into the boss. ---
                sentinelGo = new GameObject("MV720 Sentinel");
                var sentinel = sentinelGo.AddComponent<Sentinel>();
                sentinel.Init(new Vector3(bossPos.x - ApproachStartDistance, 0f, bossPos.z),
                    maxHp: 100f, range: 0f, fireInterval: 999f, moveSpeed: 0f, standoffDistance: 0f, followTarget: null);
                var sentinelCc = sentinelGo.GetComponent<CharacterController>();
                Assert.IsNotNull(sentinelCc, "the deployed Sentinel carries no CharacterController");
                Physics.SyncTransforms();

                // Boss Awake AFTER Max exists and is tagged Player — AcquireTarget needs to find him.
                InvokeAwake(boss);

                // --- drive both into contact with the boss's REAL, authored-size collider via their
                // own real CharacterController.Move — see the class doc for why this is never computed. ---
                const float approachSeconds = 5f;
                for (int i = 0; i < Mathf.CeilToInt(approachSeconds / Dt); i++)
                {
                    Vector3 toBoss = boss.transform.position - maxGo.transform.position; toBoss.y = 0f;
                    Vector3 planarVel = toBoss.sqrMagnitude > 1e-6f ? toBoss.normalized * player.WalkSpeed : Vector3.zero;
                    object split = ComputeSplitMotionMethod.Invoke(player, new object[] { planarVel, Dt });
                    var splitType = split.GetType();
                    CharacterControllerMotion.SafeMove(maxCc, (Vector3)splitType.GetField("Item1").GetValue(split));
                    CharacterControllerMotion.SafeMove(maxCc, (Vector3)splitType.GetField("Item2").GetValue(split));

                    Vector3 toBossFromSentinel = boss.transform.position - sentinelGo.transform.position; toBossFromSentinel.y = 0f;
                    Vector3 sentinelDir = toBossFromSentinel.sqrMagnitude > 1e-6f ? toBossFromSentinel.normalized : Vector3.zero;
                    CharacterControllerMotion.SafeMove(sentinelCc, sentinelDir * player.WalkSpeed * Dt);

                    Physics.SyncTransforms();
                }

                // Sanity on the test's own setup (not on the fix): both must have actually closed the
                // gap to something touching-distance-shaped before the contact-damage assertion below
                // can mean anything.
                float bossWorldRadius = WorldRadius(bossCc, boss.transform);
                float maxDistAfterApproach = Vector3.Distance(maxGo.transform.position, bossPos);
                float sentinelDistAfterApproach = Vector3.Distance(sentinelGo.transform.position, bossPos);
                Assert.LessOrEqual(maxDistAfterApproach, bossWorldRadius + 1.5f,
                    $"{worldKey}: Max never closed on {bossId} during the approach (still {maxDistAfterApproach:F2} m out) — check for unrelated blocking geometry");
                Assert.LessOrEqual(sentinelDistAfterApproach, bossWorldRadius + 1.5f,
                    $"{worldKey}: the Sentinel never closed on {bossId} during the approach (still {sentinelDistAfterApproach:F2} m out) — check for unrelated blocking geometry");

                float maxHealthBeforeContact = maxHealth.Current;
                float sentinelHealthBeforeContact = sentinel.HealthCurrent;

                // --- AC1: within 1.2s of ticking contact damage (the boss's own "no free first hit"
                // cooldown starts full at Awake, so the first landed tick needs the full 1.0s cadence to
                // elapse), both Max and the Sentinel — now physically blocked against the boss's real,
                // authored-size body — must take exactly one ContactDamagePerTick. ---
                float elapsed = 0f;
                while (elapsed < 1.2f && maxHealth.Current == maxHealthBeforeContact)
                {
                    InvokeTickContactDamage(boss, Dt);
                    elapsed += Dt;
                }

                Assert.AreEqual(maxHealthBeforeContact - BossTuning.ContactDamagePerTick, maxHealth.Current, 0.01f,
                    $"{worldKey}: Max, blocked against {bossId}'s real body, must take {BossTuning.ContactDamagePerTick} contact damage within 1.2s");
                Assert.AreEqual(sentinelHealthBeforeContact - BossTuning.ContactDamagePerTick, sentinel.HealthCurrent, 0.01f,
                    $"{worldKey}: a Sentinel blocked against {bossId}'s real body must take {BossTuning.ContactDamagePerTick} contact damage within 1.2s");

                // --- AC2: Max 1.5 m clear of the boss's own collider edge (read live off its real,
                // rig-build-surviving CharacterController — never a hardcoded assumption about its
                // radius) — the Sentinel moved well out of the way — must take nothing over 3s. ---
                sentinelGo.transform.position = bossPos + new Vector3(0f, 5000f, 0f);
                Physics.SyncTransforms();

                float maxWorldRadius = WorldRadius(maxCc, maxGo.transform);
                Vector3 awayDir = maxGo.transform.position - bossPos; awayDir.y = 0f;
                awayDir = awayDir.sqrMagnitude > 1e-6f ? awayDir.normalized : Vector3.right;
                maxGo.transform.position = bossPos + awayDir * (bossWorldRadius + maxWorldRadius + 1.5f);
                Physics.SyncTransforms();

                float maxHealthClear = maxHealth.Current;
                for (float t = 0f; t < 3f; t += Dt) InvokeTickContactDamage(boss, Dt);

                Assert.AreEqual(maxHealthClear, maxHealth.Current, 0.01f,
                    $"{worldKey}: Max standing 1.5m clear of {bossId}'s collider edge must take no contact damage over 3s");
            }
            finally
            {
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                if (sentinelGo != null) Object.DestroyImmediate(sentinelGo);
                Object.DestroyImmediate(root);
                BossCensus.Reset();
                Sentinel.ResetRegistry();
            }
        }

        [Test]
        public void BossContactDamage_ScalesReachToTheBossRealWorldSize_AcrossWorlds()
        {
            RunScenario(WorldLibrary.World1, "a12_boss1");
            RunScenario(WorldLibrary.World3, "anchorhead");
        }
    }
}
