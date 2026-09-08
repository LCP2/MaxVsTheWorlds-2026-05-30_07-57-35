using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Feel;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// UNDERTOW's cavitation implosion (MV-714) — the resolved effect a <see cref="CavitationBubble"/>
    /// triggers on impact or at max range: modest direct damage, a pull toward the impact point, and a
    /// stagger, applied to every robot within <c>pullRadius</c>. Pulled out as its own public, pure-ish
    /// static entry point (the same "testable outside the projectile's own flight" shape
    /// <see cref="MaxWorlds.Combat.SprayHit"/> gives the RCDA's cone test) so an EditMode test can drive
    /// it directly against seeded robots without simulating <see cref="CavitationBubble"/>'s actual
    /// flight — AC3 only cares about the resolved implosion, not how the bubble got there.
    /// </summary>
    public static class CavitationImplosion
    {
        private const int HitBufferSize = 16;
        private static readonly Collider[] s_hits = new Collider[HitBufferSize];

        public static void Apply(Vector3 point, float damage, float pullRadius, float pullDistance, float staggerSeconds)
        {
            int count = Physics.OverlapSphereNonAlloc(point, pullRadius, s_hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                bool isRobot = s_hits[i].TryGetComponent<RobotEnemy>(out var robot);

                // MV-716 Override: a port-exposed robot (below 25% HP) within OverrideRadius of the
                // implosion converts to Max's side INSTEAD of taking this same shot's damage — the whole
                // point is to flip a critically-damaged robot rather than finish it off, so a successful
                // conversion skips the ordinary damage/pull/stagger path below entirely. TryConvert
                // itself gates on health/cap; only the distance half of the trigger is checked here,
                // since only the caller knows the implosion point.
                if (isRobot && Vector3.Distance(point, robot.transform.position) <= RobotEnemy.OverrideRadius &&
                    robot.TryConvert())
                {
                    continue;
                }

                if (!s_hits[i].TryGetComponent<IDamageable>(out var d) || !d.IsAlive) continue;
                if (!DamageRules.Applies(Team.Player, d.Team)) continue;

                d.TakeDamage(new DamageInfo(damage, point, Vector3.up, Team.Player, source: DamageSource.PrimaryWeapon));

                // Grouping, not the kill (spec) — the pull/stagger only exists for robots, not gates or
                // other non-RobotEnemy damageables.
                if (isRobot)
                {
                    robot.ApplyPull(point, pullDistance);
                    robot.Stun(staggerSeconds);
                }
            }

            // Cosmetic only, and gated to Play mode: UndertowTests (AC3) calls this method directly
            // against seeded EditMode robots to assert the resolved pull/stagger without simulating
            // CavitationBubble's actual flight, and neither the ring nor a hit-stop's Coroutine has any
            // business creating scene objects or touching Time.timeScale outside a real running game.
            if (Application.isPlaying)
            {
                CavitationImpactVfx.PlayImplosionRing(point, pullRadius);

                // 30ms hit-stop on the implosion only (spec) — same FindFirstObjectByType lookup
                // MaxWorlds.VFX.BossSpectacle uses for a one-off request outside GameFeel's own ownership.
                var stop = Object.FindFirstObjectByType<HitStop>();
                stop?.Request(0.03f, 0.12f);
            }
        }
    }

    /// <summary>The implosion's impact ring — same reused-<see cref="GroundRing"/> shape as
    /// <see cref="RocketImpactVfx"/>, cyan-white instead of scorch-orange so UNDERTOW doesn't read as the
    /// Shoulder Rack's own splash. The ticket's caustic ribbon (along the lance) and water-caustic decal
    /// under the implosion are deferred — greybox slice, no AC covers them, and the ring plus the
    /// hit-stop are the two cheapest wins from the ticket's own priority-ordered VFX list.</summary>
    internal static class CavitationImpactVfx
    {
        private const float RingLifetimeSeconds = 0.3f;
        private const float RingStartRadius = 0.05f;

        public static void PlayImplosionRing(Vector3 point, float maxRadius)
        {
            var ring = GroundRing.Create("CavitationImplosionRing");
            ring.gameObject.AddComponent<RingLifetime>().Begin(ring, point, Mathf.Max(0.3f, maxRadius), RingLifetimeSeconds);
        }

        private sealed class RingLifetime : MonoBehaviour
        {
            private GroundRing _ring;
            private Vector3 _origin;
            private float _maxRadius;
            private float _life;
            private float _t;
            private static readonly Color Color0 = new Color(0.55f, 0.95f, 1f, 0.85f);

            public void Begin(GroundRing ring, Vector3 origin, float maxRadius, float life)
            {
                _ring = ring;
                _origin = origin;
                _maxRadius = maxRadius;
                _life = life;
                _ring.Show(_origin, RingStartRadius, Color0);
            }

            private void Update()
            {
                _t += Time.deltaTime;
                float f = Mathf.Clamp01(_t / _life);
                _ring.Show(_origin, Mathf.Lerp(RingStartRadius, _maxRadius, f),
                    new Color(Color0.r, Color0.g, Color0.b, Color0.a * (1f - f)));
                if (f >= 1f)
                {
                    _ring.Hide();
                    Destroy(gameObject);
                }
            }
        }
    }
}
