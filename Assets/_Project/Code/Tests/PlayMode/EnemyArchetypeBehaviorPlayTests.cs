using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Enemies;
using MaxWorlds.Player;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The three new archetypes (MV-293) against real Update() ticks, not just their tuning data —
    /// EnemyArchetypeTests pins the NUMBERS, this proves the numbers actually produce the behaviour
    /// the ticket asks for: a Gunner that backs off and lands a beam, a Bomber whose missile actually
    /// reaches the player, a Blinker that closes the gap by cheating instead of walking it.
    /// </summary>
    public sealed class EnemyArchetypeBehaviorPlayTests
    {
        private static void Set(object o, string field, object value) =>
            o.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
             .SetValue(o, value);

        private static float FlatDistance(Vector3 a, Vector3 b) =>
            Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        /// <summary>A bare "Player"-tagged transform — enough for chase/sight/standoff tests that
        /// don't need to be damaged.</summary>
        private static GameObject NewMaxMarker(Vector3 pos)
        {
            var go = new GameObject("Max");
            go.tag = "Player";
            go.transform.position = pos;
            return go;
        }

        /// <summary>The real, damageable Max (same construction as GroundAnchorPlayTests' contract
        /// test) — for the two tests that need a hit to actually register.</summary>
        private static GameObject NewRealMax(Vector3 pos, out PlayerHealth health)
        {
            var go = new GameObject("Max");
            go.tag = "Player";
            go.transform.position = pos;
            go.AddComponent<CharacterController>();
            go.AddComponent<PlayerController>();
            health = go.AddComponent<PlayerHealth>();
            return go;
        }

        private static RobotEnemy NewRobotAt(Vector3 pos, EnemyArchetype archetype)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Robot ({archetype.Kind})";
            go.transform.position = pos;
            go.AddComponent<CharacterController>();
            var e = go.AddComponent<RobotEnemy>();
            e.Apply(archetype);
            return e;
        }

        [UnityTest]
        public IEnumerator Gunner_BacksOffWhenMaxGetsInsideItsStandoffRange()
        {
            var max = NewMaxMarker(Vector3.zero);
            var gunner = NewRobotAt(new Vector3(0f, 0.7f, 2f), EnemyArchetype.Gunner); // inside the 4.5 m band

            float start = FlatDistance(gunner.transform.position, max.transform.position);
            yield return new WaitForSeconds(0.5f);
            float end = FlatDistance(gunner.transform.position, max.transform.position);

            Object.Destroy(gunner.gameObject);
            Object.Destroy(max);

            Assert.Greater(end, start, "a Gunner caught too close to Max must back off, not close the gap");
        }

        [UnityTest]
        public IEnumerator Gunner_LandsItsBeamOnAStationaryTarget()
        {
            var max = NewRealMax(new Vector3(0f, 1f, 6f), out var health); // inside firing range, outside standoff
            var gunner = NewRobotAt(Vector3.zero, EnemyArchetype.Gunner);

            // Telegraph (0.5s) then a 1.1s beam — give it real margin either side.
            yield return new WaitForSeconds(2.2f);

            Object.Destroy(gunner.gameObject);
            Object.Destroy(max);

            Assert.Less(health.Current, health.Max, "the Gunner's beam never landed on a target it had a clear shot at");
        }

        [UnityTest]
        public IEnumerator Bomber_HomingMissileReachesAStationaryTarget()
        {
            var max = NewRealMax(new Vector3(0f, 1f, 6f), out var health); // inside firing range, outside standoff
            var bomber = NewRobotAt(Vector3.zero, EnemyArchetype.Bomber);

            // Telegraph (0.7s) + missile flight (6 m at 4.5 m/s ≈ 1.3s) — generous margin.
            yield return new WaitForSeconds(3f);

            Object.Destroy(bomber.gameObject);
            Object.Destroy(max);

            Assert.Less(health.Current, health.Max, "the Bomber's missile never reached a stationary target");
        }

        /// <summary>MV-329 AC2: the missile HomingMissile.Fire spawns has to actually read as ordnance —
        /// a shaft, fins and a warhead band — not the plain sphere "ball" it used to fire, and every
        /// part of it has to carry a real material or it draws magenta in the build (YT-58).</summary>
        [UnityTest]
        public IEnumerator Bomber_MissileVisualIsAMissile_NotABall()
        {
            var target = NewMaxMarker(new Vector3(0f, 1f, 10f));
            var missile = HomingMissile.Fire(Vector3.zero, target.transform, 4.5f, 22f, 2f);
            yield return null;

            var renderers = missile.GetComponentsInChildren<MeshRenderer>();
            var names = renderers.Select(r => r.name).ToArray();

            Assert.Contains("Shaft", names, "the missile has no Shaft — it's still a bare primitive.");
            Assert.Contains("Fin", names, "the missile has no tail fins — it reads as a ball, not ordnance.");

            foreach (var r in renderers)
            {
                Assert.IsNotNull(r.sharedMaterial, $"'{r.name}' has no material — it draws nothing.");
                Assert.That(r.sharedMaterial.shader.name,
                    Does.StartWith("Universal Render Pipeline").Or.StartWith("MaxWorlds").Or.StartWith("Sprites"),
                    $"'{r.name}' is wearing '{r.sharedMaterial.shader.name}': magenta in the build.");
            }

            Object.Destroy(missile.gameObject);
            Object.Destroy(target);
        }

        [UnityTest]
        public IEnumerator Blinker_ClosesTheGapByTeleportingRatherThanWalkingIt()
        {
            var max = NewMaxMarker(new Vector3(0f, 1f, 10f)); // well outside lunge range — a plain
                                                                // walk can't meaningfully close this
            var blinker = NewRobotAt(Vector3.zero, EnemyArchetype.Blinker);

            // Shrink the cooldown/charge-up so the test doesn't have to wait out the authored 4.5s —
            // then re-seed the timer the shrunk cooldown feeds, same as Apply() would have.
            Set(blinker, "teleportCooldown", 0.05f);
            Set(blinker, "telegraphTime", 0.05f);
            blinker.ResetState();

            float start = FlatDistance(blinker.transform.position, max.transform.position);
            yield return new WaitForSeconds(0.5f);
            float end = FlatDistance(blinker.transform.position, max.transform.position);

            Object.Destroy(blinker.gameObject);
            Object.Destroy(max);

            // 0.5s of ordinary chase speed (~2.4 m/s) covers little over 1 m; a teleport lands it
            // roughly lungeRange*0.85 (~1.9 m) from Max. Either way this only holds if it blinked.
            Assert.Less(end, start - 3f, "the Blinker should have closed most of a 10 m gap by now — it didn't blink");
        }

        /// <summary>
        /// MV-330: the reposition in <c>RobotEnemy.TickTeleport</c> was a silent, single-frame snap —
        /// nothing ever announced it, so the VFX layer had nothing to hook into and the blink read as
        /// a bug. This proves the real state machine (not a synthetic Emit call) raises the signal,
        /// and that it carries two genuinely different points — the departure and the landing spot —
        /// not the same position twice.
        /// </summary>
        [UnityTest]
        public IEnumerator Blinker_AnnouncesItsTeleport_WithDistinctFromAndToPoints()
        {
            var max = NewMaxMarker(new Vector3(0f, 1f, 10f));
            var blinker = NewRobotAt(Vector3.zero, EnemyArchetype.Blinker);

            Set(blinker, "teleportCooldown", 0.05f);
            Set(blinker, "telegraphTime", 0.05f);
            blinker.ResetState();

            Vector3? from = null, to = null;
            int fireCount = 0;
            System.Action<Vector3, Vector3> onTeleport = (f, t) => { fireCount++; from = f; to = t; };
            HudSignals.BlinkerTeleported += onTeleport;

            try
            {
                yield return new WaitForSeconds(0.5f);
            }
            finally
            {
                HudSignals.BlinkerTeleported -= onTeleport;
                Object.Destroy(blinker.gameObject);
                Object.Destroy(max);
            }

            Assert.AreEqual(1, fireCount, "the Blinker's teleport should announce exactly once per blink");
            Assert.IsTrue(from.HasValue && to.HasValue, "the teleport signal never fired");
            Assert.Greater(Vector3.Distance(from.Value, to.Value), 1f,
                "the signal's from/to are (near) the same point — the VFX would draw a surge and both " +
                "flashes on top of each other instead of at the two ends of the blink");
        }
    }
}
