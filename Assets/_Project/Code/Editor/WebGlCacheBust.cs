using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MaxWorlds.Editor
{
    /// <summary>
    /// Makes a freshly deployed WebGL build impossible to miss (YT-62).
    ///
    /// The problem: Unity's WebGL loader fetches Build/*.data, *.wasm, *.framework.js and
    /// *.loader.js by fixed filenames that never change between builds. A browser that has loaded
    /// the play link before can therefore serve the WHOLE GAME from its cache and show a
    /// weeks-old build, with no error and nothing to indicate it. Every "the fix isn't there"
    /// report becomes unfalsifiable, and we have already burned a review cycle on exactly that.
    ///
    /// The fix is the oldest one in the book: put a unique version on the URL. Each build rewrites
    /// index.html so every Build/* URL carries "?v=&lt;commit&gt;", which the cache treats as a
    /// different resource. A new deploy simply cannot be served from an old cache.
    ///
    /// Defensive by design: if the template ever changes shape and nothing matches, it logs loudly
    /// and leaves the file alone rather than corrupting the page.
    /// </summary>
    public sealed class WebGlCacheBust : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;   // after everything else has written its files

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;

            string root = report.summary.outputPath;
            string indexPath = Path.Combine(root, "index.html");
            if (!File.Exists(indexPath))
            {
                Debug.LogWarning($"[WebGlCacheBust] no index.html at {indexPath} — skipped.");
                return;
            }

            string version = Application.version;
            string html = File.ReadAllText(indexPath);
            string patched = Patch(html, version);

            if (patched == html)
            {
                Debug.LogError("[WebGlCacheBust] nothing was rewritten — the WebGL template must have " +
                               "changed shape. The build will still work, but browsers can serve a " +
                               "STALE build from cache. Fix the pattern below.");
                return;
            }

            File.WriteAllText(indexPath, patched);
            Debug.Log($"[WebGlCacheBust] index.html now requests Build/* with ?v={version}");
        }

        /// <summary>
        /// Append <c>?v=version</c> to every <c>buildUrl + "/…"</c> the loader resolves. Public and
        /// pure so it can be unit-tested against a sample of the real template — a build-time
        /// rewrite that silently no-ops is worse than no rewrite at all.
        /// </summary>
        public static string Patch(string html, string version)
        {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(version)) return html;

            // Matches: buildUrl + "/Something.ext"   (loader, data, framework, wasm — all of them)
            // Skips anything that already carries a query, so re-running is safe.
            return Regex.Replace(
                html,
                "(buildUrl\\s*\\+\\s*\"/[^\"?]+)\"",
                m => $"{m.Groups[1].Value}?v={version}\"");
        }
    }
}
