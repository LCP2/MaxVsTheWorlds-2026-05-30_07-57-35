using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// World 2's Shoulder Rack (MV-694) — a mini rocket launcher on Max's shoulder that fires itself at
    /// the nearest awake robot in range, one thumb never leaving the stick. Self-attaches from
    /// <see cref="MaxWorlds.Player.PlayerController.Awake"/>, same code-driven-scenes rule
    /// <see cref="PlayerAbilities"/> follows, and is a total no-op while
    /// <see cref="WeaponSystemState.SecondaryKind"/> still reads <see cref="SecondaryKind.WaterBalloon"/>
    /// (World 1) or the rack's own root track (<c>s_rkt</c>) is unowned — MV-689's morph and THE RIG
    /// purchase are what flip those, neither of which this ticket implements.
    ///
    /// <c>s_clu</c> (cluster bomblets on impact) has its own dedicated RIG node on
    /// <c>rig_board.world2.json</c>, a child of <c>s_rld</c> (MV-768: this comment was true of World 1's
    /// board only — World 2's board defines <c>s_clu</c>, and this class had never been updated to read
    /// it, so the bomblet code below ran off a maxed Salvo track instead and the node itself did
    /// nothing).
    ///
    /// MV-702 resolves the deviation this class used to carry: the mount mesh now lives on
    /// <see cref="MaxWorlds.VFX.MaxRig"/> (built once in <see cref="MaxWorlds.VFX.MaxBody.Build"/>,
    /// alongside the LPPE gadget it's welded to), reading <see cref="IsBought"/>/
    /// <see cref="ReloadFraction01"/> off this component instead of owning a placeholder GameObject
    /// itself — which also fixes the tint bug the old placeholder had (built on Max's own
    /// <c>IDamageable</c> GameObject, <see cref="MaxWorlds.Rendering.CharacterSkinDirector"/> repainted
    /// it flat with him; <c>MaxRig</c> is a scene-root object outside any <c>IDamageable</c>, exactly
    /// the trap its own class doc warns about).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ShoulderRack : MonoBehaviour
    {
        private const float RangeMeters = 12f;
        private const float RocketSpeed = 14f;

        private static readonly Collider[] s_hits = new Collider[16];

        // Starts at the base reload so the very first salvo waits a full reload window rather than
        // firing on the first tick — a field initializer, not Awake, since EditMode never runs Awake
        // (MV694ShoulderRackTests adds this component directly).
        private float _reloadCooldown = AbilityTuning.DefaultShoulderRackBaseReloadSeconds;

        /// <summary>Whether the rack is bought — <c>s_rkt</c> reaches L1 — the resolved value
        /// <see cref="MaxWorlds.VFX.MaxRig"/> reads to show/hide its mount mesh (AC2, carried over from
        /// MV-694's own placeholder).</summary>
        public bool IsBought => WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.RocketDamage) >= 1;

        /// <summary>0 right after a salvo fires, 1 once the reload window has fully elapsed — what
        /// <c>MaxRig</c> lerps its tube glow across ("tubes glow as they reload"). 0 while unbought.</summary>
        public float ReloadFraction01
        {
            get
            {
                if (!IsBought) return 0f;
                float total = ReloadSecondsNow(WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.RocketDamage));
                return total <= 0f ? 1f : 1f - Mathf.Clamp01(_reloadCooldown / total);
            }
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The auto-fire salvo loop, pulled out of <see cref="Update"/> as an explicit-dt
        /// method so a test can drive deterministic time without a live PlayerLoop — same split
        /// <see cref="MaxWorlds.Arena.Sentinel"/>'s own fire-cooldown tick uses. Public for
        /// <c>MV694ShoulderRackTests</c>, same visibility <see cref="MaxWorlds.Enemies.HomingMissile.ClearTrailForRespawn"/>
        /// uses for its own test-only entry point.</summary>
        public void Tick(float dt)
        {
            int rocketLevel = WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.RocketDamage);

            if (WeaponSystemState.SecondaryKind != SecondaryKind.ShoulderRack) return;
            if (rocketLevel < 1) return;   // s_rkt unowned — the rack isn't bought yet

            _reloadCooldown -= dt;
            if (_reloadCooldown > 0f) return;

            RobotEnemy target = NearestAwakeRobotInRange();
            if (target == null) return;   // no target — stays loaded, doesn't burn parts waiting

            // A salvo costs one cell; with none left the rack stays silent (HUD reads EMPTY) — but the
            // reload window still elapsed, so the next attempt is another full ReloadSeconds away, not
            // a busy-retry every frame.
            if (!PickupWallet.TrySpendPowerCellSecondary())
            {
                _reloadCooldown = ReloadSecondsNow(rocketLevel);
                return;
            }

            FireSalvo(target, rocketLevel);
            _reloadCooldown = ReloadSecondsNow(rocketLevel);
        }

        private static float ReloadSecondsNow(int rocketLevel) => AbilityTuning.ShoulderRackReloadSeconds(
            WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.Reload),
            AbilityTuning.DefaultShoulderRackBaseReloadSeconds,
            AbilityTuning.DefaultShoulderRackReloadFloorSeconds,
            WeaponCatalog.MaxLevel(ShoulderRackTrackKind.Reload));

        private RobotEnemy NearestAwakeRobotInRange()
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position, RangeMeters, s_hits, ~0, QueryTriggerInteraction.Ignore);

            RobotEnemy best = null;
            float bestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (s_hits[i] == null) continue;
                if (!s_hits[i].TryGetComponent<RobotEnemy>(out var robot)) continue;
                if (!robot.IsAlive || robot.IsDormant) continue;

                float d = (robot.transform.position - transform.position).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = robot; }
            }
            return best;
        }

        private void FireSalvo(RobotEnemy target, int rocketLevel)
        {
            int salvoLevel = WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.Salvo);
            int salvoCount = AbilityTuning.ShoulderRackSalvoCount(salvoLevel, AbilityTuning.DefaultShoulderRackMaxSalvoCount);

            float damage = AbilityTuning.ShoulderRackRocketDamage(
                rocketLevel, AbilityTuning.DefaultShoulderRackBaseDamage, AbilityTuning.DefaultShoulderRackDamagePerLevel);

            // Shared with the Water Balloon (both are SECONDARY-family splash weapons off s_spl) — see
            // ShoulderRackTrackKind's class doc for why this reads WaterBalloonTrackLevel directly
            // rather than a fourth Shoulder Rack track.
            float splash = AbilityTuning.ShoulderRackSplashRadius(
                WeaponSystemState.WaterBalloonTrackLevel(WaterBalloonTrackKind.SplashArea),
                AbilityTuning.DefaultShoulderRackBaseSplashRadius, AbilityTuning.DefaultShoulderRackMaxSplashRadius,
                WeaponCatalog.MaxLevel(WaterBalloonTrackKind.SplashArea));

            // MV-768: gated on s_clu's own level, the same way s_spl/s_sal/s_rld are read -- not a
            // maxed Salvo track (the stale pre-fix shape this class's own doc used to describe).
            bool cluster = WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.Cluster) >= 1;

            for (int i = 0; i < salvoCount; i++)
                PlayerRocket.Fire(transform.position, target.transform, RocketSpeed, damage, splash, cluster);
        }
    }
}
