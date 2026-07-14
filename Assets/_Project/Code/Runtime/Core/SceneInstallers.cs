using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MaxWorlds.Core
{
    /// <summary>
    /// Rebuilds the code-driven scene every time a scene loads — not just the first one (YT-91).
    ///
    /// This project assembles itself in code: the materials, the 217 kit props, the lighting, the
    /// VFX, the factory's moving parts. Every one of those systems installs itself with
    /// <c>[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]</c>, which reads like "run this after a
    /// scene loads" and is not what it does. Unity fires it ONCE PER PROCESS.
    ///
    /// So the yard was built exactly once, and <c>ResultScreen.Replay</c> — <c>SceneManager.LoadScene</c>
    /// — tore it down and never rebuilt it. The second run of the game was raw greybox wearing the
    /// editor's built-in Default-Material, which URP cannot render: the whole world came back MAGENTA,
    /// propless and unlit, and Max came back grey. Every visual ticket in the project was unverifiable
    /// on any run after the first, which is exactly the run a reviewer reaches by pressing Replay.
    ///
    /// The installers were always safe to run again — each one opens by looking for itself and
    /// returning if it is already there. Nothing was ever calling them twice.
    ///
    /// WHY THIS FINDS THEM BY REFLECTION rather than holding a list of the seventeen: a list is a
    /// second definition of "what the game is made of", and the first person to add an eighteenth
    /// system would not know to add it. They would get a silent, invisible regression on Replay only —
    /// the precise bug this class exists to kill. The attribute IS the registration; this reads the
    /// same registration Unity does, so a new system is covered by writing it. The scan runs once and
    /// is cached; a scene load costs seventeen guarded no-ops.
    /// </summary>
    public static class SceneInstallers
    {
        private static MethodInfo[] s_installers;
        private static bool s_hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            if (s_hooked) return;
            s_hooked = true;

            // Unity has already run all of these for the scene we booted into. From here on, they are
            // ours to run.
            SceneManager.sceneLoaded += OnSceneLoaded;

            Debug.Log($"[SceneInstallers] {Installers.Length} self-installing systems; " +
                      "they rebuild on every scene load.");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Additive loads leave the existing world standing, and its systems with it. Only a
            // single-mode load destroys them, and only that needs them back.
            if (mode != LoadSceneMode.Single) return;
            InstallAll();
        }

        /// <summary>Every AfterSceneLoad installer in the game, found once and remembered.</summary>
        public static MethodInfo[] Installers => s_installers ??= Discover();

        /// <summary>
        /// Run every installer. Safe to call at any time and any number of times: each installer's
        /// first act is to look for itself and return if it already exists.
        /// </summary>
        public static int InstallAll()
        {
            int ran = 0;
            foreach (var m in Installers)
            {
                try
                {
                    m.Invoke(null, null);
                    ran++;
                }
                catch (Exception e)
                {
                    // One system failing to build must not take the other sixteen down with it — a
                    // yard with no ambient motes is a bad yard; a yard with no ground is no yard.
                    Debug.LogError($"[SceneInstallers] {m.DeclaringType?.Name}.{m.Name} threw: " +
                                   $"{e.InnerException ?? e}");
                }
            }
            return ran;
        }

        /// <summary>
        /// The game's own assemblies, scanned for the same attribute Unity boots from.
        /// </summary>
        public static MethodInfo[] Discover()
        {
            var found = new List<MethodInfo>(24);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = asm.GetName().Name;
                if (!name.StartsWith("MaxWorlds", StringComparison.Ordinal)) continue;
                if (name.Contains("Tests") || name.Contains("Editor")) continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = Array.FindAll(e.Types, t => t != null);
                }

                foreach (var type in types)
                {
                    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public |
                                               BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                    foreach (var m in type.GetMethods(Flags))
                    {
                        // Not ourselves: re-running Hook would subscribe to sceneLoaded a second time.
                        if (m.DeclaringType == typeof(SceneInstallers)) continue;

                        var attr = m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                        if (attr == null) continue;

                        // Only AfterSceneLoad. The earlier load types (SubsystemRegistration and the
                        // rest) are about the PROCESS coming up, not the world being built, and
                        // re-running them on a scene load would be wrong.
                        if (attr.loadType != RuntimeInitializeLoadType.AfterSceneLoad) continue;

                        if (m.GetParameters().Length != 0) continue;

                        found.Add(m);
                    }
                }
            }

            return found.ToArray();
        }
    }
}
