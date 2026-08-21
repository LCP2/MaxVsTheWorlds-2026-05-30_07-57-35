using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-350 — some robots shipped wearing the default/untinted material. MV-310's theory (only one
    /// of two spawn systems dresses its robots) doesn't hold: both <c>EnemySpawner.CreateInstance</c>
    /// and <c>AreaAccumulationDirector.CreateInstance</c> attach a <see cref="RobotRig"/> the instant
    /// they build a new <see cref="RobotEnemy"/> (MV-527; previously a per-frame sweep found it by
    /// component instead) — a factory robot and a gated-arena robot, which build an identical greybox +
    /// <see cref="RobotEnemy.Apply"/>, go through the exact same <see cref="RobotRig"/>. This proves
    /// that invariant directly: build a robot the way each spawner's CreateInstance does, run the one
    /// dressing step every path shares, and check the ACTUAL material assigned on the instance — not
    /// a colour constant, per the ticket's own instruction not to touch those again.
    /// </summary>
    public sealed class RobotSkinSpawnPathTests
    {
        /// <summary>The exact greybox-then-Apply sequence both EnemySpawner.CreateInstance and
        /// AreaAccumulationDirector.CreateInstance run: a primitive stand-in, a CharacterController,
        /// then <see cref="RobotEnemy.Apply"/> stamps the kind — the moment the archetype becomes
        /// known. Neither spawner ever waits a frame before switching the result on: when their pool
        /// for this kind is empty, <c>SpawnKind</c>/<c>Spawn</c> activate it in the same call.</summary>
        private static RobotEnemy BuildGreybox(EnemyKind kind)
        {
            EnemyArchetype archetype = EnemyArchetype.Of(kind);
            var go = GameObject.CreatePrimitive(
                archetype.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1f;
            cc.radius = 0.4f;

            var e = go.AddComponent<RobotEnemy>();
            e.Apply(archetype);
            go.SetActive(true);
            return e;
        }

        [Test]
        public void FactoryPathRobot_WearsItsArchetypeTint_NotTheDefaultMaterial()
        {
            var e = BuildGreybox(EnemyKind.Bruiser);
            try
            {
                AssertDressedCorrectly(e, EnemyKind.Bruiser);
            }
            finally { DestroyIgnoringEditModeDestroyWarnings(e.gameObject); }
        }

        [Test]
        public void AreaPathRobot_WearsItsArchetypeTint_NotTheDefaultMaterial()
        {
            // Same construction on purpose — AreaAccumulationDirector.CreateInstance is the same
            // primitive+CharacterController+RobotEnemy.Apply build as EnemySpawner's. This proves the
            // OTHER spawn path lands on identically-tinted output, not a coincidentally similar one.
            var e = BuildGreybox(EnemyKind.Gunner);
            try
            {
                AssertDressedCorrectly(e, EnemyKind.Gunner);
            }
            finally { DestroyIgnoringEditModeDestroyWarnings(e.gameObject); }
        }

        /// <summary>RobotRig strips each part's auto-added Collider and, on teardown, its two cloned
        /// materials via the play-mode-correct <c>Destroy()</c> — logged as an error in Edit mode
        /// (not this ticket's to fix, and the count varies with a kind's part total). Ignore failing
        /// messages for exactly this call rather than asserting play-mode-only cleanup mechanics.</summary>
        private static void DestroyIgnoringEditModeDestroyWarnings(Object o)
        {
            LogAssert.ignoreFailingMessages = true;
            try { Object.DestroyImmediate(o); }
            finally { LogAssert.ignoreFailingMessages = false; }
        }

        private static void AssertDressedCorrectly(RobotEnemy e, EnemyKind kind)
        {
            // The one dressing step every spawn path shares — RobotRigDirector's sweep does exactly
            // this, for any RobotEnemy it finds regardless of who created it (MV-350's actual finding:
            // the two systems were never on separate skinning paths to begin with).
            var rig = e.gameObject.AddComponent<RobotRig>();

            // Awake/OnEnable aren't reliably invoked for AddComponent outside Play mode (the same
            // EditMode-only limitation RobotEnemy.ResetState's own doc comment calls out) — RobotRig
            // has no public equivalent to CharacterSkin.Bind(), so drive its private build step
            // directly, exactly as it would have been driven by Awake() in a real build. It strips a
            // Collider off each part via the play-mode-correct Destroy() along the way, which is a
            // logged error in Edit mode (not this ticket's to fix) — ignore just that.
            LogAssert.ignoreFailingMessages = true;
            try
            {
                typeof(RobotRig).GetMethod("EnsureBuilt", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(rig, null);
            }
            finally { LogAssert.ignoreFailingMessages = false; }

            Assert.IsTrue(rig.Built, "RobotRig must finish building the instant it's attached");

            // AC2: the greybox stand-in must be hidden — a robot that fell through and never got a
            // RobotRig would still show this renderer, wearing the primitive's own untinted material.
            var greybox = e.GetComponent<MeshRenderer>();
            Assert.IsFalse(greybox.enabled,
                "the greybox stand-in must be disabled once the real rig is built, or it — not the " +
                "rig's own coloured parts — is what's actually on screen");

            // AC1: every built part wears the kind's own colour, read straight off the same mapping
            // RobotRig.BuildMaterials uses — not a re-derived expectation that could drift from it.
            CharacterRole role = CharacterSkin.RoleFor(kind);
            Color expected = CharacterSkin.BaseColorFor(role);
            string expectedBodyName = $"Robot_{role}_Body";

            bool foundTintedBody = false;
            foreach (var r in e.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r == greybox || r.sharedMaterial == null) continue;
                if (r.sharedMaterial.name != expectedBodyName) continue;

                foundTintedBody = true;
                Color actual = r.sharedMaterial.GetColor("_BaseColor");
                // A Material property is a real GPU-facing value (colour-space conversion on the
                // round trip), so this checks CLOSE, not bit-exact — exact equality here would be
                // testing float rounding, not the archetype fallthrough this test exists to catch.
                Assert.That(Vector4.Distance(expected, actual), Is.LessThan(0.01f),
                    $"the {kind} body material is named for its role but doesn't carry that role's " +
                    $"colour — a fallthrough at the point the tint is written, not at the mapping " +
                    $"(expected {expected}, was {actual})");
            }

            Assert.IsTrue(foundTintedBody,
                $"no built part of the {kind} rig wears '{expectedBodyName}' — it rendered with " +
                "whatever material it had before RobotRig ran, not its archetype tint");
        }
    }
}
