using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Rendering;

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
    /// <c>s_clu</c> (cluster bomblets on impact) has no dedicated RIG node — the SECONDARY column had no
    /// remaining slot wide enough for a fourth track without crowding into the ENERGY family's own node
    /// spacing (rig_board.json's node-spacing convention, ~120px between siblings). Folded onto a maxed
    /// Salvo track instead, as a capstone; a real node can follow once MV-689 gives THE RIG's buy-flow
    /// its own layout pass.
    ///
    /// DEVIATION from the ticket's literal step 3: the placeholder mesh is built directly on THIS
    /// component (Max's own GameObject), not threaded through <see cref="MaxWorlds.VFX.MaxRig"/>'s
    /// private <c>Build()</c>/pivot hierarchy — reaching into that would need Awake to run (it doesn't,
    /// in EditMode) or a reflection-driven partial rebuild, for a mesh the [ART] ticket replaces anyway.
    /// The cost: <see cref="MaxWorlds.Rendering.CharacterSkinDirector"/> repaints every renderer under
    /// an <c>IDamageable</c> (Max included), so this greybox placeholder tints with him rather than
    /// keeping its own gunmetal, exactly the trap <c>MaxRig</c>'s own class doc warns about. Acceptable
    /// for a greybox-only placeholder; migrate onto a real <c>MaxRig</c> mount if the tint bothers the
    /// art pass.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class ShoulderRack : MonoBehaviour
    {
        private const float RangeMeters = 12f;
        private const float RocketSpeed = 14f;

        private static readonly Collider[] s_hits = new Collider[16];

        private static readonly Color RackColor = new Color(0.35f, 0.36f, 0.4f);

        // Starts at the base reload so the very first salvo waits a full reload window rather than
        // firing on the first tick — a field initializer, not Awake, since EditMode never runs Awake
        // (MV694ShoulderRackTests adds this component directly).
        private float _reloadCooldown = AbilityTuning.DefaultShoulderRackBaseReloadSeconds;

        private GameObject _mount;

        /// <summary>The placeholder mesh's root — <c>MV694ShoulderRackTests</c> reads
        /// <c>activeInHierarchy</c> off this directly (AC2).</summary>
        public GameObject MountForTests => _mount;

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The auto-fire salvo loop, pulled out of <see cref="Update"/> as an explicit-dt
        /// method so a test can drive deterministic time without a live PlayerLoop — same split
        /// <see cref="MaxWorlds.Arena.Sentinel"/>'s own fire-cooldown tick uses. Public for
        /// <c>MV694ShoulderRackTests</c>, same visibility <see cref="MaxWorlds.Enemies.HomingMissile.ClearTrailForRespawn"/>
        /// uses for its own test-only entry point.</summary>
        public void Tick(float dt)
        {
            int rocketLevel = WeaponSystemState.ShoulderRackTrackLevel(ShoulderRackTrackKind.RocketDamage);
            RefreshMountVisibility(rocketLevel);

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

        /// <summary>Builds the mount the first time it's actually needed and syncs its visibility to
        /// whether the rack is bought (<paramref name="rocketLevel"/> &gt;= 1) — AC2's "shown only when
        /// bought." Lazy, same "most sessions never touch this" rationale
        /// <see cref="PlayerAbilities.SplashVfx"/> uses: building the placeholder geometry for every Max
        /// who never buys the rack (everyone, for the whole of World 1) would be pure waste.</summary>
        private void RefreshMountVisibility(int rocketLevel)
        {
            if (rocketLevel < 1)
            {
                if (_mount != null) _mount.SetActive(false);
                return;
            }

            EnsureMount();
            _mount.SetActive(true);
        }

        private void EnsureMount()
        {
            if (_mount != null) return;

            _mount = new GameObject("ShoulderRackMount");
            _mount.transform.SetParent(transform, worldPositionStays: false);
            // Roughly the right shoulder of a ~1.9m-tall Max, forward of the torso.
            _mount.transform.localPosition = new Vector3(0.32f, 1.35f, 0.05f);
            _mount.AddComponent<KeepsOwnMaterial>();

            Material mat = MaterialLibrary.Tinted(SurfaceKind.Metal, RackColor);

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "RackBody";
            StripCollider(body);
            body.transform.SetParent(_mount.transform, false);
            body.transform.localScale = new Vector3(0.35f, 0.12f, 0.12f);
            if (mat != null) body.GetComponent<MeshRenderer>().sharedMaterial = mat;

            for (int i = 0; i < 3; i++)
            {
                var tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tube.name = "Tube";
                StripCollider(tube);
                tube.transform.SetParent(_mount.transform, false);
                tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tube.transform.localScale = new Vector3(0.06f, 0.2f, 0.06f);
                tube.transform.localPosition = new Vector3(0f, (i - 1) * 0.09f, 0.1f);
                if (mat != null) tube.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        private static void StripCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return;
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
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

            bool cluster = salvoLevel >= WeaponCatalog.MaxLevel(ShoulderRackTrackKind.Salvo);

            for (int i = 0; i < salvoCount; i++)
                PlayerRocket.Fire(transform.position, target.transform, RocketSpeed, damage, splash, cluster);
        }
    }
}
