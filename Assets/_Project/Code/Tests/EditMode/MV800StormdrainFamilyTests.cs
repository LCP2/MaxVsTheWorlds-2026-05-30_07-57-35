using System.Collections.Generic;
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
    /// MV-800 — before this ticket, only the Rusher (Scrap Rat) ever carried World 2's "stormdrain"
    /// skin tag, so the other seven kinds fell through to the base roster table and four of them
    /// rendered near-neutral (Brute, Lurker, Turret, and the Rusher's own flat rust-brown). The one
    /// test this ticket is allowed (MV-465 Rule 1) proves the whole family reads as bright, saturated
    /// and mutually distinct once every kind carries the tag and the per-kind stormdrain table exists.
    /// </summary>
    public sealed class MV800StormdrainFamilyTests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly EnemyKind[] World2Kinds =
        {
            EnemyKind.Rusher, EnemyKind.Bruiser, EnemyKind.Heavy, EnemyKind.Brute,
            EnemyKind.Lurker, EnemyKind.Sludger, EnemyKind.Charger, EnemyKind.Turret,
        };

        [Test]
        public void AllEightStormdrainKinds_AreBrightSaturatedAndMutuallyDistinct()
        {
            var colours = new Dictionary<EnemyKind, Color>();
            var spawned = new List<GameObject>();

            try
            {
                foreach (var kind in World2Kinds)
                {
                    var e = BuildDressedStormdrainRobot(kind);
                    spawned.Add(e.gameObject);
                    colours[kind] = ReadRenderedBodyColor(e, CharacterSkin.RoleFor(kind));
                }

                foreach (var kind in World2Kinds)
                {
                    Color c = colours[kind];
                    float peak = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    float chroma = peak <= 1e-4f ? 0f : (peak - min) / peak;

                    Assert.That(peak, Is.GreaterThanOrEqualTo(0.40f),
                        $"{kind}'s stormdrain body renders at peak channel {peak:0.000} — that's still " +
                        "near-neutral, the exact drab-robot defect this ticket exists to fix.");
                    Assert.That(peak, Is.LessThanOrEqualTo(SunlitAlbedo.Ceiling),
                        $"{kind}'s stormdrain body renders at peak channel {peak:0.000}, over the " +
                        $"sunlit ceiling ({SunlitAlbedo.Ceiling}) — it will clip to white under the key.");
                    Assert.That(chroma, Is.GreaterThanOrEqualTo(0.45f),
                        $"{kind}'s stormdrain body has chroma {chroma:0.00} — that reads as grey, not " +
                        "as one of the family's hues.");
                }

                for (int i = 0; i < World2Kinds.Length; i++)
                {
                    for (int j = i + 1; j < World2Kinds.Length; j++)
                    {
                        EnemyKind a = World2Kinds[i], b = World2Kinds[j];
                        float dist = Distance(colours[a], colours[b]);
                        Assert.That(dist, Is.GreaterThanOrEqualTo(0.25f),
                            $"{a} and {b} render within {dist:0.000} of each other in RGB — too close " +
                            "to tell apart as separate kinds at a glance.");
                    }
                }
            }
            finally
            {
                foreach (var go in spawned) DestroyIgnoringEditModeDestroyWarnings(go);
            }
        }

        /// <summary>Same Euclidean RGB distance <see cref="ActorReadabilityTests"/>'s own
        /// <c>Distance</c> helper and the project's 0.25 mutual-distinguishability convention already
        /// use — duplicated here rather than exposed across files, since it's one line of maths.</summary>
        private static float Distance(Color a, Color b) =>
            Mathf.Sqrt((a.r - b.r) * (a.r - b.r) + (a.g - b.g) * (a.g - b.g) + (a.b - b.b) * (a.b - b.b));

        /// <summary>Builds <paramref name="kind"/> exactly the way a World 2 spawn does: a greybox
        /// stand-in, <see cref="RobotEnemy.Apply"/> with the "stormdrain" skin tag every kind now
        /// carries (<c>world2_config.json</c>), a <see cref="CharacterSkin"/> bound and applied against
        /// that stand-in (the AC's own instruction), and then <see cref="RobotRig"/> — which is what
        /// destroys the stand-in and builds the parts actually on screen (MV-800's own root cause: the
        /// stand-in's skin never reached the screen at all).</summary>
        private static RobotEnemy BuildDressedStormdrainRobot(EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind).WithOverride(new WorldEnemyOverride
            {
                skin = "stormdrain",
            });

            var go = GameObject.CreatePrimitive(
                archetype.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1f;
            cc.radius = 0.4f;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(archetype);
            go.SetActive(true);

            var skin = go.AddComponent<CharacterSkin>();
            skin.Bind(CharacterSkin.RoleFor(kind));

            var rig = go.AddComponent<RobotRig>();
            InvokeEnsureBuilt(rig);
            return e;
        }

        /// <summary>The colour the body renderer actually draws — a <see cref="MaterialPropertyBlock"/>
        /// override if one is set on the part, otherwise the material's own <c>_BaseColor</c>. Never the
        /// table constant: this is what makes the assertion catch a resolved-but-unrouted skin tag
        /// (MV-800's own root cause), not just a wrong table entry.</summary>
        private static Color ReadRenderedBodyColor(RobotEnemy e, CharacterRole role)
        {
            string expectedBodyName = $"Robot_{role}_Body";

            foreach (var r in e.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.name != expectedBodyName) continue;

                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                if (!mpb.isEmpty) return mpb.GetColor(BaseColorId);
                return r.sharedMaterial.GetColor(BaseColorId);
            }

            Assert.Fail($"no built part of the {e.Kind} rig wears '{expectedBodyName}' — it never got " +
                        "a real body, or the body material naming has drifted from what this test expects.");
            return default;
        }

        /// <summary>Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode — drive
        /// the private build step directly, the same workaround <c>RobotSkinSpawnPathTests</c> and
        /// <c>RobotSkinDiagnosticsTests</c> already use. It strips a Collider off each part via the
        /// play-mode-correct <c>Destroy()</c>, which is a logged error in Edit mode (not this ticket's
        /// to fix) — ignore just that.</summary>
        private static void InvokeEnsureBuilt(RobotRig rig)
        {
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static void DestroyIgnoringEditModeDestroyWarnings(Object o)
        {
            LogAssert.ignoreFailingMessages = true;
            try { Object.DestroyImmediate(o); }
            finally { LogAssert.ignoreFailingMessages = false; }
        }
    }
}
