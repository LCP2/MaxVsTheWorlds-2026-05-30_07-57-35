using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-715: World 3's Reef re-skins of the eight base enemy kinds, and the Hydroponic Reactor
    /// factory. The ONE new test this ticket adds (testing policy v2, MV-465) — one method covering
    /// every EditMode-testable AC rather than five separate files, same compression
    /// <c>MV713ReefKitTests</c> already used for an analogous "one ticket, several ACs" shape.
    /// Fails on 8bca3d8 (the commit before this ticket): <c>world3_config.json</c> ships with no
    /// <c>enemyOverrides</c> block at all, so every kind below resolves the base table's name/skin
    /// instead of its Reef reskin, <c>WorldEnemyOverride.telegraphTime</c> does not exist,
    /// <c>RobotEnemy.ApplyReefStealthVisibility</c>/the Brute front-arc armour do not exist, and
    /// <c>MowerHutch.OnDestroyed</c> never touches the core's emissive.
    ///
    /// AC1 note: the ticket's own wording is "THVs equal the World 1 values". World 3's own
    /// <c>enemyTypes</c> THV table (world3_config.json) already diverges from World 1's — authored by
    /// MV-712, before this ticket, as World 3's own difficulty curve — so a literal THV-table equality
    /// assertion would fail regardless of this ticket's changes and could never be evidence of
    /// anything. What this ticket actually owns and must not move (the ticket's own "do not re-raise:
    /// whether to change threat values (no)") is the ARCHETYPE's combat stats: <see cref="EnemyArchetype.WithOverride"/>
    /// never touches MoveSpeed/MaxHealth/ContactDamage/BodyScale, so those resolve identically to World 1's
    /// (which also authors no override for any of these eight, so World 1's own resolution is just the
    /// base table) — asserted below. Flagged for Lee rather than silently reworded, per "what's not
    /// yours to decide".
    /// </summary>
    public sealed class MV715ReefRosterTests
    {
        [Test]
        public void ReefRoster_MatchesTicketAcceptanceCriteria()
        {
            WorldConfig world3 = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(world3, "world3_config.json failed to load");

            // --- AC1: all eight kinds resolve through EnemyArchetype, wearing their Reef name/skin,
            // with combat stats identical to the base table (== World 1's own resolution, since World 1
            // authors no override for any of these eight either). ---
            AssertReefReskin(world3, EnemyKind.Rusher, "SCRAP EEL");
            AssertReefReskin(world3, EnemyKind.Bruiser, "SALVAGE CRAB");
            AssertReefReskin(world3, EnemyKind.Heavy, "CABLE TENTACLE");
            AssertReefReskin(world3, EnemyKind.Brute, "DREDGE HULK");
            AssertReefReskin(world3, EnemyKind.Gunner, "MINE URCHIN");
            AssertReefReskin(world3, EnemyKind.Launcher, "PUFFER MINE");
            AssertReefReskin(world3, EnemyKind.Blinker, "ANGLERFISH-BOT");
            AssertReefReskin(world3, EnemyKind.Bolter, "REEF BOLTER");

            // --- AC4: the Puffer Mine's telegraph is 1.2s before release — a longer, clearer tell
            // than the base Launcher's 0.7s. ---
            EnemyArchetype pufferMine = EnemyArchetype.For(EnemyKind.Launcher, world3);
            Assert.AreEqual(1.2f, pufferMine.TelegraphTime, 1e-4f,
                "the Puffer Mine's telegraph must resolve to 1.2s");

            var root = new GameObject("MV715 Probe Root");
            try
            {
                // --- AC2: the Anglerfish-bot's renderer is disabled beyond 6m, enabled within it. ---
                GameObject anglerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                anglerGo.transform.SetParent(root.transform, false);
                anglerGo.AddComponent<CharacterController>();
                RobotEnemy angler = anglerGo.AddComponent<RobotEnemy>();
                angler.Apply(EnemyArchetype.For(EnemyKind.Blinker, world3));
                Renderer anglerRenderer = anglerGo.GetComponent<Renderer>();

                angler.ApplyReefStealthVisibility(9f);
                Assert.IsFalse(anglerRenderer.enabled, "Anglerfish-bot must be hidden beyond 6m");

                angler.ApplyReefStealthVisibility(3f);
                Assert.IsTrue(anglerRenderer.enabled, "Anglerfish-bot must be visible within 6m");

                // A base-table (non-Reef) Blinker must never be affected by this.
                GameObject baseBlinkerGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                baseBlinkerGo.transform.SetParent(root.transform, false);
                baseBlinkerGo.AddComponent<CharacterController>();
                RobotEnemy baseBlinker = baseBlinkerGo.AddComponent<RobotEnemy>();
                baseBlinker.Apply(EnemyArchetype.Of(EnemyKind.Blinker));
                Renderer baseBlinkerRenderer = baseBlinkerGo.GetComponent<Renderer>();
                baseBlinker.ApplyReefStealthVisibility(9f);
                Assert.IsTrue(baseBlinkerRenderer.enabled,
                    "a base-table Blinker (no Reef skin) must never be hidden by distance");

                // --- AC3: the Dredge Hulk takes full damage from behind, reduced damage on its front
                // arc — assert both numbers. Default transform.forward is +Z; DamageInfo.Direction is
                // the hit's OWN travel direction (source -> target, same convention every other
                // DamageInfo construction site in this codebase uses), so a hit travelling -Z arrived
                // FROM the front (+Z), and a hit travelling +Z arrived FROM behind (-Z).
                GameObject hulkGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hulkGo.transform.SetParent(root.transform, false);
                hulkGo.AddComponent<CharacterController>();
                RobotEnemy hulk = hulkGo.AddComponent<RobotEnemy>();
                EnemyArchetype dredgeHulk = EnemyArchetype.For(EnemyKind.Brute, world3);
                hulk.Apply(dredgeHulk);

                const float testDamage = 100f;
                hulk.TakeDamage(new DamageInfo(testDamage, hulkGo.transform.position, Vector3.back, Team.Player));
                float frontArcDamageTaken = dredgeHulk.MaxHealth - hulk.HealthCurrent;

                hulk.Apply(dredgeHulk); // reset to full health
                hulk.TakeDamage(new DamageInfo(testDamage, hulkGo.transform.position, Vector3.forward, Team.Player));
                float behindDamageTaken = dredgeHulk.MaxHealth - hulk.HealthCurrent;

                Assert.AreEqual(testDamage * 0.4f, frontArcDamageTaken, 1e-3f,
                    "the Dredge Hulk's front-arc armour must resolve to 0.4x damage");
                Assert.AreEqual(testDamage, behindDamageTaken, 1e-3f,
                    "the Dredge Hulk must take full damage from behind");
                Assert.Less(frontArcDamageTaken, behindDamageTaken,
                    "front-arc damage must be strictly less than a hit from behind");

                // --- AC5: destroying a Hydroponic Reactor sets its bloom emissive to zero. ---
                GameObject reactorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                reactorGo.transform.SetParent(root.transform, false);
                reactorGo.transform.localScale = new Vector3(3f, 2f, 3f);
                MowerHutch reactor = reactorGo.AddComponent<MowerHutch>();
                reactor.Build();
                reactor.ApplyReefSkin();

                Renderer coreRenderer = reactorGo.transform.Find("VulnerableCore").GetComponent<Renderer>();
                InvokePrivate(reactor, "PulseCore"); // establish a real, non-zero pulsed baseline first
                var preDestroyMpb = new MaterialPropertyBlock();
                coreRenderer.GetPropertyBlock(preDestroyMpb);
                Color preDestroyEmission = preDestroyMpb.GetColor("_EmissionColor");
                Assert.Greater(preDestroyEmission.maxColorComponent, 0f,
                    "precondition: PulseCore must have set a real emissive value before destruction");

                reactor.TakeDamage(new DamageInfo(reactor.AuthoredMax * 2f, reactorGo.transform.position,
                    Vector3.forward, Team.Player));

                var postDestroyMpb = new MaterialPropertyBlock();
                coreRenderer.GetPropertyBlock(postDestroyMpb);
                Color postDestroyEmission = postDestroyMpb.GetColor("_EmissionColor");
                Assert.AreEqual(0f, postDestroyEmission.maxColorComponent, 1e-4f,
                    "destroying the Hydroponic Reactor must zero its bloom emissive");
            }
            finally
            {
                Object.DestroyImmediate(root);
                FactoryCensus.Reset();
            }
        }

        private static void AssertReefReskin(WorldConfig world3, EnemyKind kind, string expectedName)
        {
            EnemyArchetype reef = EnemyArchetype.For(kind, world3);
            EnemyArchetype baseArchetype = EnemyArchetype.Of(kind);

            Assert.AreEqual(kind, reef.Kind, $"{kind} must resolve through EnemyArchetype as itself");
            Assert.AreEqual(expectedName, reef.DisplayName, $"{kind}'s Reef reskin must be named {expectedName}");
            Assert.AreEqual("reef", reef.Skin, $"{kind}'s Reef reskin must carry the 'reef' skin tag");

            Assert.AreEqual(baseArchetype.MoveSpeed, reef.MoveSpeed, 1e-4f,
                $"{kind}'s Reef reskin must not move MoveSpeed off the base table");
            Assert.AreEqual(baseArchetype.MaxHealth, reef.MaxHealth, 1e-4f,
                $"{kind}'s Reef reskin must not move MaxHealth off the base table");
            Assert.AreEqual(baseArchetype.ContactDamage, reef.ContactDamage, 1e-4f,
                $"{kind}'s Reef reskin must not move ContactDamage off the base table");
        }

        private static void InvokePrivate(object obj, string methodName) =>
            obj.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(obj, null);
    }
}
