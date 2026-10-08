using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1127 (the one new test, per CC_AUTONOMY's testing policy): switches Sludgequeen on as World
    /// 2's real final boss — targeted sludge lobs instead of MV-696's floor flood, a sludger cap, and
    /// the finale chain working through her instead of only through <see cref="BigBermudaBoss"/>.
    ///
    /// Fails on base commit 7fe199a: <c>MapRuntime.BuildBoss</c> still builds World 2's "sludgequeen" id
    /// as a <see cref="BigBermudaBoss"/> (AC1a fails outright — <c>GetComponent&lt;SludgequeenBoss&gt;()</c>
    /// is null); <c>SludgequeenBoss</c> there still floods the arena, has no lob/ring system at all
    /// (<c>CorrosiveGlob.FireAt</c>/<c>SetCannonMouths</c>/<c>SetChuteFeet</c>/public <c>Tick(dt)</c> are
    /// all CS1061/CS0117 there), her health is <c>BossTuning.Health * 12</c> (20,700, not 6,900), her
    /// brood has no concurrent cap, and <c>BossVictoryPayoff.Install</c> never recognises her — quoted
    /// failure output is in the fix comment.
    ///
    /// Every sub-check below drives the boss through its own real public entry points — the public
    /// <see cref="SludgequeenBoss.Tick"/> (added by this ticket for exactly this), the real
    /// <see cref="CorrosiveGlob.Tick"/>/<see cref="MaxWorlds.Enemies.CorrosionPuddle.Tick"/>, the real
    /// <see cref="MaxWorlds.Enemies.CorrosionPuddle.Spawn"/> entry point, and (for AC1a/AC1h) the real
    /// <see cref="MapRuntime.Build"/> loader — never a hand-set private field standing in for one of
    /// these. <c>Awake</c>/<c>OnEnable</c>/<c>OnDeath</c> are still driven by reflection where the ticket
    /// itself needs them, the same established idiom every sibling boss/finale test in this suite already
    /// uses (Unity does not reliably invoke these for a plain <c>AddComponent</c> outside Play mode).
    /// </summary>
    public sealed class MV1127SludgequeenFightTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly List<GameObject> _scratch = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            BossCensus.Reset();
            SludgequeenBoss.ResetRegistry();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            EnemyNavigation.Reset();
            Pickup.ResetRegistry();
        }

        [TearDown]
        public void TearDown() => Cleanup();

        /// <summary>Tears down everything a single sub-check built, and resets every static registry --
        /// called between sub-checks (not just once at the end of the whole test) so an earlier
        /// sub-check's Max/Sentinel/robot never leaks into a later one. Without this, every sub-check
        /// that tags a GameObject "Player" (most of them) would leave a SECOND, stale, far-away Max in
        /// the scene for <c>GameObject.FindGameObjectWithTag("Player")</c> to find instead of the one the
        /// next sub-check actually built.</summary>
        private void Cleanup()
        {
            foreach (GameObject go in _scratch) if (go != null) Object.DestroyImmediate(go);
            _scratch.Clear();

            foreach (var stray in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<GroundRing>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            foreach (var stray in Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            // SludgequeenRig.CreateFor builds an unparented top-level GameObject (it follows the boss
            // rather than being parented to it) -- destroying a sub-check's own tracked root/boss never
            // takes the rig with it.
            foreach (var stray in Object.FindObjectsByType<SludgequeenRig>(FindObjectsSortMode.None))
                Object.DestroyImmediate(stray.gameObject);
            // SludgequeenBoss.BroodRoot() is also an unparented top-level container (an empty one, once
            // the RobotEnemy sweep above has already taken every sludger under it).
            GameObject stragglerBroodRoot = GameObject.Find("Sludgequeen Brood");
            if (stragglerBroodRoot != null) Object.DestroyImmediate(stragglerBroodRoot);

            BossCensus.Reset();
            SludgequeenBoss.ResetRegistry();
            RobotEnemy.ResetRegistry();
            Sentinel.ResetRegistry();
            EnemyNavigation.Reset();
            Pickup.ResetRegistry();
        }

        private GameObject Track(GameObject go)
        {
            _scratch.Add(go);
            return go;
        }

        private static void InvokeAwake(Object component) =>
            component.GetType().GetMethod("Awake", NonPublicInstance).Invoke(component, null);

        private static void InvokeOnEnable(Object component) =>
            component.GetType().GetMethod("OnEnable", NonPublicInstance).Invoke(component, null);

        private static void InvokeOnDeath(Object component) =>
            component.GetType().GetMethod("OnDeath", NonPublicInstance).Invoke(component, null);

        private static float ContactReachTo(SludgequeenBoss boss, Transform target) =>
            (float)typeof(SludgequeenBoss).GetMethod("ContactReachTo", NonPublicInstance)
                .Invoke(boss, new object[] { target });

        private GameObject NewMax(Vector3 position)
        {
            var go = Track(new GameObject("MV1127 Max", typeof(CharacterController)) { tag = "Player" });
            go.transform.position = position;
            var health = go.AddComponent<PlayerHealth>();
            health.Initialize();
            return go;
        }

        private Sentinel NewSentinel(Vector3 position, float maxHp = 100f)
        {
            var go = Track(new GameObject("MV1127 Sentinel"));
            var sentinel = go.AddComponent<Sentinel>();
            sentinel.Init(position, maxHp: maxHp, range: 0f, fireInterval: 999f,
                moveSpeed: 0f, standoffDistance: 0f, followTarget: null);
            return sentinel;
        }

        private RobotEnemy NewRobot(Vector3 position)
        {
            var go = Track(new GameObject("MV1127 Robot"));
            go.transform.position = position;
            var robot = go.AddComponent<RobotEnemy>();
            robot.Apply(EnemyArchetype.Rusher);
            return robot;
        }

        /// <summary>A bare Sludgequeen, scaled to a plausible real boss size (same 4x convention
        /// <c>MV1083BossClosesToContactTests</c> already uses) with her own real rig bound, so her
        /// lob system fires from REAL cannon-mouth/chute-foot transforms rather than the no-rig
        /// fallback. No map registered — <see cref="MaxWorlds.Arena.LineOfSight.Between"/> degrades to
        /// "clear" with no cover geometry in the scene, so she still wakes for real off proximity alone.</summary>
        private SludgequeenBoss NewBoss(Vector3 position, out SludgequeenRig rig)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            go.transform.position = position;
            go.transform.localScale = Vector3.one * 4f;

            var boss = go.AddComponent<SludgequeenBoss>();
            InvokeAwake(boss);
            rig = SludgequeenRig.CreateFor(boss);
            Track(rig.gameObject);
            return boss;
        }

        [Test]
        public void SludgequeenFight_MatchesEveryAcceptanceCriterion()
        {
            // Cleanup() runs after EVERY sub-check, not just at the end of the test -- see its own doc
            // comment on why (a stale Player-tagged GameObject from an earlier sub-check would otherwise
            // be the one AcquireTarget finds in a later one).
            RunIsolated(Check1aDispatchAndName);
            RunIsolated(Check1bNoFloorHazardAt30PercentAnd1gHealthIs6900);
            RunIsolated(Check1cAnd1c2LobRingsAndGlobVisuals);
            RunIsolated(Check1dSplashPuddleAndRobotImmunity);
            RunIsolated(Check1ePuddleLifetimeIsSixSeconds);
            RunIsolated(Check1fSludgerConcurrentCap);
            RunIsolated(Check1gContactDamage);
            RunIsolated(Check1hFinaleChain);
        }

        private void RunIsolated(System.Action check)
        {
            try { check(); }
            finally { Cleanup(); }
        }

        // ================================================================= AC1a

        private void Check1aDispatchAndName()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsNotNull(cfg, "world2_config.json failed to load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);

            var root = Track(new GameObject("MV1127 Map Root"));
            MapBuild built = MapRuntime.Build(map, root.transform);

            Assert.IsTrue(built.Actors.TryGetValue("sludgequeen", out GameObject bossGo) && bossGo != null,
                "world2_config.json's a21 boss ('sludgequeen') was not built");
            Assert.IsNotNull(bossGo.GetComponent<SludgequeenBoss>(),
                "AC1a: a21's boss must build as a SludgequeenBoss");
            Assert.IsNull(bossGo.GetComponent<BigBermudaBoss>(),
                "AC1a: a21's boss must NOT build as a BigBermudaBoss");

            NewMax(bossGo.transform.position + new Vector3(5f, 0f, 0f));
            var boss = bossGo.GetComponent<SludgequeenBoss>();
            InvokeAwake(boss); // re-run now Max exists -- AcquireTarget needs the tagged Player present

            string engagedName = null;
            void OnEngaged(string name, int phases) => engagedName = name;
            MaxWorlds.UI.HudSignals.BossEngaged += OnEngaged;
            try
            {
                boss.Tick(1f / 60f); // Dormant -> IsWithinWakeRange (true, no cover) -> Wake()
            }
            finally
            {
                MaxWorlds.UI.HudSignals.BossEngaged -= OnEngaged;
            }

            Assert.AreEqual("SLUDGEQUEEN", engagedName, "AC1a: she must register as SLUDGEQUEEN on waking");
        }

        // ================================================================= AC1b / AC1g

        private void Check1bNoFloorHazardAt30PercentAnd1gHealthIs6900()
        {
            var boss = NewBoss(new Vector3(2000f, 0f, 0f), out _);
            // Max stands close (east) so the fight actually runs real lob volleys/brood for this check
            // to mean anything; the bystander Sentinel stands far off in an UNRELATED direction (north),
            // well outside any lob's 2-5 m scatter around Max, so it proves the floor itself is safe,
            // not merely that nothing ever ticked.
            NewMax(boss.transform.position + new Vector3(10f, 0f, 0f));
            var bystander = NewSentinel(boss.transform.position + new Vector3(0f, 0f, 30f));
            float before = bystander.HealthCurrent;

            // Full health: tick past at least one full lob volley (Phase1GlobInterval = 4 s) with the
            // bystander far from the boss's own body and from anywhere a lob could land -- no flood, no
            // map-wide slow, nothing, even with a real volley actually having fired.
            for (int i = 0; i < 360; i++) boss.Tick(1f / 60f); // 6 s
            Assert.AreEqual(before, bystander.HealthCurrent, 0.01f,
                "AC1b: at full health, nothing must damage a bystander standing clear of her body/lobs");
            Assert.AreEqual(1f, MaxWorlds.Arena.MapSlowZones.Instance.SpeedMultiplierAt(bystander.transform.position), 0.001f,
                "AC1b: at full health, nothing must slow a bystander standing clear of her body/lobs");

            // AC1g: her health is 6,900 -- proven behaviourally (never a reflected private field read):
            // one hit just under 6,900 total must not kill her; one more point must.
            boss.TakeDamage(new DamageInfo(1f, boss.transform.position, Vector3.up, Team.Player)); // wakes her -- consumed, no HP lost
            boss.TakeDamage(new DamageInfo(SludgequeenTuning.Health * 0.7f - 2f, boss.transform.position, Vector3.up, Team.Player));
            Assert.IsFalse(boss.IsDead, "fixture: must still be alive at ~30% for the floor-hazard sub-check");

            // 30% health: no flood/slow/damage either.
            for (int i = 0; i < 360; i++) boss.Tick(1f / 60f); // 6 s
            Assert.AreEqual(before, bystander.HealthCurrent, 0.01f,
                "AC1b: at 30% health, nothing must damage a bystander standing clear of her body/lobs");
            Assert.AreEqual(1f, MaxWorlds.Arena.MapSlowZones.Instance.SpeedMultiplierAt(bystander.transform.position), 0.001f,
                "AC1b: at 30% health, nothing must slow a bystander standing clear of her body/lobs");

            // AC1g, the other half of the bracket: one point more than SludgequeenTuning.Health total
            // (already at ~30%, i.e. SludgequeenTuning.Health*0.7-1 delivered) must kill her exactly at
            // the 6,900 boundary -- deliver the rest of it plus one.
            boss.TakeDamage(new DamageInfo(SludgequeenTuning.Health * 0.3f + 2f, boss.transform.position, Vector3.up, Team.Player));
            Assert.IsTrue(boss.IsDead, "AC1g: her health must be exactly 6,900 -- she must be dead once that much damage has landed");
        }

        // ================================================================= AC1c / AC1c2

        private void Check1cAnd1c2LobRingsAndGlobVisuals()
        {
            var boss = NewBoss(new Vector3(3000f, 0f, 0f), out SludgequeenRig rig);
            Vector3 anchor = boss.transform.position + new Vector3(10f, 0f, 0f);
            NewMax(anchor);

            // NewBoss's own Awake() ran before Max existed, so its AcquireTarget found nothing -- same
            // TickDormant convention BigBermudaBoss already uses: the first Tick after Max appears only
            // re-acquires _target and returns (no wake check that same frame); the SECOND Tick is the
            // one that actually evaluates range+sight and wakes her (Max 10 m away, clear sight).
            boss.Tick(1f / 60f);
            boss.Tick(1f / 60f);
            Assert.IsTrue(boss.Engaged, "fixture: she must be awake for a volley to ever fire");

            var seen = new HashSet<CorrosiveGlob>();
            var spawnPositions = new Dictionary<CorrosiveGlob, Vector3>();
            var spawnTimes = new Dictionary<CorrosiveGlob, float>();
            const float dt = 0.02f;
            float elapsed = 0f;
            int iterations = 0;

            // Tick until all 5 (Phase1GlobCount) lobs of the first volley have launched -- capturing
            // each glob's position AND the elapsed time the FIRST time it's observed, before this same
            // frame's Tick call ever moves it, so "first seen" really does mean the instant it left the
            // mouth (the volley itself doesn't fire until _globTimer crosses Phase1GlobInterval, so most
            // of these iterations elapse before anything spawns at all).
            while (spawnPositions.Count < SludgequeenTuning.Phase1GlobCount && iterations < 500)
            {
                boss.Tick(dt);
                elapsed += dt;
                foreach (var glob in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                    if (seen.Add(glob)) { spawnPositions[glob] = glob.transform.position; spawnTimes[glob] = elapsed; }
                foreach (var glob in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                    glob.Tick(dt);
                iterations++;
            }

            Assert.AreEqual(SludgequeenTuning.Phase1GlobCount, spawnPositions.Count,
                $"AC1c: one volley at full health must fire exactly {SludgequeenTuning.Phase1GlobCount} lobs");

            // AC1c2: each lob first seen within 0.3 m of a cannon mouth transform.
            Vector3 mouth0 = rig.NozzleMouth(0).position, mouth1 = rig.NozzleMouth(1).position;
            foreach (Vector3 pos in spawnPositions.Values)
            {
                float d0 = Vector3.Distance(pos, mouth0), d1 = Vector3.Distance(pos, mouth1);
                Assert.LessOrEqual(Mathf.Min(d0, d1), 0.3f,
                    $"AC1c2: a lob must first be seen within 0.3 m of a cannon mouth, measured {Mathf.Min(d0, d1):F2} m");
            }

            // AC1c2: each glob's own rendered diameter >= 0.6 m, and it carries an enabled trail.
            foreach (CorrosiveGlob glob in spawnPositions.Keys)
            {
                MeshRenderer blob = glob.GetComponentInChildren<MeshRenderer>();
                Assert.IsNotNull(blob, "AC1c2: a lob must render a visible blob");
                float diameter = Mathf.Max(blob.bounds.size.x, blob.bounds.size.z);
                Assert.GreaterOrEqual(diameter, 0.6f, $"AC1c2: a lob's rendered diameter must be at least 0.6 m, measured {diameter:F2} m");

                var trail = glob.GetComponent<TrailRenderer>();
                Assert.IsNotNull(trail, "AC1c2: a lob must carry a trail");
                Assert.IsTrue(trail.enabled, "AC1c2: a lob's trail must be enabled");
            }

            // AC1c: five landing rings, resolved radius 1.5 m, pairwise at least 2 m apart.
            GroundRing[] rings = Object.FindObjectsByType<GroundRing>(FindObjectsSortMode.None)
                .Where(r => r.Visible).ToArray();
            Assert.AreEqual(SludgequeenTuning.Phase1GlobCount, rings.Length,
                "AC1c: one ring per lob must be visible the instant the volley has fully launched");

            foreach (GroundRing r in rings)
            {
                float resolvedRadius = r.transform.localScale.x * 0.5f;
                Assert.AreEqual(SludgequeenTuning.GlobSplashRadius, resolvedRadius, 0.01f,
                    $"AC1c: a landing ring's resolved radius must be 1.5 m, measured {resolvedRadius:F2} m");
            }

            for (int i = 0; i < rings.Length; i++)
                for (int j = i + 1; j < rings.Length; j++)
                {
                    float dist = Vector3.Distance(rings[i].transform.position, rings[j].transform.position);
                    Assert.GreaterOrEqual(dist, 1.95f,
                        $"AC1c: two landing rings must be at least 2 m apart, measured {dist:F2} m");
                }

            // AC1c: the anchor ring (at Max's own exact position) must stay visible for at least 0.9 s
            // after it was first seen, and the flight time floor (1.0 s) is what that ring hides on.
            GroundRing anchorRing = rings.First(r =>
                Vector2.Distance(new Vector2(r.transform.position.x, r.transform.position.z),
                    new Vector2(anchor.x, anchor.z)) < 0.05f);
            // The anchor lob is scheduled with zero stagger delay (ComputeLandingSpots puts it at index
            // 0) so it always launches strictly before every scattered lob in its own volley -- the
            // glob's SPAWN position (captured in spawnPositions, at the cannon mouth) can never be near
            // the anchor's LANDING position, so identify it by spawn order instead.
            CorrosiveGlob anchorGlob = spawnTimes.OrderBy(kv => kv.Value).First().Key;
            float targetElapsed = spawnTimes[anchorGlob] + 0.9f;
            while (elapsed < targetElapsed) { boss.Tick(dt); elapsed += dt; }
            Assert.IsTrue(anchorRing.Visible, "AC1c: a landing ring must still be visible 0.9 s after it was first shown");
        }

        // ================================================================= AC1d

        private void Check1dSplashPuddleAndRobotImmunity()
        {
            var boss = NewBoss(new Vector3(4000f, 0f, 0f), out _);
            Vector3 anchor = boss.transform.position + new Vector3(10f, 0f, 0f);
            NewMax(anchor);
            var sentinel = NewSentinel(anchor);
            var robot = NewRobot(anchor);
            float sentinelBefore = sentinel.HealthCurrent;
            float robotBefore = robot.HealthCurrent;

            boss.Tick(1f / 60f); // wake

            const float dt = 0.02f;
            int iterations = 0;
            bool anchorLobLaunched = false;

            // Tick until the anchor lob (the first one, zero stagger) has actually launched.
            while (!anchorLobLaunched && iterations < 400)
            {
                boss.Tick(dt);
                foreach (var glob in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                    glob.Tick(dt);
                anchorLobLaunched = Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None).Length > 0;
                iterations++;
            }
            Assert.IsTrue(anchorLobLaunched, "fixture: the anchor lob must launch for this sub-check to mean anything");

            // Tick until it lands (GlobFlightTime from its own launch -- it launched this same pass).
            for (int i = 0; i < 60; i++) // 1.2 s, comfortably past the 1.0 s flight time
            {
                boss.Tick(dt);
                foreach (var glob in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                    glob.Tick(dt);
                foreach (var puddle in Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None))
                    puddle.Tick(dt);
            }

            float sentinelAfterSplash = sentinelBefore - sentinel.HealthCurrent;
            Assert.AreEqual(14f, sentinelAfterSplash, 2f,
                $"AC1d: a sentinel standing on the landing spot must lose 14 health on impact, lost {sentinelAfterSplash:F1}");

            CorrosionPuddle[] puddles = Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None);
            CorrosionPuddle puddleHere = puddles.FirstOrDefault(p =>
                Vector2.Distance(new Vector2(p.transform.position.x, p.transform.position.z),
                    new Vector2(anchor.x, anchor.z)) < 0.1f);
            Assert.IsNotNull(puddleHere, "AC1d: a puddle must exist within 0.1 m of the ring's own centre after landing");
            Assert.AreEqual(SludgequeenTuning.GlobPuddleRadius, puddleHere.Radius, 0.1f,
                $"AC1d: the puddle's resolved radius must be 1.5 m (+-0.1), measured {puddleHere.Radius:F2} m");
            Assert.IsNotNull(puddleHere.GetComponentInChildren<MeshRenderer>(),
                "AC1d: the puddle must carry an enabled renderer");

            float sentinelBeforeDwell = sentinel.HealthCurrent;
            for (int i = 0; i < 100; i++) // 2 s standing in the puddle
            {
                boss.Tick(dt);
                foreach (var glob in Object.FindObjectsByType<CorrosiveGlob>(FindObjectsSortMode.None))
                    glob.Tick(dt);
                foreach (var puddle in Object.FindObjectsByType<CorrosionPuddle>(FindObjectsSortMode.None))
                    puddle.Tick(dt);
            }
            float dwellLoss = sentinelBeforeDwell - sentinel.HealthCurrent;
            Assert.AreEqual(12f, dwellLoss, 2f,
                $"AC1d: a sentinel standing in the puddle for 2 s must lose 12 (+-2) health, lost {dwellLoss:F1}");

            Assert.AreEqual(robotBefore, robot.HealthCurrent, 0.01f,
                "AC1d: a robot standing in the same puddle must lose nothing");
        }

        // ================================================================= AC1d (puddle lifetime)

        private void Check1ePuddleLifetimeIsSixSeconds()
        {
            // Direct, real entry point (CorrosionPuddle.Spawn) -- the same puddle a landed lob leaves,
            // proven on its own authored duration without needing a full boss/glob simulation to reach
            // the six-second mark.
            CorrosionPuddle puddle = CorrosionPuddle.Spawn(new Vector3(5000f, 0f, 0f),
                SludgequeenTuning.GlobPuddleRadius, SludgequeenTuning.GlobPuddleDuration, affectsRobots: false);
            Track(puddle.gameObject);

            const float dt = 0.1f;
            for (int i = 0; i < 57; i++) puddle.Tick(dt); // 5.7 s
            Assert.IsTrue(puddle != null && puddle.gameObject != null,
                "AC1d: the puddle must still exist at 5.7 s (within the 6 s +-0.3 s window)");

            for (int i = 0; i < 6; i++) if (puddle != null) puddle.Tick(dt); // to 6.3 s
            Assert.IsTrue(puddle == null || !puddle,
                "AC1d: the puddle must be gone by 6.3 s (6 s +-0.3 s)");
        }

        // ================================================================= AC1e

        private void Check1fSludgerConcurrentCap()
        {
            var boss = NewBoss(new Vector3(6000f, 0f, 0f), out _);
            NewMax(boss.transform.position + new Vector3(10f, 0f, 0f));
            boss.Tick(1f / 60f); // wake

            const float dt = 0.1f;
            int steps = Mathf.CeilToInt(120f / dt);
            int maxSeenAlive = 0;
            int firstBroodCount = -1;

            for (int i = 0; i < steps; i++)
            {
                boss.Tick(dt);

                RobotEnemy[] live = Object.FindObjectsByType<RobotEnemy>(FindObjectsSortMode.None)
                    .Where(r => r != null && r.IsAlive).ToArray();

                if (firstBroodCount < 0 && live.Length > 0) firstBroodCount = live.Length;
                maxSeenAlive = Mathf.Max(maxSeenAlive, live.Length);

                Assert.LessOrEqual(live.Length, SludgequeenTuning.MaxConcurrentBrood,
                    $"AC1e: her live robots must never exceed {SludgequeenTuning.MaxConcurrentBrood}, saw {live.Length} at t={i * dt:F1}s");
            }

            Assert.AreEqual(SludgequeenTuning.BroodCount, firstBroodCount,
                $"AC1e: one brood wave must emit exactly {SludgequeenTuning.BroodCount} robots");
            Assert.Greater(maxSeenAlive, 0, "fixture: at least one brood wave must have fired over 120 s");
        }

        // ================================================================= AC1f (contact damage)

        private void Check1gContactDamage()
        {
            var boss = NewBoss(new Vector3(7000f, 0f, 0f), out _);
            var maxGo = NewMax(boss.transform.position); // placed below, right at contact reach
            var sentinel = NewSentinel(boss.transform.position);

            boss.Tick(1f / 60f); // wakes, sets _playerTarget/_target
            float reach = ContactReachTo(boss, maxGo.transform) - 0.3f; // just inside her own measured reach
            maxGo.transform.position = boss.transform.position + new Vector3(reach, 0f, 0f);
            sentinel.transform.position = boss.transform.position + new Vector3(-reach, 0f, 0f);
            Physics.SyncTransforms();

            var maxHealth = maxGo.GetComponent<PlayerHealth>();
            float maxBefore = maxHealth.Current;
            float sentinelBefore = sentinel.HealthCurrent;

            const float dt = 1f / 60f;
            for (int i = 0; i < Mathf.CeilToInt(3f / dt); i++) boss.Tick(dt); // held in contact for 3 s

            float maxLost = maxBefore - maxHealth.Current;
            float sentinelLost = sentinelBefore - sentinel.HealthCurrent;
            Assert.AreEqual(45f, maxLost, 15f, $"AC1f: Max held against her body for 3 s must lose 45 (+-15), lost {maxLost:F1}");
            Assert.AreEqual(45f, sentinelLost, 15f, $"AC1f: a sentinel held against her body for 3 s must lose 45 (+-15), lost {sentinelLost:F1}");
        }

        // ================================================================= AC1h (finale chain)

        private void Check1hFinaleChain()
        {
            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.World2);
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string reason), reason);
            Assert.AreEqual(21, cfg.dials.areaCount, "fixture: a21 must be World 2's authored final area");

            var root = Track(new GameObject("MV1127 Finale Root"));
            MapBuild built = MapRuntime.Build(map, root.transform);
            Assert.IsTrue(built.Actors.TryGetValue("sludgequeen", out GameObject bossGo) && bossGo != null);
            var boss = bossGo.GetComponent<SludgequeenBoss>();

            var payoff = Track(new GameObject("BossVictoryPayoff Test")).AddComponent<BossVictoryPayoff>();
            var gate = Track(new GameObject("WorldFinaleGate Test")).AddComponent<WorldFinaleGate>();
            InvokeOnEnable(payoff);
            InvokeOnEnable(gate);

            var areaDirectorGo = Track(new GameObject("MV1127 AreaDirector"));
            var areaDirector = areaDirectorGo.AddComponent<AreaAccumulationDirector>();
            areaDirector.ConfigureWorld(cfg, worldIndex: 1); // World 2 is index 1 -- never Configure/EnterArea,
                                                              // so _finalAreaIndex resolves to 21 but no
                                                              // robot is ever stamped with it (same idiom
                                                              // MV997WorldExitDoorTests already uses).

            BossCensus.Register(boss, "SLUDGEQUEEN", 1, current: 100f, max: 100f, areaIndex: 21);

            Assert.IsFalse(gate.IsOpen, "fixture: the gate must stay shut before her death");
            Assert.AreEqual(0, Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None).Count(p => p.Kind == PickupKind.WeaponCore),
                "fixture: no Weapon Core may exist before her death");

            Vector3 deathPos = bossGo.transform.position;
            InvokeOnDeath(boss);

            Pickup core = Object.FindObjectsByType<Pickup>(FindObjectsSortMode.None)
                .SingleOrDefault(p => p.Kind == PickupKind.WeaponCore);
            Assert.IsNotNull(core, "AC1h: killing her must drop a Weapon Core");
            float dist = Vector2.Distance(new Vector2(core.transform.position.x, core.transform.position.z),
                new Vector2(deathPos.x, deathPos.z));
            Assert.LessOrEqual(dist, 1f, $"AC1h: the Weapon Core must land at her own death position, measured {dist:F2} m away");

            var pickupDirector = PickupDirector.EnsureInstalled();
            var liveField = typeof(PickupDirector).GetField("_live", NonPublicInstance);
            var live = (System.Collections.IList)liveField.GetValue(pickupDirector);
            int index = live.IndexOf(core);
            Assert.GreaterOrEqual(index, 0, "the dropped Core must be live on the pickup director");
            typeof(PickupDirector).GetMethod("Collect", NonPublicInstance).Invoke(pickupDirector, new object[] { index, core });

            gate.TickWeaponBeat(2.5f); // WeaponBeatDuration -- runs Beat A to its own end, then BeginCleanup
            Assert.IsTrue(gate.WeaponMomentResolved,
                "AC1h: WorldFinaleGate must reach its clean-up phase (or beyond) once the Core is collected");
        }
    }
}
