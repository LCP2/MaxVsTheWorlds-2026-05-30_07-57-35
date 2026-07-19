using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// YT-101 — the spring director's lifecycle and its caps.
    ///
    /// PlayMode, because Awake and OnEnable never run in edit mode: an EditMode version of "does it
    /// self-install and subscribe" would pass without any of it having happened. The motion maths is
    /// covered in SpringGutsTests; what is proven here is that the thing wires itself up with no
    /// scene edits, that a kill actually produces coils, and — the part the ticket cares most about —
    /// that a crowd dying at once cannot make an unbounded number of them.
    ///
    /// Each test gets a FRESH director. The director installs itself once per domain load and every
    /// spring hangs off it, so without this a test would start with the previous test's coils still
    /// flying and every LiveCount assertion would be reading someone else's springs.
    /// </summary>
    public sealed class SpringGutsPlayTests
    {
        private SpringGuts _director;

        [SetUp]
        public void SetUp()
        {
            ClearDirectors();
            _director = new GameObject("SpringGuts(Test)").AddComponent<SpringGuts>();
        }

        [TearDown]
        public void TearDown() => ClearDirectors();

        /// <summary>Destroying the director takes its springs with it — they are its children.</summary>
        private static void ClearDirectors()
        {
            foreach (var d in Object.FindObjectsByType<SpringGuts>(FindObjectsInactive.Include,
                                                                   FindObjectsSortMode.None))
                Object.DestroyImmediate(d.gameObject);
        }

        [Test]
        public void Install_CreatesADirector_AndRefusesToCreateASecond()
        {
            // The self-install path (docs/CODE_DRIVEN_SCENES.md: nothing may need scene wiring),
            // driven directly rather than waiting on the attribute — this way the idempotency guard
            // gets tested too, which is the half that actually has logic in it.
            ClearDirectors();

            var install = typeof(SpringGuts).GetMethod("Install",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(install, "SpringGuts.Install went missing — self-install is the convention");

            install.Invoke(null, null);
            Assert.AreEqual(1, Object.FindObjectsByType<SpringGuts>(FindObjectsSortMode.None).Length,
                "should install itself with no scene wiring");

            install.Invoke(null, null);
            Assert.AreEqual(1, Object.FindObjectsByType<SpringGuts>(FindObjectsSortMode.None).Length,
                "installing twice must not leave two directors fighting over the same kills");
        }

        [UnityTest]
        public IEnumerator AKill_ThrowsSprings()
        {
            HudSignals.EmitEnemyKilled(new Vector3(0f, 0.6f, 4f));
            yield return null;

            Assert.AreEqual(SpringGuts.PerDeath, _director.LiveCount,
                "a dead robot should throw exactly its allotted coils");
        }

        [UnityTest]
        public IEnumerator Springs_AreDrawable_NotMagenta()
        {
            HudSignals.EmitEnemyKilled(new Vector3(0f, 0.6f, 4f));
            yield return null;

            var renderer = _director.GetComponentInChildren<MeshRenderer>();
            Assert.IsNotNull(renderer, "no spring renderer was ever built");

            // The YT-58 trap: a primitive left on its default material ships magenta in the WebGL
            // build, because that shader is not in the build's shader set.
            Assert.IsNotNull(renderer.sharedMaterial,
                "no material — the spring would draw as nothing, or as magenta");
            Assert.IsNotNull(renderer.GetComponent<MeshFilter>().sharedMesh, "no mesh to draw");
        }

        [UnityTest]
        public IEnumerator ACrowdDyingAtOnce_CannotExceedTheGlobalCap()
        {
            // Forty deaths a frame, for a second. Far past anything the slice can produce — the point
            // is that the ceiling is a ceiling, not a number that happens to be big enough.
            for (int frame = 0; frame < 60; frame++)
            {
                for (int i = 0; i < 40; i++)
                    HudSignals.EmitEnemyKilled(new Vector3(i * 0.5f, 0.6f, 0f));

                yield return null;

                Assert.LessOrEqual(_director.LiveCount, SpringGuts.Capacity,
                    $"frame {frame}: springs must never exceed the global cap");
            }

            // And the pool behind them is bounded too — LiveCount only counts coils in the air, so
            // without this a leak would hide as a pile of retired-but-allocated GameObjects.
            Assert.LessOrEqual(_director.GetComponentsInChildren<MeshFilter>(true).Length,
                SpringGuts.Capacity, "the pool allocated more transforms than its capacity");
        }

        [UnityTest]
        public IEnumerator OneFrame_OnlyDressesSoManyDeaths()
        {
            // A boss AoE can kill a pile of robots on a single frame. Every one of them scattering is
            // noise, and the Craft Bible is explicit that juice must never obscure the read.
            for (int i = 0; i < 12; i++)
                HudSignals.EmitEnemyKilled(new Vector3(i, 0.6f, 0f));

            yield return null;

            Assert.AreEqual(SpringGuts.DeathsPerFrame * SpringGuts.PerDeath, _director.LiveCount,
                "only the first few deaths on a frame should throw coils");
        }

        [UnityTest]
        public IEnumerator Springs_AllCleanUp_AfterTheirLifetime()
        {
            HudSignals.EmitEnemyKilled(new Vector3(0f, 0.6f, 4f));
            yield return null;
            Assert.Greater(_director.LiveCount, 0, "nothing was thrown to begin with");

            // Past the longest life any spring is given. "They clean up" is half the AC, and a leak
            // here would be invisible until a long run had littered the yard with hundreds of coils.
            float t = 0f;
            while (t < 4f && _director.LiveCount > 0)
            {
                t += Time.deltaTime;
                yield return null;
            }

            Assert.AreEqual(0, _director.LiveCount, "every spring should have retired itself");
        }

        [Test]
        public void Director_Unsubscribes_WhenDestroyed()
        {
            Object.DestroyImmediate(_director.gameObject);
            _director = null;

            // HudSignals is static. If OnDisable missed its -=, this raise would reach a destroyed
            // director and throw — which is exactly how a static bus leaks a scene's objects.
            Assert.DoesNotThrow(() => HudSignals.EmitEnemyKilled(Vector3.zero),
                "a destroyed director must not still be listening");
        }
    }
}
