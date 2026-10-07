using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Player;
using MaxWorlds.UI;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1089 — World 3's TRAP ("CAPTURE") ability, through Lee's device playthrough: captured robots
    /// never turned green, kept firing/lunging at Max, and player-side weapons kept locking onto them.
    ///
    /// Root causes, found by reading the real render/targeting pipeline rather than guessing:
    /// (1) <see cref="RobotEnemy.BeginTrapConversion"/>/<see cref="RobotEnemy.TickTrapConversion"/> wrote
    /// the green lerp onto <c>GetComponent&lt;CharacterSkin&gt;()</c> — null for every real, RobotRig-built
    /// robot, because <see cref="CharacterSkinDirector"/> never dresses a renderer carrying
    /// <see cref="SelfDrivenTint"/>, which every <see cref="RobotRig"/> part does. The write was a no-op
    /// on screen (the same wrong-object shape MV-1071 fixed for Max's own hit flash).
    /// (2) <c>ShoulderRack.NearestAwakeRobotInRange</c> and <c>PlayerAbilities.ApplyForceFieldPop</c> and
    /// <c>Sentinel.IsEligibleTarget</c> never checked <see cref="RobotEnemy.Team"/> or
    /// <see cref="RobotEnemy.IsTrapHeld"/> at all, so a held or converted robot stayed a valid target/
    /// damage receiver for every one of them (WaterBlaster and Undertow already excluded both; this
    /// ticket brings the rest in line).
    /// (3) The HUD button and the RIG board's own <c>p_trp</c> node label still read "TRAP" everywhere
    /// the player actually sees it.
    /// (4) A captured robot's health bar followed the ordinary damage/target fade rule (MV-788) instead
    /// of showing permanently once converted.
    ///
    /// One consolidated test (MV-465 Rule 1) carries every acceptance criterion as a sub-check, through
    /// real entry points throughout: the real Chase-&gt;Telegraph-&gt;Lunge state machine
    /// (<see cref="RobotEnemy.Tick"/>) drives a Gunner into its real beam and a Rusher into its real
    /// melee lunge before either is ever caught; <see cref="RobotTrap.Spawn"/>/<see cref="RobotTrap.Tick"/>
    /// is the real capture path; <see cref="ShoulderRack.Tick"/> and <c>Undertow.Tick</c> (reflective —
    /// private, same idiom <c>MV1064UndertowLatchTests</c> already uses) are the real per-frame weapon
    /// loops; <see cref="RobotRig"/>'s own resolved body colour (never an authored constant) is read
    /// through its live <c>LateUpdate</c>; <see cref="WorldHealthBar"/>'s own visibility/fade state is
    /// read through its real <c>Refresh</c>/<c>ResolveGroups</c> pair (reflective, same idiom
    /// <c>MV788HealthBarVisibilityTests</c>/<c>MV1040HitBarTests</c> already use for this exact class).
    ///
    /// A reef skin is deliberately not applied to the Gunner/Rusher test bodies below — the three
    /// regressions this ticket fixes are Kind/RobotRig-level (colour resolution, team/held targeting
    /// exclusion), never reef-specific, and World 3's "Mine Urchin"/"Scrap Eel" reskins exercise exactly
    /// the same code paths a base Gunner/Rusher does.
    ///
    /// Must fail on base commit d30d293 (does not compile there): <see cref="RobotEnemy.TrapConversionProgress01"/>,
    /// <see cref="RobotRig.ResolvedBodyColor"/>, <see cref="RobotRig.BodyRendererEnabled"/> and
    /// <see cref="WorldHealthBar.FillColor"/> do not exist on that commit, and <c>RobotEnemy.TrapAllyColor</c>
    /// is private there — the same "doesn't compile on the pre-fix commit" evidence
    /// <c>MV1015ConvertedRobotFightsAndBurnsOutTests</c>/<c>MV1035TrapAbilityTests</c> already document
    /// for this same ability.
    /// </summary>
    public sealed class MV1089CaptureConversionTests
    {
        private const float Dt = 1f / 60f;

        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags NonPublicStatic = BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly MethodInfo RobotAwakeMethod = typeof(RobotEnemy).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo RobotOnEnableMethod = typeof(RobotEnemy).GetMethod("OnEnable", NonPublicInstance);
        private static readonly FieldInfo RobotCcField = typeof(RobotEnemy).GetField("_cc", NonPublicInstance);

        private static readonly MethodInfo RigAwakeMethod = typeof(RobotRig).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo RigLateUpdateMethod = typeof(RobotRig).GetMethod("LateUpdate", NonPublicInstance);

        private static readonly MethodInfo PlayerControllerAwakeMethod = typeof(PlayerController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowAwakeMethod = typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTickMethod = typeof(Undertow).GetMethod("Tick", NonPublicInstance);
        private static readonly FieldInfo PlayerControllerFacingField = typeof(PlayerController).GetField("_facing", NonPublicInstance);

        private static readonly MethodInfo HudAwakeMethod = typeof(HudController).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo HudOnEnableMethod = typeof(HudController).GetMethod("OnEnable", NonPublicInstance);
        private static readonly MethodInfo HudOnDisableMethod = typeof(HudController).GetMethod("OnDisable", NonPublicInstance);

        private static readonly MethodInfo BarOnEnableMethod = typeof(WorldHealthBar).GetMethod("OnEnable", NonPublicInstance);
        private static readonly MethodInfo BarOnDisableMethod = typeof(WorldHealthBar).GetMethod("OnDisable", NonPublicInstance);
        private static readonly MethodInfo BarRefreshMethod = typeof(WorldHealthBar).GetMethod("Refresh", NonPublicInstance);
        private static readonly MethodInfo BarResolveGroupsMethod = typeof(WorldHealthBar).GetMethod("ResolveGroups", NonPublicStatic);
        private static readonly FieldInfo BarSecondsSinceTriggerField = typeof(WorldHealthBar).GetField("_secondsSinceTrigger", NonPublicInstance);

        [SetUp]
        [TearDown]
        public void Clear()
        {
            RobotEnemy.ResetRegistry();
            RobotEnemy.ConversionCap = 1;
            LungeTokenPool.Reset();
            DevTuning.Reset();
            DevMode.Reset();
            EnemyNavigation.Reset();
            WeaponSystemState.Reset();
            RigState.Reset();
            RigBoard.UseWorld(0);
            PickupWallet.Reset();
            PlayerRocket.DestroyAllActive();
        }

        private static void InvokeUndertowTick(Undertow u, float dt) => UndertowTickMethod.Invoke(u, new object[] { dt });

        private static void RefreshBar(WorldHealthBar bar) => BarRefreshMethod.Invoke(bar, null);

        private static void ResolveGroups() =>
            BarResolveGroupsMethod.Invoke(null, new object[] { WorldHealthBar.DefaultGroupRadius, WorldHealthBar.DefaultPlateCap, Vector3.zero });

        private static RectTransform FindRect(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == name) return t;
            return null;
        }

        /// <summary>A real robot body: CharacterController -&gt; RobotEnemy (Awake -&gt; Apply -&gt; OnEnable,
        /// the same order <see cref="MaxWorlds.Enemies.EnemySpawner.CreateInstance"/> uses) -&gt; a real
        /// <see cref="RobotRig"/> (Awake -&gt; EnsureBuilt via the Awake call itself). This is the one
        /// difference from every earlier TRAP test's construction idiom (<c>MV1035TrapAbilityTests.NewRobot</c>
        /// hand-builds a <see cref="MeshRenderer"/> and binds a <see cref="CharacterSkin"/> directly) —
        /// deliberately, because that shortcut is exactly what let MV-1035 ship a conversion tint that
        /// writes to a component no real robot ever carries. Carrying <see cref="RobotEnemy.Awake"/> here
        /// also means this robot gets a real <see cref="WorldHealthBar"/>.</summary>
        private static RobotEnemy NewRigRobot(string name, Vector3 position, in EnemyArchetype archetype)
        {
            var go = new GameObject(name);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            RobotCcField.SetValue(e, cc); // Awake below also sets this; stamped first so nothing reads null in between
            RobotAwakeMethod.Invoke(e, null);
            e.Apply(archetype);
            RobotOnEnableMethod.Invoke(e, null);
            go.transform.position = position;

            var rig = go.AddComponent<RobotRig>();
            RigAwakeMethod.Invoke(rig, null);
            return e;
        }

        /// <summary>Max, through the real self-attach chain: <see cref="PlayerController.Awake"/> adds
        /// <see cref="PlayerAbilities"/>, <see cref="ShoulderRack"/> and <see cref="Undertow"/> itself —
        /// nothing here hand-builds any of the three.</summary>
        private static GameObject NewMax(out PlayerController player, out ShoulderRack rack, out Undertow undertow, out PlayerHealth health)
        {
            var go = new GameObject("MV1089 Max", typeof(CharacterController), typeof(PlayerController)) { tag = "Player" };
            player = go.GetComponent<PlayerController>();
            PlayerControllerAwakeMethod.Invoke(player, null);

            rack = go.GetComponent<ShoulderRack>();
            Assert.IsNotNull(rack, "fixture: PlayerController.Awake must self-attach a ShoulderRack");

            undertow = go.GetComponent<Undertow>();
            Assert.IsNotNull(undertow, "fixture: PlayerController.Awake must self-attach an Undertow");
            UndertowAwakeMethod.Invoke(undertow, null); // Awake doesn't reliably run for AddComponent outside Play mode

            health = go.AddComponent<PlayerHealth>();
            health.Initialize();
            return go;
        }

        [Test]
        public void CapturedRobotsTurnGreenStopAttackingAreIgnoredByWeaponsAndShowAHealthBar_MV1089()
        {
            // === AC(e) first, entirely decoupled from the capture mechanic: BuildTrapButton stamps the
            // label at construction time regardless of p_trp/visibility, so no RIG wiring is needed here.
            var hudGo = new GameObject("MV1089 HUD");
            GameObject maxGo = null, robotAGo = null, robotBGo = null, trapGo = null;
            try
            {
                var hud = hudGo.AddComponent<HudController>();
                HudAwakeMethod.Invoke(hud, null);
                HudOnEnableMethod.Invoke(hud, null);

                RectTransform trapButton = FindRect(hudGo, "Trap Button");
                Assert.IsNotNull(trapButton, "fixture: the capture button must exist in the HUD tree");
                Text trapLabel = trapButton.GetComponentInChildren<Text>(true);
                Assert.IsNotNull(trapLabel, "fixture: the capture button must carry a text label");
                Assert.AreEqual("CAPTURE", trapLabel.text,
                    "AC(e): the HUD button's resolved text must read CAPTURE, not TRAP");

                // === World 3 loadout for the weapon-targeting checks below (Undertow primary, Shoulder
                // Rack secondary, World 3's own RIG board) — ApplyWorldLoadout is the one real entry
                // point that sets all three together (WeaponSystemState.ApplyWorldLoadout's own doc).
                WeaponSystemState.ApplyWorldLoadout(2);
                RigState.RestoreSnapshot(
                    new System.Collections.Generic.Dictionary<string, int> { { "s_rkt", 1 }, { "s_sal", 1 } },
                    new[] { "SECONDARY" });
                PickupWallet.SetPowerCellSecondary(50);

                maxGo = NewMax(out PlayerController player, out ShoulderRack rack, out Undertow undertow, out PlayerHealth health);

                // Mine Urchin's real stand-in (a Gunner): standoff band holds it at 4.5m, well inside its
                // 6.3m lungeRange. Scrap Eel's real stand-in (a Rusher): 2.0m, inside its 2.2m lungeRange.
                // Perpendicular placement (X vs Z) so neither robot's own collider can occlude the
                // other's line of sight to Max.
                RobotEnemy mineUrchin = NewRigRobot("Mine Urchin", new Vector3(4.5f, 0f, 0f), EnemyArchetype.Gunner);
                RobotEnemy scrapEel = NewRigRobot("Scrap Eel", new Vector3(0f, 0f, 2.0f), EnemyArchetype.Rusher);
                robotAGo = mineUrchin.gameObject;
                robotBGo = scrapEel.gameObject;
                Physics.SyncTransforms();

                // === Drive both into their real attack states before either is caught — the state
                // machine's own Chase -> Telegraph -> Lunge, never forced.
                bool bothAttacking = false;
                for (int frame = 0; frame < 180 && !bothAttacking; frame++)
                {
                    mineUrchin.Tick(Dt);
                    scrapEel.Tick(Dt);
                    bothAttacking = mineUrchin.Current == RobotEnemy.State.Lunge && scrapEel.Current == RobotEnemy.State.Lunge;
                }
                Assert.IsTrue(bothAttacking,
                    "fixture: Mine Urchin (beam) and Scrap Eel (melee) must both reach State.Lunge before either is caught");

                // === Deploy the capture device and catch both mid-attack in the same tick (capacity 2,
                // both already within its radius) — RobotTrap.Spawn/Tick is the trap's own real entry
                // point (same idiom its own MV1035TrapAbilityTests fillTrap section uses). The live ally
                // cap must allow both conversions, or TryConvert silently refuses the second one — the
                // same cap TickTrapConversion itself bypasses nothing for.
                RobotEnemy.ConversionCap = 2;
                trapGo = null;
                RobotTrap trap = RobotTrap.Spawn(maxGo.transform.position, capacity: 2, radius: 8f, onDespawn: null);
                trapGo = trap.gameObject;
                trap.Tick(Dt);

                Assert.IsTrue(mineUrchin.IsTrapHeld, "Mine Urchin must be caught mid-beam");
                Assert.IsTrue(scrapEel.IsTrapHeld, "Scrap Eel must be caught mid-lunge");
                Assert.IsTrue(trap.IsConverting, "catching both at once (capacity 2) must start conversion immediately");

                // === AC(a): from the catch frame on, Max takes 0 damage from either, through the whole
                // 4.5s conversion lerp — both robots are frozen (RobotEnemy.Tick's own _trapHeld
                // early-out) and never re-enter an attack state.
                float healthAfterCatch = health.HealthCurrent;
                int framesTo4_5s = Mathf.CeilToInt(4.55f / Dt);
                for (int i = 0; i < framesTo4_5s && trap != null; i++)
                {
                    trap.Tick(Dt);
                    mineUrchin.Tick(Dt);
                    scrapEel.Tick(Dt);
                    Assert.AreEqual(healthAfterCatch, health.HealthCurrent, 0f,
                        $"AC(a) tick {i}: Max must take 0 damage from a held/converting robot");
                    Assert.AreNotEqual(RobotEnemy.State.Telegraph, mineUrchin.Current, $"tick {i}: a held robot must never telegraph");
                    Assert.AreNotEqual(RobotEnemy.State.Telegraph, scrapEel.Current, $"tick {i}: a held robot must never telegraph");
                }

                Assert.IsTrue(mineUrchin.IsConverted, "Mine Urchin must be a permanent ally once the 4.5s lerp completes");
                Assert.IsTrue(scrapEel.IsConverted, "Scrap Eel must be a permanent ally once the 4.5s lerp completes");
                Assert.IsTrue(trap == null, "the trap must despawn once everything it holds has converted");

                // === AC(b): at 4.5s, every ENABLED body renderer of each resolves to the ally green
                // within 0.05/channel — read off RobotRig's own resolved render state (property-block
                // override included), never an authored constant.
                AssertCapturedGreen(mineUrchin, "Mine Urchin");
                AssertCapturedGreen(scrapEel, "Scrap Eel");

                // === AC(c): with both captured robots nearest to Max (nothing else on the field), the
                // Shoulder Rack must fire at nothing and UNDERTOW must latch onto nothing, over 10s.
                int shoulderRackFrames = Mathf.CeilToInt(10f / Dt);
                for (int i = 0; i < shoulderRackFrames; i++) rack.Tick(Dt);
                Assert.AreEqual(0, PlayerRocket.Active.Count,
                    "AC(c): the Shoulder Rack must never fire while every robot in range is held/captured");

                DevMode.Enabled = true;
                DevMode.Invincible = true;
                DevMode.InfiniteEnergy = true;
                DevMode.AutoFire = true;
                Vector3 aimAtScrapEel = (scrapEel.transform.position - maxGo.transform.position).normalized;
                PlayerControllerFacingField.SetValue(player, aimAtScrapEel);

                const int undertowFrames = 100; // 100 x 0.1s = 10s
                for (int i = 0; i < undertowFrames; i++)
                {
                    InvokeUndertowTick(undertow, 0.1f);
                    Assert.IsNull(undertow.LatchedTransform,
                        $"AC(c) tick {i}: UNDERTOW must never latch a held/captured robot — nothing else is in range");
                }

                // === AC(d): an enemy damage source reduces the captured robot's health, and its health
                // bar is visible BOTH before (despite no recent damage/target trigger — proving the
                // permanent-while-captured rule, not the ordinary 3s fade window) and after.
                var bar = scrapEel.gameObject.GetComponent<WorldHealthBar>();
                Assert.IsNotNull(bar, "fixture: a real robot (RobotEnemy.Awake) must carry its own WorldHealthBar");
                BarOnEnableMethod.Invoke(bar, null);

                BarSecondsSinceTriggerField.SetValue(bar, 999f); // long past the ordinary 3.4s fade window
                RefreshBar(bar);
                ResolveGroups();
                Assert.IsTrue(bar.Showing,
                    "AC(d) before: a captured robot's bar must show permanently, not only within the ordinary damage/target fade window");

                float healthBeforeEnemyHit = scrapEel.HealthCurrent;
                scrapEel.TakeDamage(new DamageInfo(5f, scrapEel.transform.position, Vector3.up, Team.Enemy, source: DamageSource.Unspecified));
                Assert.Less(scrapEel.HealthCurrent, healthBeforeEnemyHit, "AC(d): an enemy damage source must reduce the captured robot's health");

                RefreshBar(bar);
                ResolveGroups();
                Assert.IsTrue(bar.Showing, "AC(d) after: the captured robot's health bar must still be visible once damaged");
            }
            finally
            {
                if (hudGo != null)
                {
                    var hud = hudGo.GetComponent<HudController>();
                    if (hud != null) HudOnDisableMethod.Invoke(hud, null);
                    Object.DestroyImmediate(hudGo);
                }
                if (trapGo != null) Object.DestroyImmediate(trapGo);
                if (maxGo != null) Object.DestroyImmediate(maxGo);
                // MV-1040's own precedent: a reflection-invoked OnEnable never went through Unity's real
                // enable state machine, so DestroyImmediate below won't fire a matching OnDisable to pull
                // this bar back out of WorldHealthBar's own static _active registry — do it explicitly,
                // or the next test in the batch that calls ResolveGroups inherits a destroyed entry.
                if (robotBGo != null)
                {
                    var bar = robotBGo.GetComponent<WorldHealthBar>();
                    if (bar != null) BarOnDisableMethod.Invoke(bar, null);
                }
                if (robotAGo != null) Object.DestroyImmediate(robotAGo);
                if (robotBGo != null) Object.DestroyImmediate(robotBGo);
            }
        }

        private static void AssertCapturedGreen(RobotEnemy robot, string label)
        {
            var rig = robot.GetComponent<RobotRig>();
            Assert.IsNotNull(rig, $"fixture: {label} must carry a real RobotRig");
            RigLateUpdateMethod.Invoke(rig, null);

            Assert.IsTrue(rig.BodyRendererEnabled, $"AC(b): {label}'s body renderer must be enabled (visible) once captured");
            Color resolved = rig.ResolvedBodyColor;
            Assert.AreEqual(RobotEnemy.TrapAllyColor.r, resolved.r, 0.05f, $"AC(b): {label}'s resolved body colour red channel must match the ally green");
            Assert.AreEqual(RobotEnemy.TrapAllyColor.g, resolved.g, 0.05f, $"AC(b): {label}'s resolved body colour green channel must match the ally green");
            Assert.AreEqual(RobotEnemy.TrapAllyColor.b, resolved.b, 0.05f, $"AC(b): {label}'s resolved body colour blue channel must match the ally green");
        }
    }
}
