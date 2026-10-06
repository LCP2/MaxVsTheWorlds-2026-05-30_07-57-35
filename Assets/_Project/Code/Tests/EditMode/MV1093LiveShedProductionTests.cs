using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1093 — Lee's device report: "Mower hutches are no threat. They produce robots way too
    /// infrequently" (World 1), and in World 3 "I can see a door sliding up and down quite fast but no
    /// robots are being produced." Three causes, all in <see cref="EnemySpawner"/>: (A) every World 3
    /// shed area carries a <see cref="WorldArea.garrison"/> but no <see cref="WorldComposition"/>, so
    /// <see cref="EnemyMix.AreaCadence"/> was permanently empty and <c>WantsToEmit</c> never checked
    /// that — the door cycled on nothing. (B) a live shed's own cap ramped from 0. (C) the field-wide
    /// room check read <see cref="RobotEnemy.ActiveCount"/>, which counts a sleeping, pre-placed
    /// garrison within Max's reach (enabled, just Dormant) as if it were a live threat, starving a shed
    /// in an otherwise-empty room.
    ///
    /// Fails to even COMPILE on base commit d30d293: <see cref="EnemySpawner.Tick"/>,
    /// <see cref="EnemySpawner.Cadence"/> and <see cref="RobotEnemy.AwakeCount"/> do not exist there,
    /// and <see cref="WorldRunner"/> never derives a composition from an area's garrison — the same
    /// "fails because the surface under test doesn't exist yet" shape <c>ReplicatorTests</c> (MV-706,
    /// commit 0a80ab1: "no Replicator type exists there at all") and <c>MV828ReplicatorAreaIndexTests</c>
    /// already carry in this suite. Quoted in this ticket's fix comment.
    ///
    /// ONE test (MV-465 Rule 1), driving the real per-shed entry points — <see cref="WorldLibrary.Load"/>/
    /// <see cref="WorldMapLoader.TryLoad"/>/<see cref="MapRuntime.Build"/>/<see cref="WorldRunner.Configure"/>
    /// resolve EVERY shed's real area composition (World 3's, from its garrison) exactly as a live run
    /// does, then <see cref="EnemySpawner.Tick"/> and <see cref="EnemySpawner.SetAreaPaused"/> (the same
    /// public entry point <see cref="WorldRunner"/> itself calls off Max's live position every frame)
    /// drive production directly — no hand-rolled re-implementation of the area-presence gate.
    ///
    /// Tier 2 (resolved values): every assertion reads <see cref="EnemySpawner.Emitted"/>,
    /// <see cref="EnemySpawner.WantsToEmit"/>, <see cref="EnemySpawner.LiveCountOf"/> or
    /// <see cref="EnemySpawner.Cadence"/>'s resolved authored set — never an authored constant asserted
    /// back at itself, and never a bare "it exists" presence check (Rule 3).
    /// </summary>
    public sealed class MV1093LiveShedProductionTests
    {
        private static readonly FieldInfo CcField =
            typeof(RobotEnemy).GetField("_cc", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly MethodInfo OnEnableMethod =
            typeof(RobotEnemy).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<GameObject> _spawned = new List<GameObject>(64);

        [SetUp]
        public void SetUp()
        {
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            DifficultyDirector.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned) if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
            RobotEnemy.ResetRegistry();
            EnemySpawner.ResetReplicatorReservations();
            DevTuning.Reset();
            DifficultyDirector.Reset();
        }

        private GameObject NewRoot(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        /// <summary>Same "no OnEnable as a side effect of AddComponent outside Play mode" reflection
        /// workaround <c>MV809ReplicatorNeverShredsTests.NewLiveRobot</c> already uses, so this robot
        /// genuinely registers in <see cref="RobotEnemy.Active"/>/<see cref="RobotEnemy.AwakeCount"/> the
        /// same way a real Play-mode spawn/placement would.</summary>
        private RobotEnemy NewRegisteredRobot(Vector3 position)
        {
            var go = new GameObject("MV1093-robot");
            _spawned.Add(go);
            var cc = go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            CcField.SetValue(e, cc);
            e.Apply(EnemyArchetype.Of(EnemyKind.Rusher));
            OnEnableMethod.Invoke(e, null);
            e.transform.position = position;
            return e;
        }

        [Test]
        public void EveryShedInEveryShippedWorld_ProducesOnlyWhileMaxStandsInItsOwnArea()
        {
            foreach (string worldKey in WorldLibrary.Keys)
            {
                WorldConfig cfg = WorldLibrary.Load(worldKey);
                Assert.IsNotNull(cfg, $"setup failure: {worldKey} did not load");
                Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string loadReason),
                    $"setup failure: {worldKey} did not build a map: {loadReason}");

                GameObject root = NewRoot($"MV1093 root {worldKey}");
                MapBuild built = MapRuntime.Build(map, root.transform);

                var runner = root.AddComponent<WorldRunner>();
                // null AreaAccumulationDirector: Configure tolerates it (it's only used for the
                // PlayerCrossedIntoArea hookup, irrelevant here) and this is what actually resolves
                // every shed's real composition (WorldComposition, or World 3's derived-from-garrison
                // fallback) through WorldRunner's own real code, exactly as a live run does.
                runner.Configure(cfg, map, built, null);

                foreach (WorldArea area in cfg.areas)
                {
                    WorldShed[] sheds = area.Sheds();
                    for (int i = 0; i < sheds.Length; i++)
                    {
                        string shedId = area.ShedId(i, sheds.Length);
                        if (!built.Actors.TryGetValue(shedId, out GameObject shedGo) || shedGo == null)
                            continue;
                        var spawner = shedGo.GetComponent<EnemySpawner>();
                        if (spawner == null) continue;

                        string label = $"{worldKey}/{shedId}";

                        // === Max in a DIFFERENT area: never wants to emit, emits nothing (AC1, "it has ===
                        // === emitted 0 and its door openness stayed 0" — FactoryDoorway.Update's own ===
                        // === shouldBeOpen is `WantsToEmit || holdTimer>0`, and holdTimer only ever grows ===
                        // === off Emitted increasing, so WantsToEmit false throughout IS door-stayed-shut). ===
                        spawner.SetAreaPaused(true);
                        for (int t = 0; t < 10; t++)
                        {
                            Assert.IsFalse(spawner.WantsToEmit,
                                $"{label}: must not want to emit while Max is in a different area");
                            spawner.Tick(3f);
                        }
                        Assert.AreEqual(0, spawner.Emitted,
                            $"{label}: must have emitted nothing while Max was in a different area");

                        // === Max standing in the shed's OWN area: >=6 emitted in 30 simulated seconds, ===
                        // === every one of a kind this area actually authors. ===
                        spawner.SetAreaPaused(false);
                        for (int t = 0; t < 60; t++) spawner.Tick(0.5f); // 60 x 0.5s = 30 simulated seconds

                        Assert.GreaterOrEqual(spawner.Emitted, 6,
                            $"{label}: must emit at least 6 robots in 30s with Max present and the field clear");

                        foreach (EnemyKind kind in (EnemyKind[])System.Enum.GetValues(typeof(EnemyKind)))
                        {
                            if (spawner.LiveCountOf(kind) > 0)
                            {
                                Assert.IsTrue(spawner.Cadence.Authors(kind),
                                    $"{label}: emitted a {kind}, which this area's resolved cadence does not author");
                            }
                        }
                    }
                }
            }

            // === Field-wide: 60 Dormant robots elsewhere must not change the result with Max present ===
            // === (MV-1093's AwakeCount fix). A field-wide rule, so proven once on a fresh, isolated ===
            // === spawner — rather than reusing one of the 30 real sheds above, which would conflate ===
            // === this with that shed's own PER-FACTORY cap (8) after it already emitted ~7. ===
            GameObject globalBudgetGo = NewRoot("MV1093 global-budget spawner");
            var globalBudgetSpawner = globalBudgetGo.AddComponent<EnemySpawner>();
            globalBudgetSpawner.ConfigureAreaComposition(new WorldComposition { rusher = 1 });
            globalBudgetSpawner.SetAreaPaused(false);

            for (int d = 0; d < 60; d++)
            {
                RobotEnemy dormant = NewRegisteredRobot(new Vector3(200000f + d, 0f, 200000f));
                dormant.BeginDormant(authoredSlot: true);
            }
            Assert.AreEqual(RobotEnemy.State.Dormant, RobotEnemy.Active[0].Current,
                "setup failure: the 60 field-wide robots must actually be Dormant");

            for (int t = 0; t < 60; t++) globalBudgetSpawner.Tick(0.5f); // 30 simulated seconds

            Assert.GreaterOrEqual(globalBudgetSpawner.Emitted, 6,
                "60 Dormant robots elsewhere must not block production - the field-wide room check used " +
                "to count every ENABLED robot (RobotEnemy.ActiveCount), including sleeping, pre-placed " +
                "garrisons, as if each were a live threat");
        }
    }
}
