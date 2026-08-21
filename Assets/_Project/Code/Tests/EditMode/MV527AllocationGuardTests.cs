using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-527 — five VFX directors ran an unthrottled <c>FindObjectsByType</c>/<c>FindFirstObjectByType</c>
    /// sweep of the whole scene every frame (two of them over every <c>MeshRenderer</c>, including
    /// inactive ones), reported as the main source of intermittent GC hitching on iOS TestFlight. A
    /// second, related allocation: <c>RobotEnemy.IsOnScreen</c> and
    /// <c>AreaAccumulationDirector.IsOnScreen</c> both called the allocating
    /// <c>GeometryUtility.CalculateFrustumPlanes(Camera)</c> overload, which returns a fresh
    /// <c>Plane[6]</c> every call.
    ///
    /// AC1 (below, source-shape guard — explicitly sanctioned by the ticket as "not a behavioural test")
    /// and AC3 (the two IsOnScreen tests) are this ticket's tests. AC2 ("A PlayMode allocation test: run
    /// a populated arena for N frames...") is NOT authored here — CC_AUTONOMY.md's PlayMode prohibition
    /// is absolute ("NEVER author a PlayMode test... PlayMode is CI's problem, not yours", after three
    /// prior stalls and one 4h20m CI hang) and a reflection-driven `MethodInfo.Invoke` wrapped in
    /// `Is.Not.AllocatingGCMemory()` is not a trustworthy substitute — `Invoke` itself has its own,
    /// separate allocation profile that would make such a test measure reflection overhead, not the
    /// fix. The gap is called out explicitly in the fix comment per CC_AUTONOMY.md's own instruction for
    /// exactly this situation.
    /// </summary>
    public sealed class MV527AllocationGuardTests
    {
        // ---------------------------------------------------------------------------- AC1

        private static readonly Regex MethodSig = new Regex(
            @"(?<sig>(private|protected|public|internal)?\s*(override\s+)?void\s+(Update|LateUpdate|FixedUpdate)\s*\(\s*\)\s*)(?<body>=>|\{)",
            RegexOptions.Compiled);

        /// <summary>
        /// Named, deliberate exceptions — not a loophole, a tracked debt. Every entry must carry a
        /// reason a reviewer can check without re-deriving it. This check is a text scan, not semantic
        /// analysis, so it cannot tell "calls it every frame" apart from "calls it once, lazily, behind
        /// a null-check cache" — the two GameFeel/AmbienceVfx entries below are the latter, not a
        /// violation, just outside what a source-shape test can prove on its own.
        ///
        /// GroundAnchorVfx.cs (MV-527's entry here): its per-frame <c>FindObjectsByType&lt;CharacterController&gt;</c>
        /// scan was evaluated for MV-527 and reverted after <c>GroundAnchorPlayTests.cs</c> (6 PlayMode
        /// tests) turned out to pin a load-bearing, documented contract — ANY actor with a
        /// CharacterController + IDamageable gets anchored, with zero per-type wiring, proven against a
        /// synthetic FakeActor type that is neither RobotEnemy, PlayerHealth nor BigBermudaBoss. MV-532
        /// converted it off the per-frame scan — to a reused-buffer <c>Physics.OverlapSphereNonAlloc</c>
        /// query rather than a registry, because a registry needs a component added at every actor's
        /// construction site, which is exactly the per-type wiring FakeActor proves must not be
        /// required. It no longer calls FindObjectsByType/FindFirstObjectByType in a per-frame path, so
        /// it no longer needs this exemption; kept off the list.
        ///
        /// GameFeel.cs / AmbienceVfx.cs: pre-existing (not touched by MV-527), and already the pattern
        /// this ticket asks for — <c>if (_field == null) _field = FindFirstObjectByType&lt;T&gt;();</c>,
        /// a one-time cached singleton lookup, not a per-frame scan. Allowlisted because a text scan
        /// can't see the guard; not a regression to fix.
        /// </summary>
        private static readonly string[] Allowlist = { "GameFeel.cs", "AmbienceVfx.cs" };

        [Test]
        public void NoUpdateMethodInRuntime_CallsFindObjectsByType_WithoutCaching()
        {
            string runtimeRoot = Path.Combine(Application.dataPath, "_Project", "Code", "Runtime");
            Assert.IsTrue(Directory.Exists(runtimeRoot), $"Runtime root not found: {runtimeRoot}");

            var offenders = new List<string>();

            foreach (string path in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(path);
                if (Array.IndexOf(Allowlist, fileName) >= 0) continue;

                // Strip comments first — several of this ticket's own fix-site comments explain what
                // USED to be a per-frame FindObjectsByType call, in prose, right inside the very Update
                // method that no longer makes it. A text scan that didn't strip comments would flag its
                // own explanation as the bug.
                string text = StripComments(File.ReadAllText(path));
                foreach (Match m in MethodSig.Matches(text))
                {
                    string body = m.Groups["body"].Value == "=>"
                        ? ExpressionBody(text, m.Index + m.Length - 1)
                        : BlockBody(text, text.IndexOf('{', m.Index + m.Length - 1));

                    if (body.Contains("FindObjectsByType") || body.Contains("FindFirstObjectByType"))
                    {
                        int line = CountLines(text, m.Index);
                        offenders.Add($"{fileName}:{line} — {m.Groups["sig"].Value.Trim()}");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "Update/LateUpdate/FixedUpdate must not call FindObjectsByType/FindFirstObjectByType " +
                "every frame — that's the MV-527 regression (source-shape check, not a behavioural one). " +
                "Offenders:\n" + string.Join("\n", offenders));
        }

        private static readonly Regex CommentRegex = new Regex(
            @"//[^\n]*|/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>Blanks out comments (replacing with spaces, never removing characters) so line
        /// numbers and match offsets in the caller still line up with the original file.
        ///
        /// MV-533: the original LINQ pipeline (<c>m.Value.Select(...).ToArray()</c>) allocated a
        /// substring, an iterator, and a resizable buffer per match. Across ~17,000 comment matches
        /// in this tree that was ~32s on plain .NET and ~99-420s under Mono/Editor batchmode —
        /// slow enough to blow the CI test budget outright. This does the identical byte-for-byte
        /// transform (comment text blanked, newlines preserved) with a single pre-sized array and a
        /// plain loop reading straight from the source text — no LINQ, no extra substring.</summary>
        private static string StripComments(string text) =>
            CommentRegex.Replace(text, m =>
            {
                int start = m.Index;
                int length = m.Length;
                var blanked = new char[length];
                for (int i = 0; i < length; i++)
                {
                    char c = text[start + i];
                    blanked[i] = c == '\n' ? '\n' : ' ';
                }
                return new string(blanked);
            });

        private static int CountLines(string text, int uptoIndex)
        {
            int line = 1;
            for (int i = 0; i < uptoIndex && i < text.Length; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        private static string ExpressionBody(string text, int arrowIndex)
        {
            int end = text.IndexOf(';', arrowIndex);
            return end < 0 ? text.Substring(arrowIndex) : text.Substring(arrowIndex, end - arrowIndex);
        }

        private static string BlockBody(string text, int openBraceIndex)
        {
            if (openBraceIndex < 0) return string.Empty;
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

        // ---------------------------------------------------------------------------- AC3

        /// <summary>
        /// Proxy for "allocates nothing per call" that doesn't route through reflection's own allocation
        /// profile (see class doc comment for why a GC-delta assertion around <c>MethodInfo.Invoke</c>
        /// isn't trustworthy here): read the private static frustum-planes buffer by reflection before
        /// and after two calls and assert it's the SAME array both times. A fresh allocation — the bug —
        /// would show up as a different array reference; the fix reuses one buffer forever.
        /// </summary>
        [Test]
        public void RobotEnemy_IsOnScreen_ReusesOneFrustumPlanesBuffer_NeverAllocatingAFreshOne()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.AddComponent<CharacterController>();
            var enemy = go.AddComponent<RobotEnemy>();

            var camGo = new GameObject("Test Main Camera") { tag = "MainCamera" };
            camGo.AddComponent<Camera>();

            try
            {
                MethodInfo isOnScreen = typeof(RobotEnemy).GetMethod("IsOnScreen",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(isOnScreen, "RobotEnemy.IsOnScreen went missing");

                FieldInfo bufferField = typeof(RobotEnemy).GetField("s_frustumPlanes",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(bufferField,
                    "RobotEnemy.s_frustumPlanes went missing — the reusable buffer this test guards (MV-527)");

                isOnScreen.Invoke(enemy, null);
                var first = (Plane[])bufferField.GetValue(null);
                Assert.IsNotNull(first, "the frustum-planes buffer was never populated");

                isOnScreen.Invoke(enemy, null);
                var second = (Plane[])bufferField.GetValue(null);

                Assert.AreSame(first, second,
                    "IsOnScreen is handing back a NEW Plane[] array on a repeat call — that's the " +
                    "per-call GeometryUtility.CalculateFrustumPlanes(Camera) allocation MV-527 removed, " +
                    "and it runs once per DORMANT robot per frame.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>Same guard, same reasoning, for the second call site the ticket names
        /// (<c>AreaAccumulationDirector.cs:863-867</c>) — a static method, so no instance to build.</summary>
        [Test]
        public void AreaAccumulationDirector_IsOnScreen_ReusesOneFrustumPlanesBuffer_NeverAllocatingAFreshOne()
        {
            var camGo = new GameObject("Test Main Camera");
            var cam = camGo.AddComponent<Camera>();

            try
            {
                Type directorType = typeof(AreaAccumulationDirector);
                MethodInfo isOnScreen = directorType.GetMethod("IsOnScreen",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(isOnScreen, "AreaAccumulationDirector.IsOnScreen went missing");

                FieldInfo bufferField = directorType.GetField("s_frustumPlanes",
                    BindingFlags.NonPublic | BindingFlags.Static);
                Assert.IsNotNull(bufferField,
                    "AreaAccumulationDirector.s_frustumPlanes went missing — the reusable buffer this test guards (MV-527)");

                isOnScreen.Invoke(null, new object[] { cam, Vector3.zero });
                var first = (Plane[])bufferField.GetValue(null);
                Assert.IsNotNull(first, "the frustum-planes buffer was never populated");

                isOnScreen.Invoke(null, new object[] { cam, Vector3.one });
                var second = (Plane[])bufferField.GetValue(null);

                Assert.AreSame(first, second,
                    "IsOnScreen is handing back a NEW Plane[] array on a repeat call — that's the " +
                    "per-attempt GeometryUtility.CalculateFrustumPlanes(Camera) allocation MV-527 " +
                    "removed, and it can run up to MaxPlacementAttempts times per spawn placement.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(camGo);
            }
        }
    }
}
