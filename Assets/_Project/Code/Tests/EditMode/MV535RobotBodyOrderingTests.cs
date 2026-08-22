using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-535 — MV-527 (94d31da) moved <see cref="RobotRig"/>'s attach into
    /// <c>EnemySpawner.CreateInstance</c> and <c>AreaAccumulationDirector.CreateInstance</c>, but
    /// attached it BEFORE <see cref="RobotEnemy.Apply"/> stamped the real <see cref="RobotEnemy.Kind"/>.
    /// <see cref="RobotRig"/>'s private <c>Awake()</c> reads <c>_enemy.Kind</c> synchronously the
    /// instant <c>AddComponent</c> runs — in a real (Play Mode / TestFlight) build that read happens
    /// immediately, so every robot's body baked as the default Kind (Rusher) regardless of its actual
    /// archetype, while <c>Kind</c> itself (read live by behaviour and the nameplate) stayed correct.
    ///
    /// This is exactly the class of bug this project's own EditMode harness cannot observe end to end.
    /// Confirmed empirically for this exact scenario (capsule + CharacterController + RobotEnemy,
    /// mirroring <c>CreateInstance</c>'s own construction) that <c>AddComponent&lt;RobotRig&gt;()</c>
    /// does NOT synchronously invoke <c>Awake()</c> within a single EditMode <c>[Test]</c> method — the
    /// same empirical finding <c>AreaGateTests</c>' MV-386 note and every other "Awake/OnEnable aren't
    /// reliably invoked for AddComponent outside Play mode" comment in this tree already document. That
    /// means a test which only inspects state AFTER the real, private <c>CreateInstance()</c> returns
    /// can never see this bug behaviourally, no matter how faithfully it drives the real method: by the
    /// time <c>CreateInstance()</c> returns, <c>Apply()</c> has already run in EVERY version of that
    /// method, buggy or fixed, so <c>RobotEnemy.Kind</c> is always correct by the time any post-hoc
    /// reflection call could look at it — the geometry, not the Kind property, is what freezes at the
    /// wrong moment, and nothing after the method returns can un-freeze it. PlayMode would prove this
    /// end to end, but authoring one is the one thing CC_AUTONOMY.md forbids this worker outright.
    ///
    /// So this test does both things AC1 asks for, through two different mechanisms, because no single
    /// one can carry both: Part 1 is a source-shape guard on the real files (the same precedented
    /// technique <c>MV527AllocationGuardTests</c>' AC1 uses for an identical "PlayMode would prove it,
    /// EditMode structurally cannot" gap) — THIS is what actually goes red on 94d31da and green after
    /// the fix, and what keeps failing if a future edit reorders these two calls back. Part 2 literally
    /// drives <c>EnemySpawner.CreateInstance</c> via reflection — the real spawner creation path, not a
    /// hand-rolled stand-in — and proves that once Part 1's ordering holds, every kind's RobotRig
    /// builds a geometry signature distinct from a Rusher's, through the real
    /// <see cref="RobotRig.EnsureBuilt"/> / <see cref="RobotBodies.Build"/> machinery, never
    /// <c>RobotBodies.Build</c> called directly — Part 2 alone cannot go red/green across the fix (see
    /// above), so it is not the regression guard, only the behavioural evidence the guard is real.
    /// </summary>
    public sealed class MV535RobotBodyOrderingTests
    {
        [Test]
        public void CreateInstance_AppliesArchetypeBeforeAttachingRobotRig_SoEveryKindBuildsItsOwnBody()
        {
            // ---- Part 1: source-shape guard on the two real spawn sites ----------------------------
            // This is the part that actually goes red on 94d31da and green after the fix, and the part
            // that keeps failing if anyone reorders these two calls back in future — the behavioural
            // half below can't do either, per the class doc comment.
            AssertApplyPrecedesRobotRigAttach(
                Path.Combine("Enemies", "EnemySpawner.cs"), "CreateInstance(in EnemyArchetype a)");
            AssertApplyPrecedesRobotRigAttach(
                Path.Combine("Enemies", "AreaAccumulationDirector.cs"), "CreateInstance(in EnemyArchetype a)");

            // ---- Part 2: behavioural proof that the ordering the guard above just confirmed is what
            // makes the geometry come out right ----------------------------------------------------
            string rusherSignature = BuildAndSignature(EnemyKind.Rusher);
            foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            {
                if (kind == EnemyKind.Rusher) continue;

                string signature = BuildAndSignature(kind);
                Assert.AreNotEqual(rusherSignature, signature,
                    $"{kind} built the same renderer-count+bounds signature as a Rusher ({signature}) — " +
                    "either every kind is falling back to the default archetype again (MV-535), or " +
                    "RobotBodies hasn't actually given this kind its own body");
            }
        }

        /// <summary>Reads the real source file and asserts <c>.Apply(</c> appears, inside the named
        /// method's body, before <c>AddComponent&lt;RobotRig&gt;</c> — the exact ordering MV-535 fixed.
        /// Comments are blanked first so a fix-site comment explaining the ordering (this ticket left
        /// one right above the real statement) can never be mistaken for the statement itself.</summary>
        private static void AssertApplyPrecedesRobotRigAttach(string relativePath, string methodSignature)
        {
            string path = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime", relativePath);
            Assert.IsTrue(File.Exists(path), $"source file not found: {path}");

            string text = StripComments(File.ReadAllText(path));

            int methodStart = text.IndexOf(methodSignature, StringComparison.Ordinal);
            Assert.GreaterOrEqual(methodStart, 0, $"{methodSignature} not found in {relativePath}");

            int bodyStart = text.IndexOf('{', methodStart);
            Assert.GreaterOrEqual(bodyStart, 0, $"no method body found for {methodSignature} in {relativePath}");
            string body = BlockBody(text, bodyStart);

            int applyIndex = body.IndexOf(".Apply(", StringComparison.Ordinal);
            int addRigIndex = body.IndexOf("AddComponent<RobotRig>", StringComparison.Ordinal);

            Assert.GreaterOrEqual(applyIndex, 0, $".Apply( not found inside {methodSignature} in {relativePath}");
            Assert.GreaterOrEqual(addRigIndex, 0,
                $"AddComponent<RobotRig> not found inside {methodSignature} in {relativePath}");

            Assert.Less(applyIndex, addRigIndex,
                $"{relativePath}'s {methodSignature} attaches RobotRig before calling Apply() — " +
                "RobotRig.Awake() reads Kind synchronously, so every robot would build as the default " +
                "Kind (Rusher) regardless of its real archetype (MV-535 / the 94d31da regression)");
        }

        private static readonly Regex CommentRegex = new Regex(
            @"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        private static string StripComments(string text) =>
            CommentRegex.Replace(text, m => new string(' ', m.Length));

        private static string BlockBody(string text, int openBraceIndex)
        {
            int depth = 0;
            for (int i = openBraceIndex; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return text.Substring(openBraceIndex, i - openBraceIndex + 1);
                }
            }
            return text.Substring(openBraceIndex);
        }

        /// <summary>Drives the REAL, private <c>EnemySpawner.CreateInstance(in EnemyArchetype)</c> via
        /// reflection — the actual spawner creation path, not a hand-rolled stand-in — then forces the
        /// one build step every spawn path shares through <see cref="RobotRig.EnsureBuilt"/> (Awake/
        /// OnEnable aren't reliably invoked for AddComponent outside Play mode — see class doc comment),
        /// exactly as <c>RobotSkinSpawnPathTests</c> already does for this same rig. By the time
        /// <c>CreateInstance</c> returns, <c>Apply()</c> has already run regardless of source order (see
        /// class doc comment for why that makes this half unable to go red on its own) — Part 1's
        /// source-shape guard is what actually proves the ordering; this proves the geometry it produces
        /// once that ordering holds. Returns a signature (part count + combined bounds size) read off
        /// the BUILT GameObject, never off <c>RobotBodies.Build</c> called directly.</summary>
        private static string BuildAndSignature(EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind);
            var spawnerGo = new GameObject("MV-535 test spawner");
            RobotEnemy e = null;
            try
            {
                var spawner = spawnerGo.AddComponent<EnemySpawner>();

                MethodInfo createInstance = typeof(EnemySpawner).GetMethod(
                    "CreateInstance", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(createInstance, "EnemySpawner.CreateInstance went missing");

                LogAssert.ignoreFailingMessages = true;
                try
                {
                    e = (RobotEnemy)createInstance.Invoke(spawner, new object[] { archetype });
                }
                finally { LogAssert.ignoreFailingMessages = false; }

                var rig = e.GetComponent<RobotRig>();
                Assert.IsNotNull(rig, $"CreateInstance did not attach a RobotRig for {kind}");

                LogAssert.ignoreFailingMessages = true;
                try
                {
                    typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                        .Invoke(rig, null);
                }
                finally { LogAssert.ignoreFailingMessages = false; }

                Assert.IsTrue(rig.Built, $"RobotRig never finished building for {kind}");

                var greybox = e.GetComponent<MeshRenderer>();
                var renderers = e.GetComponentsInChildren<MeshRenderer>(true);

                int count = 0;
                Bounds bounds = default;
                bool started = false;
                foreach (var r in renderers)
                {
                    if (r == greybox) continue;
                    count++;
                    if (!started) { bounds = r.bounds; started = true; }
                    else bounds.Encapsulate(r.bounds);
                }

                return $"count={count} size={bounds.size:F2}";
            }
            finally
            {
                LogAssert.ignoreFailingMessages = true;
                if (e != null) UnityEngine.Object.DestroyImmediate(e.gameObject);
                UnityEngine.Object.DestroyImmediate(spawnerGo);
                LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
