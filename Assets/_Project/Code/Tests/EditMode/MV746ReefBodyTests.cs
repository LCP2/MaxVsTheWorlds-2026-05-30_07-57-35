using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Enemies;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-746: World 3's eight kinds get their own bodies instead of wearing World 1's — MV-715
    /// renamed the kinds but never re-shaped them. The ONE new test this ticket adds (testing policy
    /// v2, MV-465), covering every EditMode-testable AC in one method, the same "one ticket, several
    /// ACs" compression <c>MV713ReefKitTests</c>/<c>MV715ReefRosterTests</c> already use.
    ///
    /// Fails on 5005071 (the commit before this ticket): <c>RobotBodies.Build</c> takes no skin
    /// parameter at all, so every World 3 kind's mesh set is reference-equal to its World 1
    /// counterpart's (built from the exact same cached <see cref="CharacterMeshes"/> entries),
    /// <c>RobotRig.BuildMaterials</c> never reads <see cref="WorldMaterials"/>' Reef colours, and
    /// nothing in the roster ever calls <c>EnableKeyword("_EMISSION")</c>.
    /// </summary>
    public sealed class MV746ReefBodyTests
    {
        private static readonly EnemyKind[] ReefKinds =
        {
            EnemyKind.Rusher, EnemyKind.Bruiser, EnemyKind.Heavy, EnemyKind.Brute,
            EnemyKind.Gunner, EnemyKind.Launcher, EnemyKind.Blinker, EnemyKind.Bolter,
        };

        [Test]
        public void ReefBodies_MatchTicketAcceptanceCriteria()
        {
            // --- AC1: each World 3 kind's BODY mesh set (renderers other than its eye lens/lure —
            // every kind in the roster, World 1 and World 3 alike, already shares one cached Sphere
            // mesh for its eye, and that overlap predates this ticket) is not reference-equal to its
            // World 1 counterpart's, and no two World 3 kinds share a mesh set. ---
            var reefMeshSets = new Dictionary<EnemyKind, HashSet<Mesh>>();
            foreach (EnemyKind kind in ReefKinds)
            {
                HashSet<Mesh> world1Meshes = BodyMeshes(kind, skin: null);
                HashSet<Mesh> world3Meshes = BodyMeshes(kind, skin: "reef");
                reefMeshSets[kind] = world3Meshes;

                Assert.IsFalse(world1Meshes.Overlaps(world3Meshes),
                    $"{kind}'s Reef body mesh set shares a mesh with its World 1 counterpart's");
            }
            foreach (EnemyKind a in ReefKinds)
                foreach (EnemyKind b in ReefKinds)
                {
                    if (a >= b) continue;
                    Assert.IsFalse(reefMeshSets[a].Overlaps(reefMeshSets[b]),
                        $"{a}'s and {b}'s Reef body mesh sets overlap — they must not share a kind's silhouette");
                }

            // --- AC2/AC3/AC4/AC5, driven through the real spawn path (EnemySpawner.CreateInstance +
            // RobotRig.EnsureBuilt), the same technique MV535RobotBodyOrderingTests uses, so materials
            // come from the real RobotRig.BuildMaterials and colliders from the real sizing in
            // EnemySpawner.CreateInstance rather than a hand-rolled stand-in. ---
            WorldConfig world3Config = WorldLibrary.Load(WorldLibrary.World3);
            Assert.IsNotNull(world3Config, "world3_config.json failed to load");

            Color[] reefColours =
            {
                WorldMaterials.ReefHazard, WorldMaterials.ReefCircuitCyan,
                WorldMaterials.ReefMetalDark, WorldMaterials.ReefBioGlow,
            };

            foreach (EnemyKind kind in ReefKinds)
            {
                EnemyArchetype reefArchetype = EnemyArchetype.For(kind, world3Config);
                GameObject reefGo = null;
                try
                {
                    RobotRig reefRig = BuildViaSpawner(reefArchetype, out reefGo);

                    // AC2: every renderer that carries a body colour at all wears a WorldMaterials
                    // Reef colour. Eyes/lures — every kind's, World 1 and World 3 alike — wear the
                    // shared additive glow material (VfxMaterials.Additive), which happens to expose
                    // its own default-white _BaseColor; that is not a body colour, so it is excluded
                    // via RobotRig's own private _eyes array, the same set RobotBodies.Body.Eyes feeds.
                    var greybox = reefGo.GetComponent<MeshRenderer>();
                    var eyeRenderers = new HashSet<MeshRenderer>(
                        (MeshRenderer[])typeof(RobotRig).GetField("_eyes", BindingFlags.NonPublic | BindingFlags.Instance)
                            .GetValue(reefRig) ?? System.Array.Empty<MeshRenderer>());
                    foreach (MeshRenderer r in reefGo.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        if (r == greybox || eyeRenderers.Contains(r) || r.sharedMaterial == null) continue;
                        if (!r.sharedMaterial.HasProperty("_BaseColor")) continue;

                        Color c = r.sharedMaterial.GetColor("_BaseColor");
                        Assert.IsTrue(reefColours.Any(rc => ColourNear(rc, c)),
                            $"{kind}'s renderer '{r.name}' wears {c}, not a WorldMaterials Reef colour");
                    }

                    // AC3: collider sizing is untouched — EnemyArchetype.WithOverride never overrides
                    // ColliderHeight/ColliderRadius, so the Reef archetype's own resolved values must
                    // still match World 1's within 0.01m.
                    EnemyArchetype world1Archetype = EnemyArchetype.Of(kind);
                    Assert.AreEqual(world1Archetype.ColliderHeight, reefArchetype.ColliderHeight, 0.01f,
                        $"{kind}'s Reef collider height must match World 1's within 0.01m");
                    Assert.AreEqual(world1Archetype.ColliderRadius, reefArchetype.ColliderRadius, 0.01f,
                        $"{kind}'s Reef collider radius must match World 1's within 0.01m");
                    var cc = reefGo.GetComponent<CharacterController>();
                    Assert.IsNotNull(cc, $"{kind}'s Reef instance has no CharacterController");

                    if (kind == EnemyKind.Blinker)
                    {
                        // AC4: the Anglerfish-bot's lure is the only renderer on it with emission
                        // enabled — nothing else this ticket builds ever calls EnableKeyword("_EMISSION").
                        var emissive = reefGo.GetComponentsInChildren<MeshRenderer>(true)
                            .Where(r => r != greybox && r.sharedMaterial != null
                                     && r.sharedMaterial.IsKeywordEnabled("_EMISSION"))
                            .ToList();
                        Assert.AreEqual(1, emissive.Count,
                            "the Anglerfish-bot must have exactly one renderer with emission enabled");
                        Assert.AreEqual("Lure", emissive[0].name,
                            "the Anglerfish-bot's one emissive renderer must be its lure");
                    }

                    if (kind == EnemyKind.Launcher)
                    {
                        // AC5: the Puffer Mine's inflatable core scales UP over its telegraph.
                        Transform inflatable = reefGo.GetComponentsInChildren<Transform>(true)
                            .FirstOrDefault(t => t.name == "Inflatable");
                        Assert.IsNotNull(inflatable, "the Puffer Mine must expose an Inflatable body part");

                        MethodInfo updateInflate = typeof(RobotRig).GetMethod(
                            "UpdateReefInflate", BindingFlags.NonPublic | BindingFlags.Instance);
                        Assert.IsNotNull(updateInflate, "RobotRig.UpdateReefInflate went missing");

                        RobotEnemy enemy = reefGo.GetComponent<RobotEnemy>();
                        SetTelegraphProgress(enemy, 0f);
                        updateInflate.Invoke(reefRig, null);
                        float scaleAtStart = inflatable.localScale.x;

                        SetTelegraphProgress(enemy, 1f);
                        updateInflate.Invoke(reefRig, null);
                        float scaleAtEnd = inflatable.localScale.x;

                        Assert.Greater(scaleAtEnd, scaleAtStart,
                            "the Puffer Mine's body scale at the end of its telegraph must be larger than at the start");
                    }
                }
                finally
                {
                    if (reefGo != null) Object.DestroyImmediate(reefGo);
                }
            }
        }

        private static bool ColourNear(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>Every distinct <see cref="Mesh"/> a directly-built (not spawner-driven — this only
        /// needs the pure geometry) body wears, excluding the eye/lure renderer(s) — see the class doc
        /// comment for why those are excluded from the AC1 comparison.</summary>
        private static HashSet<Mesh> BodyMeshes(EnemyKind kind, string skin)
        {
            var root = new GameObject("MV746 Probe Root").transform;
            try
            {
                var m = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Standard"));
                var palette = new RobotPalette(m, m, m, m);
                RobotBodies.Body body = RobotBodies.Build(kind, root, palette, skin);

                var eyeSet = new HashSet<MeshRenderer>(body.Eyes ?? System.Array.Empty<MeshRenderer>());
                var meshes = new HashSet<Mesh>();
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var renderer = mf.GetComponent<MeshRenderer>();
                    if (renderer != null && eyeSet.Contains(renderer)) continue;
                    if (mf.sharedMesh != null) meshes.Add(mf.sharedMesh);
                }
                return meshes;
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        /// <summary>Drives the real, private <c>EnemySpawner.CreateInstance(in EnemyArchetype)</c> via
        /// reflection, then forces <see cref="RobotRig.EnsureBuilt"/> — the same technique
        /// <c>MV535RobotBodyOrderingTests.BuildAndSignature</c> uses, for the same reason: Awake/OnEnable
        /// aren't reliably invoked for AddComponent outside Play mode.</summary>
        private static RobotRig BuildViaSpawner(EnemyArchetype archetype, out GameObject enemyGo)
        {
            var spawnerGo = new GameObject("MV746 test spawner");
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();
                MethodInfo createInstance = typeof(EnemySpawner).GetMethod(
                    "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(createInstance, "EnemySpawner.CreateInstance went missing");

                LogAssert.ignoreFailingMessages = true;
                RobotEnemy e;
                try { e = (RobotEnemy)createInstance.Invoke(spawner, new object[] { archetype }); }
                finally { LogAssert.ignoreFailingMessages = false; }

                enemyGo = e.gameObject;
                var rig = e.GetComponent<RobotRig>();
                Assert.IsNotNull(rig, $"CreateInstance did not attach a RobotRig for {archetype.Kind}");

                LogAssert.ignoreFailingMessages = true;
                try
                {
                    typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(rig, null);
                }
                finally { LogAssert.ignoreFailingMessages = false; }

                Assert.IsTrue(rig.Built, $"RobotRig never finished building for {archetype.Kind}");

                // CreateInstance parents every spawned body under the spawner's own "Robots" metre-space
                // container (EnemySpawner.Bodies()) — detach before the spawner is torn down below, or
                // DestroyImmediate(spawnerGo) cascades and destroys the very GameObject this method hands
                // back to its caller.
                enemyGo.transform.SetParent(null, worldPositionStays: false);
                return rig;
            }
            finally
            {
                Object.DestroyImmediate(spawnerGo);
            }
        }

        /// <summary>Forces <see cref="RobotEnemy.TelegraphProgress"/> to 0 or 1 without driving a real
        /// wind-up over time — sets the two private fields that getter reads directly.</summary>
        private static void SetTelegraphProgress(RobotEnemy enemy, float progress)
        {
            typeof(RobotEnemy).GetProperty("Current", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(enemy, RobotEnemy.State.Telegraph);
            FieldInfo timerField = typeof(RobotEnemy).GetField("_stateTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo telegraphTimeField = typeof(RobotEnemy).GetField("telegraphTime", BindingFlags.NonPublic | BindingFlags.Instance);
            float telegraphTime = (float)telegraphTimeField.GetValue(enemy);
            timerField.SetValue(enemy, progress * telegraphTime);
        }
    }
}
